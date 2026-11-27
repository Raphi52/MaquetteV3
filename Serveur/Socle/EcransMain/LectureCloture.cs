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
/// NON LU : chambre, date et heure d'audience, type de classement, huissier -- ce sont les choix de l'agent dans la grille
/// d'enrôlement (OPE_SELECT_PC.cs:827, RechercheAudienceCabinet :1398), pas des données de la base ; « Dernier courrier » (:678-682)
/// dépend du mode de clôture choisi (ParamCLOT_CodeEvePro, PROC_CLOT.cs:208-244), que l'écran V3 ne propose pas ; les
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

    private static readonly string Requete = $"""
        SELECT TOP (@plafond) p.PRCLL_ID_PRCLL, p.PRCLL_NUM_PC, p.PRCLL_NOM_AFFAIRE, p.PRCLL_FORFAIT_FACTURE, d.NumeroGestion,
               lim.Limite, {OrganesPc.MandatairesEnFonction("p", "'LIQ'")} AS Liquidateurs, COUNT(*) OVER () AS Total
        FROM v2.PROCEDURE_COLLECTIVE AS p
        CROSS APPLY (SELECT TOP (1) {Limite} AS Limite FROM v2.EVENEMENT_PROCEDURE AS e
                     WHERE {Evenement4Clo} ORDER BY e.EVNPR_DATE DESC, e.EVNPR_ID_EVNPR DESC) AS lim
        LEFT JOIN rec.DossierRcs AS d ON d.Id = p.PRCLL_ID_DSRCS
        WHERE p.PRCLL_EXTERIEUR = 0 AND p.PRCLL_CODE_CLOTURE IS NULL
          AND EXISTS (SELECT 1 FROM v2.EVENEMENT_PROCEDURE AS e
                      WHERE {Evenement4Clo}
                        AND (@au IS NULL OR (@du IS NULL AND {Limite} < @au) OR (@du IS NOT NULL AND {Limite} BETWEEN @du AND @au)))
          AND NOT EXISTS (SELECT 1 FROM v2.INSTANCE AS i
                          WHERE i.INSTN_ID_PRCLL = p.PRCLL_ID_PRCLL AND i.INSTN_CODE_AFFAIRE IN (@code1, @code2, @code3))
        ORDER BY p.PRCLL_NUM_PC, p.PRCLL_ID_PRCLL
        """;

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
            await Code("@code1", CodeAffaireSo), await Code("@code2", CodeAffaire), await Code("@code3", CodeAffaireAv2006));
        while (await r.ReadAsync(ct))
        {
            total = Convert.ToInt32(r["Total"], CultureInfo.InvariantCulture);
            lignes.Add(new AffaireClotureLue(Convert.ToInt32(r["PRCLL_ID_PRCLL"], CultureInfo.InvariantCulture),
                FormeLigne.Texte(r, "PRCLL_NUM_PC").Trim(), FormeLigne.Texte(r, "PRCLL_NOM_AFFAIRE").Trim(),
                FormeLigne.Texte(r, "NumeroGestion").Trim(), FormeLigne.DateDe(r, "Limite"), r["PRCLL_FORFAIT_FACTURE"] is true,
                FormeLigne.Texte(r, "Liquidateurs").Trim()));
        }
        return new AffairesClotureLues([.. lignes], total);
    }
}
