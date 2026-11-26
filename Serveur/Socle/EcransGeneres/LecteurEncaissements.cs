using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Encaissements » (PROC_RECHENC) : la recherche des encaissements du greffe et de leurs ventilations. Mode d'emploi :
/// LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 6, 26/11/2026. Lecture seule : RIG v2 n'ecrit rien ici.
///
/// SOURCE (ancien RIG) : PROC_RECHENC (D:\RigApplication\Source\RIG\DLL\Processus\PROC_RECHENC ; le Detail OPE de
/// proc_rechenc.md fait foi). La requete de OPE_CRITERES.GetValues (proc_rechenc.md:84-110, :262-272) :
///   ENCAISSEMENT LEFT JOIN VENTILATION (VENTI_ID_ENCMT) LEFT JOIN CLIENT (VENTI_ID_CLIEN) LEFT JOIN PAYEUR (VENTI_ID_PYEUR)
///   INNER JOIN UTILISATEUR (ENCMT_ID_UTLST), ORDER BY ENCMT_DATE_ENCAISSEMENT DESC, ENCMT_MONTANT, ENCMT_NUMERO_TITRE.
/// Une ligne par couple encaissement x ventilation, comme la grille de la V2 (proc_rechenc.md:274). Dans la base V3, CLIENT est
/// la fiche rec.Client (REC-013) et UTILISATEUR le compte soc.Compte (IdV2 = UTLST_ID_UTLST, SOC-001) : il est lu en LEFT JOIN,
/// pour qu'un encaissement dont l'agent n'est pas repris reste visible (la V2 ne le montrait pas, faute d'utilisateur valide).
///
/// LA LISTE A L'OUVERTURE : la V2 refuse une recherche sans critere (« Veuillez renseigner au moins un critere »,
/// ParamRecherche.ControlValues, proc_rechenc.md:120-125) ; ses deux dates sont pre-remplies a aujourd'hui (OPE_CRITERES.
/// DoChargerDonnees, proc_rechenc.md:56-66). La V3 ouvre sur ces criteres pre-remplis -- les encaissements du jour, de 0 h a
/// 24 h --, comme LecteurTribunalDigital ouvre sur les siens. La copie de developpement ne garde que les plus anciens : elle ne
/// rend aucun encaissement du jour ; la recherche par criteres les retrouve.
///
/// RECHERCHE (IRechercheEcran), les criteres de OPE_CRITERES (tableau de proc_rechenc.md:278-294), chacun en parametre :
///   Date de début / Date de fin (ENCMT_DATE_ENCAISSEMENT, bornes a 0 h et 24 h ; proc_rechenc.md:112-118), Numéro de titre
///   (LIKE), Mode de paiement (= code, ou son libelle), Banque (LIKE), Payeur (LIKE ENCMT_DESIGNATION_PAYEUR), Numéro de remise
///   (=), Commentaire (LIKE), Abrégé client (LIKE CLIEN_ABREGE), Nom du client (LIKE CLIEN_NOM), Montant minimum (>=), Montant
///   maximum (<=), Affaire (VENTI_ID_AFFRE =). Le champ de recherche cherche dans le titre, le payeur, la banque, le
///   commentaire ou le client (l'un OU l'autre). Non repris : la source et l'identifiant externe (ENCMT_EXT_*), l'utilisateur
///   et le tiers payeur (des listes de la V2, OPE_CRITERES.cs:131-160) -- a ajouter avec leurs listes.
///
/// GROUPES : « Encaissement ventilé » / « Encaissement sans ventilation ».
/// REGLE D'ETAT (la mise en rouge de OPE_RESULTAT._ChargerRowFromId, proc_rechenc.md:327) :
///   aucune ventilation, ou une ventilation a 0 -> AFaire « Sans ventilation » (la V2 l'ecrit en rouge) ;
///   ventilation non nulle                   -> Validee « Ventilé ».
/// « En cours » n'a pas de ligne : la V2 ne connait que ces deux cas.
///
/// FICHE (IFicheEcran), Id = ENCMT_ID_ENCMT : « Encaissement » (les colonnes de la grille V2, proc_rechenc.md:322-326) et
/// « Ventilations » (une ligne par ventilation, son client, son tiers payeur, son affaire, son montant). L'affaire s'ouvre dans
/// la V2 au double-clic (GCOM, proc_rechenc.md:331-336) : la V3 la nomme, sans l'ouvrir.
/// </summary>
internal sealed class LecteurEncaissements : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_RECHENC";

    internal const string Ventile = "Encaissement ventilé", SansVentilation = "Encaissement sans ventilation";

    internal const string CritereDebut = "Date de début", CritereFin = "Date de fin", CritereTitre = "Numéro de titre",
                          CritereMode = "Mode de paiement", CritereBanque = "Banque", CriterePayeur = "Payeur",
                          CritereRemise = "Numéro de remise", CritereCommentaire = "Commentaire", CritereAbrege = "Abrégé client",
                          CritereClient = "Nom du client", CritereMin = "Montant minimum", CritereMax = "Montant maximum",
                          CritereAffaire = "Affaire";

    internal const string SectionEncaissement = "Encaissement", SectionVentilations = "Ventilations";

    public IReadOnlyCollection<string> Criteres { get; } =
        [CritereDebut, CritereFin, CritereTitre, CritereMode, CritereBanque, CriterePayeur, CritereRemise, CritereCommentaire,
         CritereAbrege, CritereClient, CritereMin, CritereMax, CritereAffaire];

    private const string Requete = """
        SELECT TOP (@plafond) e.ENCMT_ID_ENCMT, e.ENCMT_NUMERO_TITRE, e.ENCMT_DATE_ENCAISSEMENT, e.ENCMT_MONTANT,
               e.ENCMT_DESIGNATION_PAYEUR, e.ENCMT_DESIGNATION_BANQUE, e.ENCMT_REMIS,
               COALESCE(mp.R_MODEPAIEME_LIBELLE_ECRAN, e.ENCMT_MODE_PAIEMENT) AS Mode,
               v.VENTI_ID_VENTI, v.VENTI_MONTANT, v.VENTI_ID_AFFRE, c.Abrege AS ClientAbrege, c.Nom AS ClientNom,
               COUNT(*) OVER () AS Total
        FROM v2.ENCAISSEMENT AS e
        LEFT JOIN v2.VENTILATION AS v ON v.VENTI_ID_ENCMT = e.ENCMT_ID_ENCMT
        LEFT JOIN rec.Client AS c ON c.Id = v.VENTI_ID_CLIEN
        LEFT JOIN v2.MODE_PAIEMENT AS mp ON mp.R_MODEPAIEME_VALEUR = e.ENCMT_MODE_PAIEMENT
        WHERE 1 = 1 /*filtres*/
        ORDER BY e.ENCMT_DATE_ENCAISSEMENT DESC, e.ENCMT_MONTANT, e.ENCMT_NUMERO_TITRE, e.ENCMT_ID_ENCMT, v.VENTI_ID_VENTI
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        // Les dates pre-remplies de la V2 : aujourd'hui, de 0 h a 24 h (en tete du fichier).
        var f = new FiltresRecherche();
        f.Ajouter("e.ENCMT_DATE_ENCAISSEMENT >= @debut AND e.ENCMT_DATE_ENCAISSEMENT < @finExclue",
            ("@debut", aujourdhui.Date), ("@finExclue", aujourdhui.Date.AddDays(1)));
        return Lire(baseGreffe, f, plafond, ct);
    }

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            f.Ajouter("e.ENCMT_NUMERO_TITRE LIKE @texte OR e.ENCMT_DESIGNATION_PAYEUR LIKE @texte OR e.ENCMT_DESIGNATION_BANQUE LIKE @texte " +
                      "OR e.ENCMT_COMMENTAIRE LIKE @texte OR c.Nom LIKE @texte OR c.Abrege LIKE @texte",
                ("@texte", FormeLigne.Contient(recherche.Texte)));
        // Les dates : 0 h pour le debut, 24 h pour la fin (proc_rechenc.md:112-118 ; la V2 inverse la borne de la fin seule, un
        // defaut qu'elle corrige elle-meme a la ligne suivante).
        if (CriteresConsultation.Date(recherche.Critere(CritereDebut)) is { } debut)
            f.Ajouter("e.ENCMT_DATE_ENCAISSEMENT >= @debut", ("@debut", debut));
        if (CriteresConsultation.Date(recherche.Critere(CritereFin)) is { } fin)
            f.Ajouter("e.ENCMT_DATE_ENCAISSEMENT < @finExclue", ("@finExclue", fin.AddDays(1)));
        if (recherche.Critere(CritereTitre) is { } titre)
            f.Ajouter("e.ENCMT_NUMERO_TITRE LIKE @titre", ("@titre", FormeLigne.Contient(titre)));
        if (recherche.Critere(CritereMode) is { } mode)
            f.Ajouter("e.ENCMT_MODE_PAIEMENT = @mode OR mp.R_MODEPAIEME_LIBELLE_ECRAN = @mode", ("@mode", mode));
        if (recherche.Critere(CritereBanque) is { } banque)
            f.Ajouter("e.ENCMT_DESIGNATION_BANQUE LIKE @banque", ("@banque", FormeLigne.Contient(banque)));
        if (recherche.Critere(CriterePayeur) is { } payeur)
            f.Ajouter("e.ENCMT_DESIGNATION_PAYEUR LIKE @payeur", ("@payeur", FormeLigne.Contient(payeur)));
        if (recherche.Critere(CritereRemise) is { } remise)
            Ou(f, CriteresConsultation.Entier(remise), "e.ENCMT_REMIS = @remise", "@remise");
        if (recherche.Critere(CritereCommentaire) is { } commentaire)
            f.Ajouter("e.ENCMT_COMMENTAIRE LIKE @commentaire", ("@commentaire", FormeLigne.Contient(commentaire)));
        if (recherche.Critere(CritereAbrege) is { } abrege)
            f.Ajouter("c.Abrege LIKE @abrege", ("@abrege", FormeLigne.Contient(abrege)));
        if (recherche.Critere(CritereClient) is { } client)
            f.Ajouter("c.Nom LIKE @client", ("@client", FormeLigne.Contient(client)));
        if (recherche.Critere(CritereMin) is { } min) Ou(f, CriteresConsultation.Montant(min), "e.ENCMT_MONTANT >= @min", "@min");
        if (recherche.Critere(CritereMax) is { } max) Ou(f, CriteresConsultation.Montant(max), "e.ENCMT_MONTANT <= @max", "@max");
        if (recherche.Critere(CritereAffaire) is { } affaire) Ou(f, CriteresConsultation.Entier(affaire), "v.VENTI_ID_AFFRE = @affaire", "@affaire");
        return Lire(baseGreffe, f, plafond, ct);
    }

    /// <summary>Le filtre d'une valeur lue ; une saisie qui ne se lit pas (« abc » pour un montant) ne retrouve rien.</summary>
    // fix-ok: une saisie illisible etait remplacee par decimal.MaxValue, hors du type money de la base ; elle donne maintenant
    // « 1 = 0 » -- bloc « Montant maximum = abc » de PROC_RECHENC.recherches, vert au banc EcransGeneresBase le 26/11/2026.
    private static void Ou(FiltresRecherche f, object? valeur, string clause, string nom)
    {
        if (valeur is null) f.Ajouter("1 = 0");
        else f.Ajouter(clause, (nom, valeur));
    }

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, FiltresRecherche f, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtres*/", f.EnSuite, StringComparison.Ordinal), Ligne, ct,
            f.Parametres(new SqlParameter("@plafond", plafond)));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var id = FormeLigne.Texte(r, "ENCMT_ID_ENCMT");
        var titre = FormeLigne.Texte(r, "ENCMT_NUMERO_TITRE");
        var montantVentile = CriteresConsultation.Decimal(r, "VENTI_MONTANT");
        var ventilee = montantVentile is { } mv && mv != 0;
        var client = FormeLigne.Joindre(FormeLigne.Texte(r, "ClientAbrege"), FormeLigne.Texte(r, "ClientNom"));
        var payeur = FormeLigne.Texte(r, "ENCMT_DESIGNATION_PAYEUR");
        var remise = CriteresConsultation.EntierDe(r, "ENCMT_REMIS");
        var affaire = FormeLigne.Texte(r, "VENTI_ID_AFFRE");
        var nom = FormeLigne.Joindre(titre.Length > 0 ? $"Encaissement {titre}" : $"Encaissement n° {id}", payeur.Length > 0 ? payeur : client);
        var precisions = FormeLigne.Joindre(
            FormeLigne.Texte(r, "Mode"),
            CriteresConsultation.Euros(CriteresConsultation.Decimal(r, "ENCMT_MONTANT")),
            $"le {FormeLigne.Date(FormeLigne.DateDe(r, "ENCMT_DATE_ENCAISSEMENT"))}",
            FormeLigne.Texte(r, "ENCMT_DESIGNATION_BANQUE"),
            // -1 : « pas de remise » (OPE_RESULTAT.cs:139).
            remise is { } n && n != -1 ? $"remise {n}" : null,
            ventilee ? $"ventilé {CriteresConsultation.Euros(montantVentile)}" : "sans ventilation",
            ventilee && client.Length > 0 && payeur.Length > 0 ? $"client {client}" : null,
            affaire.Length > 0 ? $"affaire {affaire}" : null);
        // La regle d'etat (en tete du fichier).
        return new LigneLue(nom, precisions, ventilee ? Ventile : SansVentilation, ventilee ? "Validee" : "AFaire", []) { Id = id };
    }

    // ------------------------------------------------------------------ LA FICHE

    internal const string ChampTitre = "Numéro de titre", ChampDate = "Date d'encaissement", ChampMontant = "Montant",
                          ChampMode = "Mode de paiement", ChampBanque = "Banque", ChampPayeur = "Payeur", ChampRemise = "Numéro de remise",
                          ChampCommentaire = "Commentaire", ChampAgent = "Saisi par", ChampCompta = "Date de comptabilisation",
                          ChampVentilations = "Ventilations";

    private const string Fiche = """
        SELECT e.ENCMT_ID_ENCMT, e.ENCMT_NUMERO_TITRE, e.ENCMT_DATE_ENCAISSEMENT, e.ENCMT_MONTANT, e.ENCMT_DESIGNATION_PAYEUR,
               e.ENCMT_DESIGNATION_BANQUE, e.ENCMT_REMIS, e.ENCMT_COMMENTAIRE, e.ENCMT_DATE_COMPTABILISATION,
               COALESCE(mp.R_MODEPAIEME_LIBELLE_ECRAN, e.ENCMT_MODE_PAIEMENT) AS Mode,
               LTRIM(RTRIM(CONCAT(u.Prenom, ' ', u.Nom))) AS Agent
        FROM v2.ENCAISSEMENT AS e
        LEFT JOIN v2.MODE_PAIEMENT AS mp ON mp.R_MODEPAIEME_VALEUR = e.ENCMT_MODE_PAIEMENT
        LEFT JOIN soc.Compte AS u ON u.IdV2 = e.ENCMT_ID_UTLST
        WHERE e.ENCMT_ID_ENCMT = @id
        """;

    private const string Ventilations = """
        SELECT v.VENTI_ID_VENTI, v.VENTI_MONTANT, v.VENTI_DATE_VENTILATION, v.VENTI_ID_AFFRE, c.Abrege, c.Nom,
               p.PYEUR_LIBELLE, p.PYEUR_IS_PRINCIPAL
        FROM v2.VENTILATION AS v
        LEFT JOIN rec.Client AS c ON c.Id = v.VENTI_ID_CLIEN
        LEFT JOIN v2.PAYEUR AS p ON p.PYEUR_ID_PYEUR = v.VENTI_ID_PYEUR
        WHERE v.VENTI_ID_ENCMT = @id
        ORDER BY v.VENTI_ID_VENTI
        """;

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (CriteresConsultation.Entier(id) is not { } n) return null;
        string nom;
        ChampFiche[] encaissement;
        await using (var r = await LectureV2.LireAsync(baseGreffe, Fiche, ct, new SqlParameter("@id", n)))
        {
            if (!await r.ReadAsync(ct)) return null;
            var titre = FormeLigne.Texte(r, "ENCMT_NUMERO_TITRE");
            nom = titre.Length > 0 ? $"Encaissement {titre}" : $"Encaissement n° {n}";
            var remise = CriteresConsultation.EntierDe(r, "ENCMT_REMIS");
            var compta = FormeLigne.DateDe(r, "ENCMT_DATE_COMPTABILISATION");
            encaissement =
            [
                new(ChampTitre, titre),
                new(ChampDate, FormeLigne.DateFiche(FormeLigne.DateDe(r, "ENCMT_DATE_ENCAISSEMENT"))),
                new(ChampMontant, CriteresConsultation.MontantFiche(CriteresConsultation.Decimal(r, "ENCMT_MONTANT"))),
                new(ChampMode, FormeLigne.Texte(r, "Mode")),
                new(ChampBanque, FormeLigne.Texte(r, "ENCMT_DESIGNATION_BANQUE")),
                new(ChampPayeur, FormeLigne.Texte(r, "ENCMT_DESIGNATION_PAYEUR")),
                new(ChampRemise, remise is { } x && x != -1 ? x.ToString(System.Globalization.CultureInfo.InvariantCulture) : ""),
                new(ChampCommentaire, FormeLigne.Texte(r, "ENCMT_COMMENTAIRE")),
                new(ChampAgent, FormeLigne.Texte(r, "Agent")),
                // 1800 : « non comptabilise » (OPE_RESULTAT.cs:153).
                new(ChampCompta, compta is { Year: not 1800 } c ? FormeLigne.DateFiche(c) : ""),
            ];
        }
        var ventilations = await CriteresConsultation.SousListeAsync(baseGreffe, Ventilations, r =>
        [
            FormeLigne.Joindre(FormeLigne.Texte(r, "Abrege"), FormeLigne.Texte(r, "Nom")),
            // Un payeur non principal est un tiers payeur (OPE_CRITERES.cs:153-160).
            r["PYEUR_IS_PRINCIPAL"] is false ? FormeLigne.Texte(r, "PYEUR_LIBELLE") : "",
            FormeLigne.Texte(r, "VENTI_ID_AFFRE"),
            CriteresConsultation.Euros(CriteresConsultation.Decimal(r, "VENTI_MONTANT")),
            FormeLigne.Date(FormeLigne.DateDe(r, "VENTI_DATE_VENTILATION")),
        ], ct, new SqlParameter("@id", n));
        return new FicheEcran(Procedure, id, nom,
        [
            new SectionFiche(SectionEncaissement, encaissement),
            new SectionFiche(SectionVentilations,
                [CriteresConsultation.Tableau(ChampVentilations, ["Client", "Tiers payeur", "Affaire", "Montant ventilé", "Date"], ventilations)]),
        ]);
    }
}
