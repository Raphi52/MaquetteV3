// fix-ok: pas un correctif -- lot 11 du plan-f4fb097f (28/11/2026) : la grille lit « Dernier courrier envoyé » (le geste du courrier
// aux débiteurs, EcritureCloture.cs, l'écrit) et partage son filtre AExaminer avec ce geste ; trois retouches d'un même ajout.
using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Clôture » (PROC_CLOT, web/src/ecrans/EcranCloture.tsx) : les procédures collectives dont l'examen de clôture approche,
/// lues dans la base V3 du greffe (v2.PROCEDURE_COLLECTIVE, v2.EVENEMENT_PROCEDURE, v2.INSTANCE, v2.GERER_PC recopiées ;
/// rec.DossierRcs). LECTURE SEULE (LectureV2). Plan « RIG v3 100 % fonctionnel », lot 20, 27/11/2026, marche du lot 17.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_CLOT\PROC_CLOT\1-4-5 Selection PC\OPE_SELECT_PC.cs,
/// LoadDGVProcedures (:414-516) -- la grille « Sélection PC » :
///   - FILTRES (:441-454) : une PC du greffe (PRCLL_EXTERIEUR = 0), pas encore clôturée (PRCLL_CODE_CLOTURE IS NULL), qui a un
///     évènement « 4CLO… » FUTUR (EVNPR_SURVENU = 0, EVNPR_DELAI_EXPIRE = 0) dont la limite ISNULL(DATE_MAXI, DATE_MINI,
///     DATE_FIXE) est ENTRE la date de début et la date de fin, ou AVANT la date de fin sans début ; et AUCUNE instance de clôture
///     (INSTN_CODE_AFFAIRE parmi les réglages CLOT_CODE_AFFAIRE_SO, CLOT_CODE_AFFAIRE, CLOT_CODE_AFFAIRE_AV2006 :
///     ParamCLOT_AllCodeAffaire, PROC_CLOT.cs:45-65 ; un réglage absent n'ajoute aucun code). La V2 exige une date de fin
///     (ultToolRecherche grisé sans elle, :98) ; ici, sans date de fin, aucune borne de date.
///   - ORDRE : PRCLL_NUM_PC (:454).
///   - COLONNES : N° PC et nom de l'affaire (:528-532) ; la limite d'examen = la date maxi, mini ou fixe de l'évènement 4CLO futur
///     de PLUS GRANDE date (:672-676, « prendre la plus grande date si plusieurs EP ») ; « Forfait » = PRCLL_FORFAIT_FACTURE
///     (:686), jamais facturé (OPE_SELECT_PC.cs:2006) ; les liquidateurs en mission (ListeMandatairesPC_PCFiltre("LIQ"),
///     :525-542, :624-626) -- sans liquidateur, la V2 montre la PC, bloquée (« Pas de liquidateur présent… », :534).
///   - Le dossier ouvert par le clic : celui de la PC (PRCLL_ID_DSRCS, fiche rec.DossierRcs).
///   - « Dernier courrier envoyé » (:678-682) : la plus grande date des évènements de la PC au code du mode de clôture
///     (ParamCLOT_CodeEvePro, PROC_CLOT.cs:208-247) -- celui du courrier aux débiteurs (CLOT_EVE_PRO_INCITR, :232), le seul mode que
///     l'écran V3 joue (EcritureCloture.cs, lot 11 du plan du parcours complet, 28/11/2026).
/// NON LU : chambre, date et heure d'audience, type de classement, huissier -- ce sont les choix de l'agent dans la grille
/// d'enrôlement (OPE_SELECT_PC.cs:827, RechercheAudienceCabinet :1398), pas des données de la base ; les
/// dirigeants (ListeDirigeantsPC) ; l'étape « Enrôlement / requête » (OPE_ENROLEMENT_REQUETE, mode 2-3).
/// </summary>
public static class LectureCloture
{
    /// <summary>Au plus autant de procédures (le plafond des écrans faits main).</summary>
    public const int Plafond = RoutesEcransMain.Plafond;

    /// <summary>Les codes d'affaire des instances de clôture (ParamCLOT_AllCodeAffaire, PROC_CLOT.cs:49, :54, :59).</summary>
    internal static readonly LectureReglage CodeAffaireSo = new("CLOT_CODE_AFFAIRE_SO", "", "PROC_CLOT.cs:49");
    internal static readonly LectureReglage CodeAffaire = new("CLOT_CODE_AFFAIRE", "", "PROC_CLOT.cs:54");
    internal static readonly LectureReglage CodeAffaireAv2006 = new("CLOT_CODE_AFFAIRE_AV2006", "", "PROC_CLOT.cs:59");

    /// <summary>La limite d'un évènement : ISNULL(EVNPR_DATE_MAXI, ISNULL(EVNPR_DATE_MINI, EVNPR_DATE_FIXE)) (:447).</summary>
    private const string Limite = "ISNULL(e.EVNPR_DATE_MAXI, ISNULL(e.EVNPR_DATE_MINI, e.EVNPR_DATE_FIXE))";

    /// <summary>Un évènement 4CLO futur de la PC p (:446).</summary>
    private const string Evenement4Clo = """
        e.EVNPR_ID_PRCLL = p.PRCLL_ID_PRCLL AND e.EVNPR_CODE_EVE_PRO LIKE '4CLO%' AND e.EVNPR_SURVENU = 0 AND e.EVNPR_DELAI_EXPIRE = 0
        """;

    /// <summary>Une PC de la grille, sans borne de date (:441-454) : du greffe, non clôturée, un 4CLO futur, aucune instance de clôture
    /// (@code1 à @code3) -- aussi le contrôle du courrier aux débiteurs (EcritureCloture.cs) sur les PC cochées.</summary>
    internal static readonly string AExaminer = $"""
        p.PRCLL_EXTERIEUR = 0 AND p.PRCLL_CODE_CLOTURE IS NULL
          AND EXISTS (SELECT 1 FROM v2.EVENEMENT_PROCEDURE AS e WHERE {Evenement4Clo})
          AND NOT EXISTS (SELECT 1 FROM v2.INSTANCE AS i
                          WHERE i.INSTN_ID_PRCLL = p.PRCLL_ID_PRCLL AND i.INSTN_CODE_AFFAIRE IN (@code1, @code2, @code3))
        """;

    // « Dernier courrier envoyé » (:678-682) : la plus grande date des évènements de la PC au code du courrier aux débiteurs
    // (@courrier, réglage CLOT_EVE_PRO_INCITR : le seul mode de clôture que l'écran V3 joue, EcritureCloture.cs).
    private static readonly string Requete = $"""
        SELECT TOP (@plafond) p.PRCLL_ID_PRCLL, p.PRCLL_NUM_PC, p.PRCLL_NOM_AFFAIRE, p.PRCLL_FORFAIT_FACTURE, d.NumeroGestion,
               lim.Limite, {OrganesPc.MandatairesEnFonction("p", "'LIQ'")} AS Liquidateurs, COUNT(*) OVER () AS Total,
               (SELECT MAX(c.EVNPR_DATE) FROM v2.EVENEMENT_PROCEDURE AS c
                WHERE c.EVNPR_ID_PRCLL = p.PRCLL_ID_PRCLL AND c.EVNPR_CODE_EVE_PRO = @courrier) AS DernierCourrier
        FROM v2.PROCEDURE_COLLECTIVE AS p
        CROSS APPLY (SELECT TOP (1) {Limite} AS Limite FROM v2.EVENEMENT_PROCEDURE AS e
                     WHERE {Evenement4Clo} ORDER BY e.EVNPR_DATE DESC, e.EVNPR_ID_EVNPR DESC) AS lim
        LEFT JOIN rec.DossierRcs AS d ON d.Id = p.PRCLL_ID_DSRCS
        WHERE {AExaminer}
          AND EXISTS (SELECT 1 FROM v2.EVENEMENT_PROCEDURE AS e
                      WHERE {Evenement4Clo}
                        AND (@au IS NULL OR (@du IS NULL AND {Limite} < @au) OR (@du IS NOT NULL AND {Limite} BETWEEN @du AND @au)))
        ORDER BY p.PRCLL_NUM_PC, p.PRCLL_ID_PRCLL
        """;

    /// <summary>Le code de l'évènement du courrier aux débiteurs (ParamCLOT_CodeEvePro, mode 5 : PROC_CLOT.cs:232).</summary>
    internal static readonly LectureReglage CodeCourrier = new(EcritureCloture.ReglageEvenement, "", "PROC_CLOT.cs:232");

    /// <summary>Les procédures à examiner pour <paramref name="recherche"/>, au plus <paramref name="plafond"/>.</summary>
    public static async Task<AffairesClotureLues> LireAsync(SqlConnection baseGreffe, RechercheCloture recherche, CancellationToken ct,
                                                           int plafond = Plafond)
    {
        // Un réglage absent ou vide n'apporte aucun code : NULL ne vaut jamais un code d'affaire.
        async Task<SqlParameter> Code(string nom, LectureReglage r)
            => new(nom, SqlDbType.VarChar, 40) { Value = await r.LireTexteAsync(baseGreffe, ct) is { Length: > 0 } c ? c.Trim() : DBNull.Value };
        object Jour(DateOnly? d) => d is { } v ? v.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        var lignes = new List<AffaireClotureLue>();
        var total = 0;
        await using var r = await LectureV2.LireAsync(baseGreffe, Requete, ct,
            new SqlParameter("@plafond", SqlDbType.Int) { Value = plafond },
            new SqlParameter("@du", SqlDbType.DateTime) { Value = Jour(recherche.Du) },
            new SqlParameter("@au", SqlDbType.DateTime) { Value = Jour(recherche.Au) },
            await Code("@code1", CodeAffaireSo), await Code("@code2", CodeAffaire), await Code("@code3", CodeAffaireAv2006),
            await Code("@courrier", CodeCourrier));
        while (await r.ReadAsync(ct))
        {
            total = Convert.ToInt32(r["Total"], CultureInfo.InvariantCulture);
            lignes.Add(new AffaireClotureLue(Convert.ToInt32(r["PRCLL_ID_PRCLL"], CultureInfo.InvariantCulture),
                FormeLigne.Texte(r, "PRCLL_NUM_PC").Trim(), FormeLigne.Texte(r, "PRCLL_NOM_AFFAIRE").Trim(),
                FormeLigne.Texte(r, "NumeroGestion").Trim(), FormeLigne.DateDe(r, "Limite"), r["PRCLL_FORFAIT_FACTURE"] is true,
                FormeLigne.Texte(r, "Liquidateurs").Trim(), FormeLigne.DateDe(r, "DernierCourrier")));
        }
        return new AffairesClotureLues([.. lignes], total);
    }

    // ================================================================ LES ENRÔLEMENTS (onglet « Enrôlement / requête »)

    /// <summary>Les évènements de convocation des trois modes d'enrôlement (ParamCLOT_AllCodeEvePro, PROC_CLOT.cs:165-203).</summary>
    internal static readonly LectureReglage ConvocationRequete = new("CLOT_EVE_PRO_CONV", "", "PROC_CLOT.cs:172");
    internal static readonly LectureReglage ConvocationLrar = new("CLOT_EVE_PRO_CONVSO_LRAR", "", "PROC_CLOT.cs:178");
    internal static readonly LectureReglage ConvocationHuissier = new("CLOT_EVE_PRO_CONVSO_HUI", "", "PROC_CLOT.cs:183");

    /// <summary>
    /// Les instances de clôture enrôlées : celles des codes d'affaire de clôture (@code1 à @code3, ParamCLOT_AllCodeAffaire), sur une PC,
    /// dont l'enrôlement est entre @du et @au (sans borne : toutes), les plus récentes d'abord. Les colonnes de l'étape « Résultats » de
    /// RIG v2 (OPE_RESULTATS.cs:267-299) : n° de PC et de rôle, acte de saisine (son libellé), premier demandeur, premier défendeur
    /// (« [n] » devant s'il y en a plusieurs, :278), l'audience du premier appel (:284) ; et ce que l'enrôlement sur saisine d'office pose
    /// (EcritureCloture.EnrolerAsync) : le classement et l'heure de l'appel, la dernière convocation survenue de l'instance, le 4CLO survenu
    /// de la PC. La V2 ne liste à cette étape que les enrôlements sur requête ; la V3 y montre les trois codes, pour relire aussi ceux sur
    /// saisine d'office.
    /// </summary>
    private static readonly string RequeteEnrolements = """
        SELECT TOP (@plafond) i.INSTN_ID_INSTN, i.INSTN_NUM_ROLE, i.INSTN_NUM_REP_GENERAL, p.PRCLL_ID_PRCLL, p.PRCLL_NUM_PC, i.INSTN_DATE_ENROLEMENT,
               ISNULL(ap.R_CODEACTEPR_LIBELLE_ECRAN, ISNULL(i.INSTN_CODE_ACTE_PROCE, '')) AS Acte, dem.Designation AS Demandeur,
               CASE WHEN def.Nombre > 1 THEN '[' + CONVERT(varchar(10), def.Nombre) + '] ' ELSE '' END + ISNULL(def.Designation, '') AS Defendeur,
               app.AUDNC_DATE, app.AUDNC_HEURE, app.AUDNC_CHAMBRE, app.APPAF_TYPE_CLASSEMENT, app.APPAF_HEURE_APPEL,
               conv.EVNPR_CODE_EVE_PRO AS Convocation, conv.EVNPR_DATE AS DateConvocation,
               (SELECT MAX(e.EVNPR_DATE) FROM v2.EVENEMENT_PROCEDURE AS e
                WHERE e.EVNPR_ID_PRCLL = p.PRCLL_ID_PRCLL AND e.EVNPR_CODE_EVE_PRO LIKE '4CLO%' AND e.EVNPR_SURVENU = 1) AS ExamenSurvenu,
               COUNT(*) OVER () AS Total
        FROM v2.INSTANCE AS i
        JOIN v2.PROCEDURE_COLLECTIVE AS p ON p.PRCLL_ID_PRCLL = i.INSTN_ID_PRCLL
        LEFT JOIN v2.CODE_ACTE_PROCE AS ap ON ap.R_CODEACTEPR_VALEUR = i.INSTN_CODE_ACTE_PROCE
        OUTER APPLY (SELECT TOP (1) pr.PRT_DESIGNATION AS Designation FROM v2.PARTIE AS pr
                     WHERE pr.PRT_ID_INSTN = i.INSTN_ID_INSTN AND pr.PRT_QUALIF_PARTIE LIKE 'D%' ORDER BY pr.PRT_NUM_ORDRE, pr.PRT_ID_PRT) AS dem
        OUTER APPLY (SELECT TOP (1) pr.PRT_DESIGNATION AS Designation, COUNT(*) OVER () AS Nombre FROM v2.PARTIE AS pr
                     WHERE pr.PRT_ID_INSTN = i.INSTN_ID_INSTN AND pr.PRT_QUALIF_PARTIE LIKE 'F%' ORDER BY pr.PRT_NUM_ORDRE, pr.PRT_ID_PRT) AS def
        OUTER APPLY (SELECT TOP (1) a.AUDNC_DATE, a.AUDNC_HEURE, a.AUDNC_CHAMBRE, aa.APPAF_TYPE_CLASSEMENT, aa.APPAF_HEURE_APPEL
                     FROM v2.APPEL_AFFAIRE AS aa LEFT JOIN v2.AUDIENCE_CABINET AS a ON a.AUDNC_ID_ADNC = aa.APPAF_ID_ADNC
                     WHERE aa.APPAF_ID_INSTN = i.INSTN_ID_INSTN ORDER BY aa.APPAF_NUM_ORDRE, aa.APPAF_ID_APPAF) AS app
        OUTER APPLY (SELECT TOP (1) e.EVNPR_CODE_EVE_PRO, e.EVNPR_DATE FROM v2.EVENEMENT_PROCEDURE AS e
                     WHERE e.EVNPR_ID_INSTN = i.INSTN_ID_INSTN AND e.EVNPR_SURVENU = 1 AND e.EVNPR_CODE_EVE_PRO IN (@conv1, @conv2, @conv3)
                     ORDER BY e.EVNPR_DATE DESC, e.EVNPR_ID_EVNPR DESC) AS conv
        WHERE i.INSTN_CODE_AFFAIRE IN (@code1, @code2, @code3)
          AND (@du IS NULL OR i.INSTN_DATE_ENROLEMENT >= @du) AND (@au IS NULL OR i.INSTN_DATE_ENROLEMENT < DATEADD(day, 1, @au))
        ORDER BY i.INSTN_DATE_ENROLEMENT DESC, i.INSTN_ID_INSTN DESC
        """;

    /// <summary>Les instances de clôture enrôlées (<see cref="RequeteEnrolements"/>), au plus <paramref name="plafond"/>.</summary>
    public static async Task<EnrolementsClotureLus> EnrolementsAsync(SqlConnection baseGreffe, RechercheCloture recherche, CancellationToken ct,
                                                                    int plafond = Plafond)
    {
        async Task<SqlParameter> Code(string nom, LectureReglage r)
            => new(nom, SqlDbType.VarChar, 40) { Value = await r.LireTexteAsync(baseGreffe, ct) is { Length: > 0 } c ? c.Trim() : DBNull.Value };
        object Jour(DateOnly? d) => d is { } v ? v.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        var lignes = new List<EnrolementClotureLu>();
        var total = 0;
        await using var r = await LectureV2.LireAsync(baseGreffe, RequeteEnrolements, ct,
            new SqlParameter("@plafond", SqlDbType.Int) { Value = plafond },
            new SqlParameter("@du", SqlDbType.DateTime) { Value = Jour(recherche.Du) },
            new SqlParameter("@au", SqlDbType.DateTime) { Value = Jour(recherche.Au) },
            await Code("@code1", CodeAffaireSo), await Code("@code2", CodeAffaire), await Code("@code3", CodeAffaireAv2006),
            await Code("@conv1", ConvocationRequete), await Code("@conv2", ConvocationLrar), await Code("@conv3", ConvocationHuissier));
        while (await r.ReadAsync(ct))
        {
            total = Convert.ToInt32(r["Total"], CultureInfo.InvariantCulture);
            string T(string c) => FormeLigne.Texte(r, c).Trim();
            lignes.Add(new EnrolementClotureLu(Convert.ToInt32(r["INSTN_ID_INSTN"], CultureInfo.InvariantCulture), T("INSTN_NUM_ROLE"),
                T("INSTN_NUM_REP_GENERAL"), Convert.ToInt32(r["PRCLL_ID_PRCLL"], CultureInfo.InvariantCulture), T("PRCLL_NUM_PC"), T("Demandeur"),
                T("Defendeur"), T("Acte"), FormeLigne.DateDe(r, "INSTN_DATE_ENROLEMENT"), FormeLigne.DateDe(r, "AUDNC_DATE"), T("AUDNC_HEURE"),
                T("AUDNC_CHAMBRE"), T("APPAF_TYPE_CLASSEMENT"), T("APPAF_HEURE_APPEL"), T("Convocation"), FormeLigne.DateDe(r, "DateConvocation"),
                FormeLigne.DateDe(r, "ExamenSurvenu")));
        }
        return new EnrolementsClotureLus([.. lignes], total);
    }
}
