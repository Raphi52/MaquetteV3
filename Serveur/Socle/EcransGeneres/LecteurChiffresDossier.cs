using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Chiffres d'un dossier » (PROC_VCHIFFRES) : les chiffres seuils de chaque exercice d'un dossier RCS, tires des bilans
/// deposes. Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 6, 26/11/2026. Lecture seule (l'export PDF
/// est une edition, non reprise).
///
/// SOURCE (ancien RIG) : PROC_VCHIFFRES (le Detail OPE de proc_vchiffres.md fait foi), ouvert sur UN dossier (parametre
/// idDossier). La grille (ChiffresSeuils.GetChiffresSeuils(dossier, out exercicesSans...), RigMetier\Depot\ChiffresSeuils.cs:150-163 ;
/// Grid.cs:135-186) : les exercices du dossier (ExercicesAll : EXERCICE.EXRCC_ID_DSRCS) qui ont leurs chiffres seuils
/// (EXRCC_ID_CHPSL -> CHIFFRES_SEUILS), puis, le reglage SHOW_EXERCICES_SANS_CHIFFRES_CLES etant vrai par defaut (Grid.cs:161-165),
/// les exercices SANS chiffres, en lignes « - » ; tri par date de cloture decroissante (Grid.cs:183). Le reglage du greffe n'est
/// pas lu : sa valeur par defaut (vrai) s'applique. Un bilan rejete (ACTE_DEPOSE.ACTDP_SAISIE_BILAN_CodeRejet) est signale sur sa
/// ligne (proc_vchiffres.md:206). DOSSIER_RCS est la fiche rec.DossierRcs (Serveur/Recopie/FichesV3.cs).
///
/// LA LISTE A L'OUVERTURE : sans dossier, la V2 s'ouvre en recherche manuelle (aucune ligne). L'ecran V3 montre la grille de
/// chaque dossier qui a au moins un exercice chiffre -- une liste de depart de la V3 --, et la recherche par dossier (numero de
/// gestion ou SIREN, ou denomination) rend la grille de CE dossier, exercices sans chiffres compris.
///
/// GROUPES : « Bilans déposés » (l'exercice a ses chiffres seuils) / « Exercices sans chiffres ».
/// REGLE D'ETAT :
///   exercice sans chiffres seuils                   -> AFaire « Sans chiffres » (la ligne « - » de la V2) ;
///   chiffres seuils, un bilan de l'exercice rejete  -> EnCours « Bilan rejeté » (ErrorText de la V2) ;
///   chiffres seuils                                 -> Validee « Chiffres connus ».
///
/// FICHE (IFicheEcran), Id = EXRCC_ID_EXRCC : « Dossier », « Chiffres seuils » (les colonnes CHPSL_* de la grille) et « Actes
/// de l'exercice » (ActeDepose.GetActesDeposesBilan : les pieces des depots de l'exercice, DPTRC_ID_EXRCC).
/// </summary>
internal sealed class LecteurChiffresDossier : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_VCHIFFRES";

    internal const string BilansDeposes = "Bilans déposés", SansChiffres = "Exercices sans chiffres";

    internal const string CritereDossier = "Dossier RCS", CritereDenomination = "Dénomination";

    internal const string SectionDossier = "Dossier", SectionChiffres = "Chiffres seuils", SectionActes = "Actes de l'exercice";

    internal const string ChampDossier = "Dossier RCS", ChampDenomination = "Dénomination", ChampCloture = "Date de clôture",
                          ChampDuree = "Durée (mois)", ChampMonnaie = "Monnaie", ChampCa = "Chiffre d'affaires", ChampResultat = "Résultat",
                          ChampActif = "Actif", ChampCapital = "Capital", ChampCapitaux = "Capitaux propres", ChampEffectif = "Effectif",
                          ChampActes = "Actes déposés";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereDossier, CritereDenomination];

    private const string NomEntreprise = "COALESCE(NULLIF(LTRIM(RTRIM(e.Denomination)), ''), LTRIM(RTRIM(CONCAT(e.Patronyme, ' ', e.Prenom))))";

    // Un bilan rejete : une piece d'un depot de l'exercice porte un code de rejet.
    private const string Requete = $"""
        SELECT TOP (@plafond) x.EXRCC_ID_EXRCC, x.EXRCC_DATE_CLOTURE, s.NumeroGestion, {NomEntreprise} AS Nom,
               c.CHPSL_CHIFFRE_AFFAIRE, c.CHPSL_RESULTAT, c.CHPSL_N_SALARIES, CASE WHEN c.CHPSL_ID_CHPSL IS NULL THEN 0 ELSE 1 END AS Chiffres,
               CASE WHEN EXISTS (SELECT 1 FROM v2.DEPOT_RCS AS p JOIN v2.ACTE_DEPOSE AS ad ON ad.ACTDP_ID_DPTRC = p.DPTRC_ID_DPTRC
                                 WHERE p.DPTRC_ID_EXRCC = x.EXRCC_ID_EXRCC AND ISNULL(ad.ACTDP_SAISIE_BILAN_CodeRejet, '') <> '')
                    THEN 1 ELSE 0 END AS Rejete,
               COUNT(*) OVER () AS Total
        FROM v2.EXERCICE AS x
        JOIN rec.DossierRcs AS s ON s.Id = x.EXRCC_ID_DSRCS
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        LEFT JOIN v2.CHIFFRES_SEUILS AS c ON c.CHPSL_ID_CHPSL = x.EXRCC_ID_CHPSL
        WHERE 1 = 1 /*filtres*/
        ORDER BY s.NumeroGestion, x.EXRCC_DATE_CLOTURE DESC, x.EXRCC_ID_EXRCC DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        // Les dossiers qui ont au moins un exercice chiffre (en tete du fichier).
        var f = new FiltresRecherche();
        f.Ajouter("EXISTS (SELECT 1 FROM v2.EXERCICE AS o JOIN v2.CHIFFRES_SEUILS AS oc ON oc.CHPSL_ID_CHPSL = o.EXRCC_ID_CHPSL " +
                  "WHERE o.EXRCC_ID_DSRCS = s.Id)");
        return Lire(baseGreffe, f, plafond, ct);
    }

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            f.Ajouter($"s.NumeroGestion = @texteNumero OR s.Siren = @texteNumero OR {NomEntreprise} LIKE @texte",
                ("@texteNumero", CriteresConsultation.Numero(recherche.Texte)), ("@texte", FormeLigne.Contient(recherche.Texte)));
        if (recherche.Critere(CritereDossier) is { } dossier)
            f.Ajouter("s.NumeroGestion = @dossier OR s.Siren = @dossier", ("@dossier", CriteresConsultation.Numero(dossier)));
        if (recherche.Critere(CritereDenomination) is { } nom)
            f.Ajouter($"{NomEntreprise} LIKE @nom", ("@nom", FormeLigne.Contient(nom)));
        return Lire(baseGreffe, f, plafond, ct);
    }

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, FiltresRecherche f, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtres*/", f.EnSuite, StringComparison.Ordinal), Ligne, ct,
            f.Parametres(new SqlParameter("@plafond", plafond)));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var chiffres = Convert.ToInt32(r["Chiffres"], System.Globalization.CultureInfo.InvariantCulture) == 1;
        var rejete = Convert.ToInt32(r["Rejete"], System.Globalization.CultureInfo.InvariantCulture) == 1;
        var cloture = FormeLigne.DateDe(r, "EXRCC_DATE_CLOTURE");
        var effectif = CriteresConsultation.EntierDe(r, "CHPSL_N_SALARIES");
        var nom = FormeLigne.Joindre(cloture is { } c ? $"Exercice clos le {FormeLigne.Date(c)}" : "Exercice", FormeLigne.Texte(r, "Nom"));
        var precisions = FormeLigne.Joindre(
            $"dossier {FormeLigne.NumeroGestion(FormeLigne.Texte(r, "NumeroGestion"))}",
            !chiffres ? "sans chiffres clés" : null,
            chiffres && CriteresConsultation.Decimal(r, "CHPSL_CHIFFRE_AFFAIRE") is { } ca ? $"CA {CriteresConsultation.Euros(ca)}" : null,
            chiffres && CriteresConsultation.Decimal(r, "CHPSL_RESULTAT") is { } res ? $"résultat {CriteresConsultation.Euros(res)}" : null,
            chiffres && effectif is { } n ? $"{n} salarié{(n > 1 ? "s" : "")}" : null,
            rejete ? "bilan rejeté" : null);
        // La regle d'etat (en tete du fichier).
        var etat = !chiffres ? "AFaire" : rejete ? "EnCours" : "Validee";
        return new LigneLue(nom, precisions, chiffres ? BilansDeposes : SansChiffres, etat, []) { Id = FormeLigne.Texte(r, "EXRCC_ID_EXRCC") };
    }

    // ------------------------------------------------------------------ LA FICHE

    private const string Fiche = $"""
        SELECT x.EXRCC_ID_EXRCC, x.EXRCC_DATE_CLOTURE, s.NumeroGestion, {NomEntreprise} AS Nom, c.CHPSL_DUREE, c.CHPSL_MONNAIE,
               c.CHPSL_CHIFFRE_AFFAIRE, c.CHPSL_RESULTAT, c.CHPSL_ACTIF, c.CHPSL_CAPITAL_SOCIAL, c.CHPSL_TOTAL_CAPITAUX_PROPRES, c.CHPSL_N_SALARIES
        FROM v2.EXERCICE AS x
        JOIN rec.DossierRcs AS s ON s.Id = x.EXRCC_ID_DSRCS
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        LEFT JOIN v2.CHIFFRES_SEUILS AS c ON c.CHPSL_ID_CHPSL = x.EXRCC_ID_CHPSL
        WHERE x.EXRCC_ID_EXRCC = @id
        """;

    private const string Actes = """
        SELECT p.DPTRC_NUM_DEPOT, p.DPTRC_DATE, ad.ACTDP_ABREGE_PIECE, ad.ACTDP_SAISIE_BILAN_CodeRejet, ad.ACTDP_SAISIE_BILAN_MotifRejet
        FROM v2.DEPOT_RCS AS p
        JOIN v2.ACTE_DEPOSE AS ad ON ad.ACTDP_ID_DPTRC = p.DPTRC_ID_DPTRC
        WHERE p.DPTRC_ID_EXRCC = @id
        ORDER BY p.DPTRC_DATE DESC, ad.ACTDP_NUM_ORDRE
        """;

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (CriteresConsultation.Entier(id) is not { } n) return null;
        string nom;
        ChampFiche[] dossier, chiffres;
        await using (var r = await LectureV2.LireAsync(baseGreffe, Fiche, ct, new SqlParameter("@id", n)))
        {
            if (!await r.ReadAsync(ct)) return null;
            var cloture = FormeLigne.DateDe(r, "EXRCC_DATE_CLOTURE");
            nom = FormeLigne.Joindre(cloture is { } c ? $"Exercice clos le {FormeLigne.Date(c)}" : "Exercice", FormeLigne.Texte(r, "Nom"));
            dossier = [new(ChampDossier, FormeLigne.NumeroGestion(FormeLigne.Texte(r, "NumeroGestion"))), new(ChampDenomination, FormeLigne.Texte(r, "Nom"))];
            string Montant(string colonne) => CriteresConsultation.MontantFiche(CriteresConsultation.Decimal(r, colonne));
            chiffres =
            [
                new(ChampCloture, FormeLigne.DateFiche(cloture)), new(ChampDuree, FormeLigne.Texte(r, "CHPSL_DUREE")),
                new(ChampMonnaie, FormeLigne.Texte(r, "CHPSL_MONNAIE")), new(ChampCa, Montant("CHPSL_CHIFFRE_AFFAIRE")),
                new(ChampResultat, Montant("CHPSL_RESULTAT")), new(ChampActif, Montant("CHPSL_ACTIF")),
                new(ChampCapital, Montant("CHPSL_CAPITAL_SOCIAL")), new(ChampCapitaux, Montant("CHPSL_TOTAL_CAPITAUX_PROPRES")),
                new(ChampEffectif, FormeLigne.Texte(r, "CHPSL_N_SALARIES")),
            ];
        }
        var actes = await CriteresConsultation.SousListeAsync(baseGreffe, Actes,
            r => [FormeLigne.Texte(r, "DPTRC_NUM_DEPOT"), FormeLigne.Date(FormeLigne.DateDe(r, "DPTRC_DATE")), FormeLigne.Texte(r, "ACTDP_ABREGE_PIECE"),
                  FormeLigne.Joindre(FormeLigne.Texte(r, "ACTDP_SAISIE_BILAN_CodeRejet"), FormeLigne.Texte(r, "ACTDP_SAISIE_BILAN_MotifRejet"))],
            ct, new SqlParameter("@id", n));
        return new FicheEcran(Procedure, id, nom,
        [
            new SectionFiche(SectionDossier, dossier),
            new SectionFiche(SectionChiffres, chiffres),
            new SectionFiche(SectionActes, [CriteresConsultation.Tableau(ChampActes, ["N° de dépôt", "Déposé le", "Pièce", "Rejet"], actes)]),
        ]);
    }
}
