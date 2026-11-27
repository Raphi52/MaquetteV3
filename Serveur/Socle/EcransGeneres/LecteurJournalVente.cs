using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Journal de vente » (PROC_JVENTE) : les articles vendus (factures du greffe) sur la periode choisie, et leurs totaux. Mode
/// d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 13, 27/11/2026. Lecture seule : RIG v2 n'ecrit rien ici.
///
/// SOURCE (ancien RIG) : PROC_JVENTE ne porte aucune requete (proc_jvente.md:182, Detail OPE) ; il remplit un JventeParam et le
/// passe a JournalVente.GenererDonneesRapport (D:\RigApplication\Source\DllRapportCrystal\RigCrystalFactu\JournalVente.cs:106-410),
/// qui porte la requete. La V3 reprend l'edition « Détaillée » (JVENTE_TYPE_EDITION par defaut, OPE_EDITION.cs:34-37) triee « par
/// date » (OPE_TRI.cs:50-51), avec les cases des filtres a leur valeur initiale (OPE_FILTRES.Designer.cs:106-274 : tous les types
/// de client, articles « Facturés au greffe » et « Web » coches, « Tarifés » NON coche, factures INPI et autres cochees) :
///   ARTICLE_UTILISE INNER JOIN CLIENT (ARTUT_ID_CLIEN) LEFT JOIN ARTICLE (ARTCL_CODE_ARTICLE_RIG = ARTUT_CODE_ARTICLE) LEFT JOIN
///   FACTURE (ARTUT_NUMERO_FACTURE) (:284-287) ; le code article affiche : ISNULL(ARTCL_CODE_ARTICLE_GREFFE, ARTUT_CODE_ARTICLE) (:262) ;
///   la periode (:297-303) : un article facture par la date de sa FACTURE (FACTU_DATE_CREATION, 0 h - 24 h, numero de facture
///   renseigne), un article tarife non facture par sa date (ARTUT_DATE_CREATION, numero vide) ;
///   le type d'article (:308-339) : « Web » = code WEB, « Tarifés » = sans facture, « Facturés au greffe » = autre code, facture ;
///   ORDER BY ARTUT_DATE_CREATION, ARTUT_NUMERO_FACTURE, ARTUT_ID_ARTUT (:407). La jointure ARTICLE de la V2 peut doubler une ligne
///   (plusieurs versions d'un code) et son GROUP BY la refond (:402-403) : la V3 prend UNE version (OUTER APPLY TOP 1).
/// Dans la base V3, CLIENT est la fiche rec.Client (REC-013) et UTILISATEUR le compte soc.Compte (IdV2).
///
/// PARAMETRES (IRechercheEcran ; les Parametres du descripteur, que l'Etat envoie a la base) :
///   « Période » « du|au » (OPE_CRITERESGENERAUX.cs:27-30, aujourd'hui par defaut ; une borne vide reprend aujourd'hui) ;
///   « Utilisateurs » : l'agent de l'article (ARTUT_ID_UTLST IN, :118-136), cherche sur son nom, son prenom ou son compte ;
///   « Abrégé client » : CLIEN_ABREGE LIKE (:206-207 ; il remplace les types de client, OPE_FILTRES.cs:187-205) ;
///   « Code ou libellé article » : ARTUT_CODE_ARTICLE ou ARTUT_LIBELLE LIKE (:343-346) ;
///   « Type d'article » : « Facturés au greffe », « Web » ou « Tarifés », un a la fois (les trois cases, :308-339) ;
///   « Taux de TVA » : ARTUT_TAUX_TVA = (:350-372 ; un taux a la fois) ;
///   « Tri » : « Par date » (:407) ou « Par article » (:408).
/// Le champ de recherche cherche dans le code, le libelle ou le client.
/// Non repris (nommes) : les editions « Par titre », « Résumée », « Résumée par titre » et « Par facture » (OPE_EDITION.cs:74-93,
/// JournalVente.cs:211-245, :385-389, :413-450) ; les types de client hors abrege (Divers, En compte, Greffes, PC, Web, Internes,
/// :145-205) et les types de facture INPI / autres (:376-381) -- tous coches par defaut, donc sans filtre a l'ouverture ;
/// l'affichage commentaires / libelles (JVENTE_AFF_ARTICLE).
///
/// GROUPES : les trois types d'article de la V2 (« Facturés au greffe », « Web », « Tarifés », OPE_FILTRES.Designer.cs:151-171).
/// REGLE D'ETAT : sans facture -> AFaire « Non facturé » ; facture non comptabilisee (FACTU_DATE_COMPTABILISATION vide) -> EnCours
/// « Facturé » ; facture comptabilisee -> Validee « Comptabilisé ».
/// VALEURS : dans l'ordre des Colonnes (Date, Facture, Code article, Libellé, Client, Quantité, Montant HT, Montant TTC). Le montant
/// HT est ARTUT_PRIX_UHT x ARTUT_QUANTITE, le calcul de la V2 pour une edition qui totalise (:258, :454) ; montants en decimal exact.
/// </summary>
internal sealed class LecteurJournalVente : IRechercheEcran
{
    public string Procedure => "PROC_JVENTE";

    internal const string Facture = "Facturés au greffe", Web = "Web", Tarife = "Tarifés";

    internal const string CriterePeriode = "Période", CritereUtilisateurs = "Utilisateurs", CritereClient = "Abrégé client",
                          CritereArticle = "Code ou libellé article", CritereType = "Type d'article", CritereTva = "Taux de TVA", CritereTri = "Tri";
    internal const string TriDate = "Par date", TriArticle = "Par article";

    public IReadOnlyCollection<string> Criteres { get; } = [CriterePeriode, CritereUtilisateurs, CritereClient, CritereArticle, CritereType, CritereTva, CritereTri];

    private const string Requete = """
        SELECT TOP (@plafond) au.ARTUT_ID_ARTUT, au.ARTUT_DATE_CREATION, au.ARTUT_NUMERO_FACTURE, au.ARTUT_LIBELLE, au.ARTUT_QUANTITE,
               au.ARTUT_PRIX_UHT, au.ARTUT_MONTANT_TTC, au.ARTUT_TAUX_TVA, au.ARTUT_CODE_ARTICLE,
               ISNULL(art.ARTCL_CODE_ARTICLE_GREFFE, au.ARTUT_CODE_ARTICLE) AS CodeArticle, c.Abrege, c.Nom AS ClientNom,
               f.FACTU_DATE_CREATION, f.FACTU_DATE_COMPTABILISATION,
               COUNT(*) OVER () AS Total
        FROM v2.ARTICLE_UTILISE AS au
        INNER JOIN rec.Client AS c ON c.Id = au.ARTUT_ID_CLIEN
        OUTER APPLY (SELECT TOP (1) a.ARTCL_CODE_ARTICLE_GREFFE FROM v2.ARTICLE AS a WHERE a.ARTCL_CODE_ARTICLE_RIG = au.ARTUT_CODE_ARTICLE
                     ORDER BY a.ARTCL_DATE_APPLICATION DESC, a.ARTCL_ID_ARTCL DESC) AS art
        LEFT JOIN v2.FACTURE AS f ON f.FACTU_NUMERO_FACTURE = au.ARTUT_NUMERO_FACTURE
        LEFT JOIN soc.Compte AS u ON u.IdV2 = au.ARTUT_ID_UTLST
        WHERE /*periode*/ /*filtres*/
        ORDER BY /*tri*/
        """;

    // La periode et le type d'article (JournalVente.cs:297-339), selon les types demandes : facture par la date de sa facture, tarife
    // par sa date.
    private const string PeriodeFacture = "(f.FACTU_DATE_CREATION >= @debut AND f.FACTU_DATE_CREATION < @finExclue AND au.ARTUT_NUMERO_FACTURE IS NOT NULL)";
    private const string PeriodeTarife = "(au.ARTUT_DATE_CREATION >= @debut AND au.ARTUT_DATE_CREATION < @finExclue AND au.ARTUT_NUMERO_FACTURE IS NULL)";

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, aujourdhui.Date, aujourdhui.Date, null, new FiltresRecherche(), TriDate, plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var (du, au) = LecteurJournalCaisse.Periode(recherche.Critere(CriterePeriode), aujourdhui);
        var f = new FiltresRecherche();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            f.Ajouter("au.ARTUT_CODE_ARTICLE LIKE @texte OR au.ARTUT_LIBELLE LIKE @texte OR c.Nom LIKE @texte OR c.Abrege LIKE @texte",
                ("@texte", FormeLigne.Contient(recherche.Texte)));
        if (recherche.Critere(CritereUtilisateurs) is { } agent)
            f.Ajouter("u.Nom LIKE @agent OR u.Prenom LIKE @agent OR u.CompteWindows LIKE @agent OR CONCAT(u.Prenom, ' ', u.Nom) LIKE @agent",
                ("@agent", FormeLigne.Contient(agent)));
        if (recherche.Critere(CritereClient) is { } client)
            f.Ajouter("c.Abrege LIKE @client", ("@client", FormeLigne.Contient(client)));
        if (recherche.Critere(CritereArticle) is { } article)
            f.Ajouter("au.ARTUT_CODE_ARTICLE LIKE @article OR au.ARTUT_LIBELLE LIKE @article", ("@article", FormeLigne.Contient(article)));
        if (recherche.Critere(CritereTva) is { } tva)
        {
            if (CriteresConsultation.Montant(tva) is { } taux) f.Ajouter("au.ARTUT_TAUX_TVA = @tva", ("@tva", taux));
            else f.Ajouter("1 = 0");
        }
        return Lire(baseGreffe, du, au, recherche.Critere(CritereType), f, recherche.Critere(CritereTri) ?? TriDate, plafond, ct);
    }

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, DateTime du, DateTime au, string? type, FiltresRecherche f, string tri,
                                         int plafond, CancellationToken ct)
    {
        // Sans type : les cases initiales de la V2 (« Facturés au greffe » et « Web », pas « Tarifés ») -> la periode des factures.
        var periode = type switch
        {
            Tarife => PeriodeTarife,
            Web => PeriodeFacture + " AND au.ARTUT_CODE_ARTICLE = 'WEB'",
            Facture => PeriodeFacture + " AND au.ARTUT_CODE_ARTICLE <> 'WEB'",
            null => PeriodeFacture,
            _ => "1 = 0",
        };
        var ordre = tri == TriArticle
            ? "au.ARTUT_CODE_ARTICLE, au.ARTUT_DATE_CREATION, au.ARTUT_NUMERO_FACTURE, au.ARTUT_ID_ARTUT"
            : "au.ARTUT_DATE_CREATION, au.ARTUT_NUMERO_FACTURE, au.ARTUT_ID_ARTUT";
        var sql = Requete.Replace("/*periode*/", periode, StringComparison.Ordinal).Replace("/*filtres*/", f.EnSuite, StringComparison.Ordinal)
                         .Replace("/*tri*/", ordre, StringComparison.Ordinal);
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct,
            f.Parametres(new SqlParameter("@plafond", plafond), new SqlParameter("@debut", du), new SqlParameter("@finExclue", au.AddDays(1))));
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var numero = FormeLigne.Texte(r, "ARTUT_NUMERO_FACTURE");
        var code = FormeLigne.Texte(r, "CodeArticle");
        var libelle = FormeLigne.Texte(r, "ARTUT_LIBELLE");
        var client = FormeLigne.Joindre(FormeLigne.Texte(r, "Abrege"), FormeLigne.Texte(r, "ClientNom"));
        var quantite = CriteresConsultation.EntierDe(r, "ARTUT_QUANTITE") ?? 0;
        var ht = (CriteresConsultation.Decimal(r, "ARTUT_PRIX_UHT") ?? 0m) * quantite;
        var ttc = CriteresConsultation.Decimal(r, "ARTUT_MONTANT_TTC");
        // La date de l'edition : celle de la facture pour un article facture (la date de la periode), la sienne sinon.
        var date = numero.Length > 0 ? FormeLigne.DateDe(r, "FACTU_DATE_CREATION") : FormeLigne.DateDe(r, "ARTUT_DATE_CREATION");
        var groupe = numero.Length == 0 ? Tarife : FormeLigne.Texte(r, "ARTUT_CODE_ARTICLE") == "WEB" ? Web : Facture;
        var (etat, libelleEtat) = numero.Length == 0 ? ("AFaire", "non facturé")
            : FormeLigne.DateDe(r, "FACTU_DATE_COMPTABILISATION") is null ? ("EnCours", "facturé") : ("Validee", "comptabilisé");
        var nom = FormeLigne.Joindre(code, libelle);
        var precisions = FormeLigne.Joindre(numero.Length > 0 ? $"facture {numero}" : null, $"le {FormeLigne.Date(date)}", client,
            $"{quantite} × {CriteresConsultation.Euros(CriteresConsultation.Decimal(r, "ARTUT_PRIX_UHT"))} HT",
            $"{CriteresConsultation.Euros(ttc)} TTC", libelleEtat);
        return new LigneLue(nom, precisions, groupe, etat, [])
        {
            Id = FormeLigne.Texte(r, "ARTUT_ID_ARTUT"),
            Valeurs = [FormeLigne.DateFiche(date), numero, code, libelle, client, quantite.ToString(System.Globalization.CultureInfo.InvariantCulture),
                       CriteresConsultation.MontantFiche(ht), CriteresConsultation.MontantFiche(ttc)],
        };
    }
}
