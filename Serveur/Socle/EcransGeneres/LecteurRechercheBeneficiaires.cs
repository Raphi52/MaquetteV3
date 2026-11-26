using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Rechercher un beneficiaire » (PROC_BEN) : les beneficiaires effectifs des dossiers du registre, et leurs divergences en
/// attente de la societe. Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 6, 26/11/2026. Lecture seule :
/// RIG v2 n'ecrit rien ici (proc_ben.md:344-346) ; signaler une divergence ouvre PROC_DBEN, un autre ecran.
///
/// SOURCE (ancien RIG) : OPE_RESULTATS_BEN._BuildRequete (D:\RigApplication\Source\RIG\DLL\Processus\PROC_BEN\Résultats\
/// OPE_RESULTATS_BEN.cs:127-191), deux blocs reunis par UNION, tries par BENEF_ID_DSRCS :
///   - bloc 1, les beneficiaires : BENEFICIAIRE_EFFECTIF JOIN DOSSIER_RCS, BENEF_SUPPRIME = 0, TOP 100 (TopMax par defaut,
///     ETP_RESULTATS_BEN.cs:7-70) ;
///   - bloc 2, leurs divergences « En attente de la societe » (DIVER_STATUS = 0, Divergence.eStatus.Encours) : JOIN ACTION_RCS
///     JOIN DIVERGENCE (DIVER_ID_ACTRC) JOIN DIVERGENCE_DETAIL (DIVDET_ID_DIVER, DIVDET_ID_BENEF) ; les filtres du bloc 1
///     s'y appliquent aussi (le meme « where », proc_ben.md:319).
/// Filtres (l. 140-163) : toujours BENEF_SUPPRIME = 0 ; « Numéro de dossier » = DSSRC_NUM_GESTION OU DSSRC_SIREN ; « Nom
/// d'usage », « Nom patronymique », « Prénoms » en LIKE ; « Date de naissance » = ; « Dossiers radiés » decoche (le defaut) ->
/// DSSRC_DATE_RADIATION IS NULL. La V2 lance la recherche sans critere : la liste a l'ouverture est la meme. Non repris :
/// « Divergences seulement », une case que la V2 ne lit jamais (proc_ben.md:315-317, critere mort).
/// Dans la base V3, DOSSIER_RCS est rec.DossierRcs et ACTION_RCS rec.ActionRegistre (Serveur/Recopie/FichesV3.cs).
/// Ordre : le dossier (BENEF_ID_DSRCS), puis le beneficiaire, sa ligne avant celles de ses divergences.
///
/// GROUPES : « Bénéficiaire en vigueur » (dossier non radie) / « Dossier radié » (la colonne « radié » de la V2, l. 133).
/// REGLE D'ETAT (le libelle du bouton de la ligne, Search(), OPE_RESULTATS_BEN.cs:198-203) :
///   ligne d'une divergence en attente (bloc 2) -> Validee « Divergence en attente » (bouton « Visualiser ») ;
///   ligne du beneficiaire (bloc 1)             -> AFaire « Sans divergence » (bouton « Signaler »).
///
/// FICHE (IFicheEcran), Id = BENEF_ID_BENEF : « Bénéficiaire » (les colonnes de la grille) et « Divergence » (celles en attente
/// sur ce beneficiaire, leur demandeur et leur date).
/// </summary>
internal sealed class LecteurRechercheBeneficiaires : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_BEN";

    internal const string EnVigueur = "Bénéficiaire en vigueur", Radie = "Dossier radié";

    internal const string CritereNumero = "Numéro de dossier", CritereUsage = "Nom d'usage", CriterePatronyme = "Nom patronymique",
                          CriterePrenoms = "Prénoms", CritereNaissance = "Date de naissance", CritereRadies = "Dossiers radiés";

    internal const string SectionBeneficiaire = "Bénéficiaire", SectionDivergence = "Divergence";

    internal const string ChampGestion = "Numéro de gestion", ChampSiren = "SIREN", ChampNom = "Nom", ChampPrenoms = "Prénoms",
                          ChampNaissance = "Date de naissance", ChampEffet = "Date d'effet", ChampDivergences = "Divergences en attente";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereNumero, CritereUsage, CriterePatronyme, CriterePrenoms, CritereNaissance, CritereRadies];

    // TOP 100 par bloc (le TopMax par defaut de la V2). fix-ok: chaque bloc ordonne est une table derivee -- SQL Server refuse un
    // ORDER BY dans une branche d'UNION ; banc EcransGeneresBase vert le 26/11/2026 (PROC_BEN.attendu et .recherches).
    private const string Requete = """
        SELECT TOP (@plafond) l.*, COUNT(*) OVER () AS Total
        FROM (SELECT * FROM (SELECT TOP (100) 0 AS Bloc, b.BENEF_ID_BENEF, b.BENEF_ID_DSRCS, b.BENEF_PATRONYME, b.BENEF_NOM_USAGE, b.BENEF_PRENOM_ETAT_CIVIL,
                     b.BENEF_DATE_NAISSANCE, s.NumeroGestion, s.Siren, s.DateRadiation, CAST(NULL AS datetime) AS DateDivergence,
                     CAST(NULL AS int) AS IdDivergence, CAST(NULL AS varchar(max)) AS Demandeur
              FROM v2.BENEFICIAIRE_EFFECTIF AS b
              JOIN rec.DossierRcs AS s ON s.Id = b.BENEF_ID_DSRCS
              WHERE b.BENEF_SUPPRIME = 0 /*filtres*/
              ORDER BY b.BENEF_ID_DSRCS, b.BENEF_ID_BENEF) AS b1
              UNION ALL
              SELECT * FROM (SELECT DISTINCT TOP (100) 1 AS Bloc, b.BENEF_ID_BENEF, b.BENEF_ID_DSRCS, b.BENEF_PATRONYME, b.BENEF_NOM_USAGE, b.BENEF_PRENOM_ETAT_CIVIL,
                     b.BENEF_DATE_NAISSANCE, s.NumeroGestion, s.Siren, s.DateRadiation, d.DIVER_DATE_CREATION, d.DIVER_ID_DIVER,
                     d.DIVER_DEMANDEUR
              FROM v2.BENEFICIAIRE_EFFECTIF AS b
              JOIN rec.DossierRcs AS s ON s.Id = b.BENEF_ID_DSRCS
              JOIN rec.ActionRegistre AS a ON a.IdDossier = s.Id
              JOIN v2.DIVERGENCE AS d ON d.DIVER_ID_ACTRC = a.Id
              JOIN v2.DIVERGENCE_DETAIL AS dd ON dd.DIVDET_ID_DIVER = d.DIVER_ID_DIVER AND dd.DIVDET_ID_BENEF = b.BENEF_ID_BENEF
              WHERE b.BENEF_SUPPRIME = 0 AND d.DIVER_STATUS = 0 /*filtres*/
              ORDER BY b.BENEF_ID_DSRCS, b.BENEF_ID_BENEF, d.DIVER_ID_DIVER) AS b2) AS l
        ORDER BY l.BENEF_ID_DSRCS, l.BENEF_ID_BENEF, l.Bloc, l.IdDivergence
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, new FiltresRecherche(), radies: false, plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            f.Ajouter("s.NumeroGestion = @texteNumero OR s.Siren = @texteNumero OR b.BENEF_NOM_USAGE LIKE @texte OR b.BENEF_PATRONYME LIKE @texte " +
                      "OR b.BENEF_PRENOM_ETAT_CIVIL LIKE @texte",
                ("@texteNumero", CriteresConsultation.Numero(recherche.Texte)), ("@texte", FormeLigne.Contient(recherche.Texte)));
        if (recherche.Critere(CritereNumero) is { } numero)
            f.Ajouter("s.NumeroGestion = @numero OR s.Siren = @numero", ("@numero", CriteresConsultation.Numero(numero)));
        if (recherche.Critere(CritereUsage) is { } usage)
            f.Ajouter("b.BENEF_NOM_USAGE LIKE @usage", ("@usage", FormeLigne.Contient(usage)));
        if (recherche.Critere(CriterePatronyme) is { } patronyme)
            f.Ajouter("b.BENEF_PATRONYME LIKE @patronyme", ("@patronyme", FormeLigne.Contient(patronyme)));
        if (recherche.Critere(CriterePrenoms) is { } prenoms)
            f.Ajouter("b.BENEF_PRENOM_ETAT_CIVIL LIKE @prenoms", ("@prenoms", FormeLigne.Contient(prenoms)));
        if (recherche.Critere(CritereNaissance) is { } naissance)
        {
            // Une date qui ne se lit pas ne retrouve personne.
            if (CriteresConsultation.Date(naissance) is { } date) f.Ajouter("b.BENEF_DATE_NAISSANCE = @naissance", ("@naissance", date));
            else f.Ajouter("1 = 0");
        }
        return Lire(baseGreffe, f, CriteresConsultation.OuiNon(recherche.Critere(CritereRadies)) == true, plafond, ct);
    }

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, FiltresRecherche f, bool radies, int plafond, CancellationToken ct)
    {
        // « Dossiers radiés » decoche : les dossiers non radies seuls (en tete du fichier).
        var filtres = (radies ? "" : " AND s.DateRadiation IS NULL") + f.EnSuite;
        // Les parametres servent aux deux blocs : SqlClient ne repete pas un nom, les clauses le reutilisent.
        return LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtres*/", filtres, StringComparison.Ordinal), Ligne, ct,
            f.Parametres(new SqlParameter("@plafond", plafond)));
    }

    private static string NomDe(SqlDataReader r)
        => LtrimJoin(FormeLigne.Texte(r, "BENEF_NOM_USAGE") is { Length: > 0 } usage ? usage : FormeLigne.Texte(r, "BENEF_PATRONYME"),
                     FormeLigne.Texte(r, "BENEF_PRENOM_ETAT_CIVIL"));

    private static string LtrimJoin(string a, string b) => $"{a} {b}".Trim();

    private static LigneLue Ligne(SqlDataReader r)
    {
        var divergence = Convert.ToInt32(r["Bloc"], System.Globalization.CultureInfo.InvariantCulture) == 1;
        var radie = FormeLigne.DateDe(r, "DateRadiation") is not null;
        var naissance = FormeLigne.DateDe(r, "BENEF_DATE_NAISSANCE");
        var precisions = FormeLigne.Joindre(
            $"dossier {FormeLigne.NumeroGestion(FormeLigne.Texte(r, "NumeroGestion"))}",
            naissance is { } n ? $"né(e) le {FormeLigne.Date(n)}" : null,
            radie ? "dossier radié" : null,
            divergence ? $"divergence du {FormeLigne.Date(FormeLigne.DateDe(r, "DateDivergence"))} en attente de la société" : null,
            divergence ? FormeLigne.Texte(r, "Demandeur") : null);
        // La regle d'etat (en tete du fichier).
        return new LigneLue(NomDe(r), precisions, radie ? Radie : EnVigueur, divergence ? "Validee" : "AFaire", [])
        {
            Id = FormeLigne.Texte(r, "BENEF_ID_BENEF"),
        };
    }

    // ------------------------------------------------------------------ LA FICHE

    private const string Fiche = """
        SELECT b.BENEF_ID_BENEF, b.BENEF_PATRONYME, b.BENEF_NOM_USAGE, b.BENEF_PRENOM_ETAT_CIVIL, b.BENEF_DATE_NAISSANCE, b.BENEF_DATE_EFFET,
               s.NumeroGestion, s.Siren
        FROM v2.BENEFICIAIRE_EFFECTIF AS b
        JOIN rec.DossierRcs AS s ON s.Id = b.BENEF_ID_DSRCS
        WHERE b.BENEF_ID_BENEF = @id
        """;

    private const string Divergences = """
        SELECT d.DIVER_ID_DIVER, d.DIVER_DATE_CREATION, d.DIVER_DEMANDEUR
        FROM v2.DIVERGENCE AS d
        JOIN v2.DIVERGENCE_DETAIL AS dd ON dd.DIVDET_ID_DIVER = d.DIVER_ID_DIVER
        WHERE dd.DIVDET_ID_BENEF = @id AND d.DIVER_STATUS = 0
        ORDER BY d.DIVER_DATE_CREATION DESC, d.DIVER_ID_DIVER DESC
        """;

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (CriteresConsultation.Entier(id) is not { } n) return null;
        string nom;
        ChampFiche[] beneficiaire;
        await using (var r = await LectureV2.LireAsync(baseGreffe, Fiche, ct, new SqlParameter("@id", n)))
        {
            if (!await r.ReadAsync(ct)) return null;
            nom = NomDe(r);
            beneficiaire =
            [
                new(ChampGestion, FormeLigne.NumeroGestion(FormeLigne.Texte(r, "NumeroGestion"))),
                new(ChampSiren, FormeLigne.Siren(FormeLigne.Texte(r, "Siren"))),
                new(ChampNom, FormeLigne.Texte(r, "BENEF_NOM_USAGE") is { Length: > 0 } usage ? usage : FormeLigne.Texte(r, "BENEF_PATRONYME")),
                new(ChampPrenoms, FormeLigne.Texte(r, "BENEF_PRENOM_ETAT_CIVIL")),
                new(ChampNaissance, FormeLigne.DateFiche(FormeLigne.DateDe(r, "BENEF_DATE_NAISSANCE"))),
                new(ChampEffet, FormeLigne.DateFiche(FormeLigne.DateDe(r, "BENEF_DATE_EFFET"))),
            ];
        }
        var divergences = await CriteresConsultation.SousListeAsync(baseGreffe, Divergences,
            r => [FormeLigne.Texte(r, "DIVER_ID_DIVER"), FormeLigne.Date(FormeLigne.DateDe(r, "DIVER_DATE_CREATION")), FormeLigne.Texte(r, "DIVER_DEMANDEUR")],
            ct, new SqlParameter("@id", n));
        return new FicheEcran(Procedure, id, nom.Length > 0 ? nom : $"Bénéficiaire n° {n}",
        [
            new SectionFiche(SectionBeneficiaire, beneficiaire),
            new SectionFiche(SectionDivergence,
                [CriteresConsultation.Tableau(ChampDivergences, ["N° de divergence", "Créée le", "Demandeur"], divergences)]),
        ]);
    }
}
