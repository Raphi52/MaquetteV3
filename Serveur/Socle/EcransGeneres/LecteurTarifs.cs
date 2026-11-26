using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Tarifs » (PROC_TARIFS) : les articles tarifaires du greffe, leur prix et leur composition. Mode d'emploi : LecteurEcran.cs.
/// Plan « RIG v3 100 % fonctionnel », lot 6, 26/11/2026. Lecture seule : justifier une incoherence, deriver un article et
/// reaffecter une categorie ecrivent (OPE_RESULTATS, OPE_ACTIONS_CATEGORIES) -- non repris, nommes.
///
/// SOURCE (ancien RIG) : PROC_TARIFS, ArticleSearchQueries.GetSelectCte et BuildWhereClause (D:\RigApplication\Source\RIG\DLL\
/// Processus\PROC_TARIFS\DataAccess\ArticleSearchQueries.cs:21-135) :
///   - la DERNIERE version de chaque article : ROW_NUMBER() OVER (PARTITION BY ARTCL_CODE_ARTICLE_GREFFE ORDER BY
///     ARTCL_DATE_APPLICATION DESC) = 1 (ARTICLE_NEWEST, :23-26) ;
///   - son prix : ARTCL_PRIX_UHT, sauf un titre (TI) dont le prix est la somme quantite x prix de ses articles principaux (PR),
///     par ARTICLE_COMPOSITION (ARTICLE_PRIX_DETAIL / ARTICLE_PRIX_TITRE, :48-66) ;
///   - sa categorie (ARTICLE_CATEGORIE, « Aucune » sans categorie) et ses codes evenement (CEP_ARTICLE, :32-34, :75).
/// Les criteres par defaut de la V2 (OPE_CRITERES.DoInitZoneSaisie, proc_tarifs.md:159) : statut « Actif » (A), type « Titre »
/// (TI), toutes categories -- la liste a l'ouverture de la V3. Non repris (nommes) : la periode de tarification (ARTICLE_PERIODICITY,
/// la periodicite tiree de ARTICLE_CORRESP, :35-47), le calcul « Cohérent ? » (Article.IsCoherent, RigMetier\Facture\Article.cs),
/// et le seuil d'alerte de chargement par utilisateur (TARIFS.AlertBigLoadThreshold, 100).
///
/// RECHERCHE (IRechercheEcran), les filtres de BuildWhereClause (:91-134) : « Statut » (A, S, ou « tous »), « Type d'article »
/// (TI, PR... ou « tous »), « Catégorie d'article » (son libelle, ou « Aucune »), « Code événement » (LIKE, « * » : tout article qui
/// en a un, :116-118). Le champ de recherche cherche le code OU le libelle (:105-110).
///
/// GROUPES : « Article de base » / « Article dérivé » (un article qui est la correspondance d'un autre :
/// ARTICLE_CORRESP.ARTCO_CODE_ARTICLE_CORRESP).
/// REGLE D'ETAT :
///   sa famille a une incoherence justifiee (ARTICLE_CORRESP_INCOHERENCE_JUSTIF.ARCIN_REGROUPEMENT = ARTCO_REGROUPEMENT : « une
///   justification vaut pour toute la famille ») -> Validee « Incohérence justifiée » ;
///   statut « En sommeil » (S)                                                     -> EnCours « En sommeil » ;
///   autre                                                                         -> AFaire « Actif ».
///
/// FICHE (IFicheEcran), Id = ARTCL_ID_ARTCL : « Article », « Composition » (les articles principaux d'un titre) et « Cohérence »
/// (la justification de la famille).
/// </summary>
internal sealed class LecteurTarifs : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_TARIFS";

    internal const string ArticleDeBase = "Article de base", ArticleDerive = "Article dérivé";

    internal const string CritereStatut = "Statut", CritereType = "Type d'article", CritereCategorie = "Catégorie d'article",
                          CritereEvenement = "Code événement";

    internal const string SectionArticle = "Article", SectionComposition = "Composition", SectionCoherence = "Cohérence";

    internal const string ChampCode = "Code", ChampLibelle = "Libellé", ChampMontant = "Montant HT", ChampType = "Type",
                          ChampStatut = "Statut", ChampCategorie = "Catégorie", ChampApplication = "Date d'application",
                          ChampEvenements = "Codes événement", ChampComposants = "Articles composants",
                          ChampFamille = "Famille", ChampJustification = "Date de justification";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereStatut, CritereType, CritereCategorie, CritereEvenement];

    // La derniere version de chaque article (ARTICLE_NEWEST).
    private const string Recents = """
        (SELECT v.* FROM (SELECT a.*, ROW_NUMBER() OVER (PARTITION BY a.ARTCL_CODE_ARTICLE_GREFFE ORDER BY a.ARTCL_DATE_APPLICATION DESC) AS RangVersion
                          FROM v2.ARTICLE AS a) AS v WHERE v.RangVersion = 1)
        """;

    // Le prix d'un titre : quantite x prix de ses articles principaux les plus recents (ARTICLE_PRIX_TITRE).
    private const string Prix = $"""
        CASE WHEN n.ARTCL_TYPE_ARTICLE <> 'TI' THEN n.ARTCL_PRIX_UHT
             ELSE ISNULL((SELECT SUM(c.X_ARTCP_QUANTITE * p.ARTCL_PRIX_UHT) FROM v2.ARTICLE_COMPOSITION AS c
                          JOIN {Recents} AS p ON p.ARTCL_ID_ARTCL = c.X_ARTCP_ID_ARTCL_FILS AND p.ARTCL_TYPE_ARTICLE = 'PR'
                          WHERE c.X_ARTCP_ID_ARTCL_PERE = n.ARTCL_ID_ARTCL), 0) END
        """;

    // Les codes evenement de l'article (CODE_EVE), separes par des virgules.
    private const string Evenements = """
        STUFF((SELECT ', ' + e.CEPAR_CODE_EVE_PRO FROM v2.CEP_ARTICLE AS e WHERE e.CEPAR_CODE_ARTICLE = n.ARTCL_CODE_ARTICLE_RIG
               ORDER BY e.CEPAR_NUM_ORDRE FOR XML PATH('')), 1, 2, '')
        """;

    private const string Requete = $"""
        SELECT TOP (@plafond) n.ARTCL_ID_ARTCL, n.ARTCL_CODE_ARTICLE_RIG, n.ARTCL_LIBELLE, n.ARTCL_TYPE_ARTICLE, n.ARTCL_STATUT,
               {Prix} AS PrixHt, ISNULL(cat.ARTCA_LIBELLE, 'Aucune') AS Categorie, {Evenements} AS Codes,
               CASE WHEN EXISTS (SELECT 1 FROM v2.ARTICLE_CORRESP AS co WHERE co.ARTCO_CODE_ARTICLE_CORRESP = n.ARTCL_CODE_ARTICLE_RIG)
                    THEN 1 ELSE 0 END AS Derive,
               CASE WHEN EXISTS (SELECT 1 FROM v2.ARTICLE_CORRESP AS co JOIN v2.ARTICLE_CORRESP_INCOHERENCE_JUSTIF AS j
                                      ON j.ARCIN_REGROUPEMENT = co.ARTCO_REGROUPEMENT
                                 WHERE n.ARTCL_CODE_ARTICLE_RIG IN (co.ARTCO_CODE_ARTICLE_BASE, co.ARTCO_CODE_ARTICLE_CORRESP))
                    THEN 1 ELSE 0 END AS Justifie,
               COUNT(*) OVER () AS Total
        FROM {Recents} AS n
        LEFT JOIN v2.ARTICLE_CATEGORIE AS cat ON cat.ARTCA_ID_ARTCA = n.ARTCL_ID_ARTCA
        WHERE 1 = 1 /*filtres*/
        ORDER BY n.ARTCL_CODE_ARTICLE_RIG, n.ARTCL_ID_ARTCL
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        // Les criteres par defaut de la V2 : actifs, titres (en tete du fichier).
        var f = new FiltresRecherche();
        f.Ajouter("n.ARTCL_STATUT = 'A'");
        f.Ajouter("n.ARTCL_TYPE_ARTICLE = 'TI'");
        return Lire(baseGreffe, f, plafond, ct);
    }

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            f.Ajouter("n.ARTCL_CODE_ARTICLE_RIG LIKE @texte OR n.ARTCL_LIBELLE LIKE @texte", ("@texte", FormeLigne.Contient(recherche.Texte)));
        // Statut et type : leur defaut V2 s'applique tant qu'ils ne sont pas saisis ; « tous » leve le filtre.
        var statut = recherche.Critere(CritereStatut) ?? "A";
        if (!Tous(statut))
            f.Ajouter("n.ARTCL_STATUT = @statut", ("@statut", statut.Trim().ToUpperInvariant() switch
            {
                "ACTIF" => "A", "EN SOMMEIL" or "SOMMEIL" => "S", var s => s,
            }));
        var type = recherche.Critere(CritereType) ?? "TI";
        if (!Tous(type)) f.Ajouter("n.ARTCL_TYPE_ARTICLE = @type", ("@type", type.Trim().ToUpperInvariant()));
        if (recherche.Critere(CritereCategorie) is { } categorie)
            f.Ajouter("ISNULL(cat.ARTCA_LIBELLE, 'Aucune') LIKE @categorie", ("@categorie", FormeLigne.Contient(categorie)));
        if (recherche.Critere(CritereEvenement) is { } evenement)
            f.Ajouter(evenement.Trim() == "*"
                    ? "EXISTS (SELECT 1 FROM v2.CEP_ARTICLE AS e WHERE e.CEPAR_CODE_ARTICLE = n.ARTCL_CODE_ARTICLE_RIG)"
                    : "EXISTS (SELECT 1 FROM v2.CEP_ARTICLE AS e WHERE e.CEPAR_CODE_ARTICLE = n.ARTCL_CODE_ARTICLE_RIG AND e.CEPAR_CODE_EVE_PRO LIKE @evenement)",
                ("@evenement", FormeLigne.Contient(evenement)));
        return Lire(baseGreffe, f, plafond, ct);
    }

    private static bool Tous(string valeur) => valeur.Trim().Equals("tous", StringComparison.OrdinalIgnoreCase);

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, FiltresRecherche f, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtres*/", f.EnSuite, StringComparison.Ordinal), Ligne, ct,
            f.Parametres(new SqlParameter("@plafond", plafond)));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var statut = FormeLigne.Texte(r, "ARTCL_STATUT");
        var justifie = System.Convert.ToInt32(r["Justifie"], System.Globalization.CultureInfo.InvariantCulture) == 1;
        var derive = System.Convert.ToInt32(r["Derive"], System.Globalization.CultureInfo.InvariantCulture) == 1;
        var codes = FormeLigne.Texte(r, "Codes");
        var nom = FormeLigne.Joindre($"Article {FormeLigne.Texte(r, "ARTCL_CODE_ARTICLE_RIG")}", FormeLigne.Texte(r, "ARTCL_LIBELLE"));
        var precisions = FormeLigne.Joindre(
            $"{CriteresConsultation.Euros(CriteresConsultation.Decimal(r, "PrixHt"))} HT",
            $"type {FormeLigne.Texte(r, "ARTCL_TYPE_ARTICLE")}",
            statut == "S" ? "en sommeil" : null,
            $"catégorie {FormeLigne.Texte(r, "Categorie")}",
            codes.Length > 0 ? $"codes événement {codes}" : null,
            justifie ? "incohérence justifiée" : null);
        // La regle d'etat (en tete du fichier).
        var etat = justifie ? "Validee" : statut == "S" ? "EnCours" : "AFaire";
        return new LigneLue(nom, precisions, derive ? ArticleDerive : ArticleDeBase, etat, []) { Id = FormeLigne.Texte(r, "ARTCL_ID_ARTCL") };
    }

    // ------------------------------------------------------------------ LA FICHE

    private const string Fiche = $"""
        SELECT n.ARTCL_ID_ARTCL, n.ARTCL_CODE_ARTICLE_RIG, n.ARTCL_LIBELLE, n.ARTCL_TYPE_ARTICLE, n.ARTCL_STATUT, n.ARTCL_DATE_APPLICATION,
               {Prix} AS PrixHt, ISNULL(cat.ARTCA_LIBELLE, 'Aucune') AS Categorie, {Evenements} AS Codes,
               (SELECT TOP (1) co.ARTCO_REGROUPEMENT FROM v2.ARTICLE_CORRESP AS co
                WHERE n.ARTCL_CODE_ARTICLE_RIG IN (co.ARTCO_CODE_ARTICLE_BASE, co.ARTCO_CODE_ARTICLE_CORRESP) ORDER BY co.ARTCO_ID_ARTCO) AS Famille,
               (SELECT MAX(j.ARCIN_DATE_MODIFICATION) FROM v2.ARTICLE_CORRESP AS co JOIN v2.ARTICLE_CORRESP_INCOHERENCE_JUSTIF AS j
                    ON j.ARCIN_REGROUPEMENT = co.ARTCO_REGROUPEMENT
                WHERE n.ARTCL_CODE_ARTICLE_RIG IN (co.ARTCO_CODE_ARTICLE_BASE, co.ARTCO_CODE_ARTICLE_CORRESP)) AS Justification
        FROM v2.ARTICLE AS n
        LEFT JOIN v2.ARTICLE_CATEGORIE AS cat ON cat.ARTCA_ID_ARTCA = n.ARTCL_ID_ARTCA
        WHERE n.ARTCL_ID_ARTCL = @id
        """;

    private const string Composants = """
        SELECT p.ARTCL_CODE_ARTICLE_RIG, p.ARTCL_LIBELLE, c.X_ARTCP_QUANTITE, p.ARTCL_PRIX_UHT
        FROM v2.ARTICLE_COMPOSITION AS c
        JOIN v2.ARTICLE AS p ON p.ARTCL_ID_ARTCL = c.X_ARTCP_ID_ARTCL_FILS
        WHERE c.X_ARTCP_ID_ARTCL_PERE = @id
        ORDER BY c.X_ARTCP_CLASSEMENT, p.ARTCL_CODE_ARTICLE_RIG
        """;

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (CriteresConsultation.Entier(id) is not { } n) return null;
        string nom;
        ChampFiche[] article, coherence;
        await using (var r = await LectureV2.LireAsync(baseGreffe, Fiche, ct, new SqlParameter("@id", n)))
        {
            if (!await r.ReadAsync(ct)) return null;
            nom = FormeLigne.Joindre($"Article {FormeLigne.Texte(r, "ARTCL_CODE_ARTICLE_RIG")}", FormeLigne.Texte(r, "ARTCL_LIBELLE"));
            article =
            [
                new(ChampCode, FormeLigne.Texte(r, "ARTCL_CODE_ARTICLE_RIG")), new(ChampLibelle, FormeLigne.Texte(r, "ARTCL_LIBELLE")),
                new(ChampMontant, CriteresConsultation.MontantFiche(CriteresConsultation.Decimal(r, "PrixHt"))),
                new(ChampType, FormeLigne.Texte(r, "ARTCL_TYPE_ARTICLE")),
                new(ChampStatut, FormeLigne.Texte(r, "ARTCL_STATUT") == "S" ? "En sommeil" : "Actif"),
                new(ChampCategorie, FormeLigne.Texte(r, "Categorie")),
                new(ChampApplication, FormeLigne.DateFiche(FormeLigne.DateDe(r, "ARTCL_DATE_APPLICATION"))),
                new(ChampEvenements, FormeLigne.Texte(r, "Codes")),
            ];
            coherence =
            [
                new(ChampFamille, FormeLigne.Texte(r, "Famille")),
                new(ChampJustification, FormeLigne.DateFiche(FormeLigne.DateDe(r, "Justification"))),
            ];
        }
        var composants = await CriteresConsultation.SousListeAsync(baseGreffe, Composants,
            r => [FormeLigne.Texte(r, "ARTCL_CODE_ARTICLE_RIG"), FormeLigne.Texte(r, "ARTCL_LIBELLE"), FormeLigne.Texte(r, "X_ARTCP_QUANTITE"),
                  CriteresConsultation.Euros(CriteresConsultation.Decimal(r, "ARTCL_PRIX_UHT"))],
            ct, new SqlParameter("@id", n));
        return new FicheEcran(Procedure, id, nom,
        [
            new SectionFiche(SectionArticle, article),
            new SectionFiche(SectionComposition, [CriteresConsultation.Tableau(ChampComposants, ["Code", "Libellé", "Quantité", "Prix HT"], composants)]),
            new SectionFiche(SectionCoherence, coherence),
        ]);
    }
}
