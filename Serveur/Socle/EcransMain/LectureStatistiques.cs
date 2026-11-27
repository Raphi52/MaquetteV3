using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Statistiques » (PROC_STAT, web/src/ecrans/EcranStatistiques.tsx) : les six volets de RIG v2 CALCULÉS dans la base V3 du
/// greffe, en lecture seule (LectureV2). Plan « RIG v3 100 % fonctionnel », lot 9, 27/11/2026, sur la marche du lot 17.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_STAT (les volets, leurs dates et leur niveau) et les calculs qu'ils
/// appellent, Outils\DLLEdition\RigRapport\Jud\Stats -- chaque requête est reprise TELLE QUELLE, ses tables lues dans v2.* ou,
/// reprises en fiche, sous leurs noms V2 (LecteurPilotage.Traduire, la correspondance de Serveur/Recopie/FichesV3.cs) ; les dates
/// passent en paramètres au lieu d'être concaténées (CONVERT(DATETIME, '<date>', 103)) :
///   - « Statistiques internes » (MetierStats.GenererStatsInternes, Internes\MetierStats.cs:23-115) : judiciaires (:118-260),
///     procédures collectives (:262-587), clôtures (:589-614), injonctions de payer (:619-700) ;
///   - « Statistiques web » (GenererStatsWeb, :817-1030) : huit familles de PC par tranche de chiffre d'affaires (4) et de
///     salariés (5), puis les clôtures par code (:1613-1729) ; les tranches sont les mêmes requêtes pour toutes les familles ;
///   - « Répartitions des JC et OP » (GenererStatsRepartitionsJCOP, :704-814) ;
///   - « Ouverture par activités » par chiffre d'affaires et par salariés (MetierStats.Activites.cs), au niveau NAF choisi ;
///   - « Statistiques semestrielles » (Semestrielles\RapportStatMandataire.cs) : les mandats de la période et les totaux par
///     mandataire, et le nombre de PC que l'écran V2 affiche (OPE_STATS_SEMESTRIELLES._MajNbPC, :145-152).
/// LES RÉGLAGES : STAT_TYPE_DECISION_JUGEMENTS_INSTRUCTION / _SIEGE (MetierStats.cs:190-210) et STAT_CONV_LJ_LJS (:1336-1341), lus
/// dans soc.Reglage (la reprise de PARAMETRAGE, AudiencesV2.cs:24-26), à défaut les valeurs du code. Chaque code y est un mot
/// (« 'JF','ZIND' ») : un réglage illisible est nommé, sa requête n'est pas jouée.
/// LA PÉRIODE : refusée si le début suit la fin, au message de RIG v2 (OPE_DATES.DoVerifier, OPE_DATES.cs:35-40) ; les dates
/// sont des jours à 0 h, comme celles de l'écran V2 (la fin à 0 h, sauf les clôtures internes, :606-612, et les semestrielles,
/// au lendemain exclu).
/// CE QUE LA V3 NE REPREND PAS (nommé) : le classeur Excel (la maquette STAT_* de la GED, AmiAsposeExcel) -- les chiffres sont
/// montrés à l'écran, tableau par tableau ; « Accéder aux archives » des semestrielles (l'explorateur Windows). ÉCART À RIG v2,
/// nommé : les activités regroupent les lignes d'un même code NAF en ADDITIONNANT leurs nombres, là où RIG v2 colle leurs textes
/// (« 3 » puis « 2 » donnent « 32 », MetierStats.Activites.cs:129, un 🐞 du code V2).
/// </summary>
public static partial class LectureStatistiques
{
    /// <summary>Les volets, au mot près ceux de l'écran (EcranStatistiques.tsx) : Contrats/EcransMain/LectureStatistiques.cs.</summary>
    public const string Internes = VoletsStatistiques.Internes, Web = VoletsStatistiques.Web, RepartitionsJcOp = VoletsStatistiques.RepartitionsJcOp,
                        ActivitesCa = VoletsStatistiques.ActivitesCa, ActivitesSalaries = VoletsStatistiques.ActivitesSalaries,
                        Semestrielles = VoletsStatistiques.Semestrielles;

    /// <summary>Le message de RIG v2 quand le début suit la fin (OPE_DATES.cs:37-38, OPE_STATS_SEMESTRIELLES.cs:93-95).</summary>
    public const string DatesInversees = "La date de début ne peut pas être supérieur à la date de fin";

    /// <summary>Au plus autant de lignes par tableau (le plafond des écrans faits main) ; les totaux comptent tout.</summary>
    public const int Plafond = 500;

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Le volet <paramref name="r"/> calculé dans la base du greffe.</summary>
    public static async Task<StatistiquesLues> LireAsync(SqlConnection baseGreffe, RechercheStatistiques r, CancellationToken ct)
    {
        if (r.Debut.Date > r.Fin.Date) return new StatistiquesLues(r.Volet, [], DatesInversees);
        var c = new Calcul(baseGreffe, r.Debut.Date, r.Fin.Date, ct);
        await c.ChargerAsync();
        return r.Volet switch
        {
            Internes => new StatistiquesLues(r.Volet, await StatsInternesAsync(c)),
            Web => new StatistiquesLues(r.Volet, await StatsWebAsync(c)),
            RepartitionsJcOp => new StatistiquesLues(r.Volet, await RepartitionsAsync(c)),
            ActivitesCa or ActivitesSalaries => new StatistiquesLues(r.Volet, await ActivitesAsync(c, r.Volet == ActivitesCa, r.Niveau)),
            Semestrielles => await SemestriellesAsync(c, r.Volet),
            _ => new StatistiquesLues(r.Volet, [], $"Volet inconnu de l'écran « Statistiques » : {r.Volet}."),
        };
    }

    // ============================================================ le moteur : une requete de RIG v2 sur la base V3

    /// <summary>Une ligne lue : ses colonnes par nom (sans casse).</summary>
    private sealed class Ligne(Dictionary<string, object?> colonnes)
    {
        public object? this[string nom] => colonnes.TryGetValue(nom, out var v) ? v : null;
        public string Texte(string nom) => this[nom] is { } v and not DBNull ? Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() ?? "" : "";
        public int Entier(string nom) => this[nom] is { } v and not DBNull ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : 0;
        public decimal Decimal(string nom) => this[nom] is { } v and not DBNull ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) : 0m;
    }

    private sealed class Calcul(SqlConnection cn, DateTime debut, DateTime fin, CancellationToken ct)
    {
        public DateTime Debut { get; } = debut;
        public DateTime Fin { get; } = fin;
        public HashSet<string> Tables { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Reglages { get; } = new(StringComparer.OrdinalIgnoreCase);

        public async Task ChargerAsync()
        {
            await using (var r = await LectureV2.LireAsync(cn, "SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID('v2')", ct))
                while (await r.ReadAsync(ct)) Tables.Add(r.GetString(0));
            await using (var r = await LectureV2.LireAsync(cn, """
                SELECT Code, Valeur FROM soc.Reglage
                WHERE Code IN ('STAT_TYPE_DECISION_JUGEMENTS_INSTRUCTION', 'STAT_TYPE_DECISION_JUGEMENTS_SIEGE', 'STAT_CONV_LJ_LJS')
                """, ct))
                while (await r.ReadAsync(ct))
                    if (!r.IsDBNull(1) && r.GetString(1).Trim().Length > 0) Reglages[r.GetString(0)] = r.GetString(1);
        }

        /// <summary>
        /// Les lignes de la requete V2 <paramref name="sqlV2"/> (dates @debut, @fin, @finJour = fin a 23:59:59, @lendemain), ou
        /// pourquoi elle ne se calcule pas : une table absente, une colonne de fiche non reprise, une erreur de la base.
        /// </summary>
        public async Task<(List<Ligne>? Lignes, string? Cause)> LireAsync(string sqlV2)
        {
            if (LecteurPilotage.Traduire(sqlV2, Tables, out var sql) is { } cause) return (null, cause);
            try
            {
                var lignes = new List<Ligne>();
                await using var r = await LectureV2.LireAsync(cn, sql, ct,
                    new SqlParameter("@debut", System.Data.SqlDbType.DateTime) { Value = Debut },
                    new SqlParameter("@fin", System.Data.SqlDbType.DateTime) { Value = Fin },
                    new SqlParameter("@finJour", System.Data.SqlDbType.DateTime) { Value = Fin.AddDays(1).AddSeconds(-1) },
                    new SqlParameter("@lendemain", System.Data.SqlDbType.DateTime) { Value = Fin.AddDays(1) });
                while (await r.ReadAsync(ct))
                {
                    var colonnes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < r.FieldCount; i++) colonnes[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
                    lignes.Add(new Ligne(colonnes));
                }
                return (lignes, null);
            }
            catch (SqlException e)
            {
                return (null, $"requête de RIG v2 en erreur : {e.Message}");
            }
        }

        /// <summary>La colonne <paramref name="colonne"/> de la premiere ligne (selectOneInt / selectOneDouble), ou la cause.</summary>
        public async Task<string> UnAsync(string sqlV2, string colonne = "NB")
        {
            var (lignes, cause) = await LireAsync(sqlV2);
            if (cause is not null) return $"non calculé : {cause}";
            return lignes!.Count == 0 ? "0" : Nombre(lignes[0][colonne]);
        }

        /// <summary>Les codes d'un reglage STAT_* (« 'JF','ZIND' »), a defaut <paramref name="defaut"/> ; null si illisible.</summary>
        public string? Codes(string reglage, string defaut)
        {
            var valeur = Reglages.TryGetValue(reglage, out var v) ? v : defaut;
            var codes = valeur.Split(',').Select(x => x.Trim().Trim('\'').Trim()).ToArray();
            return codes.Length > 0 && codes.All(x => MotDeCode().IsMatch(x)) ? string.Join(",", codes.Select(x => $"'{x}'")) : null;
        }
    }

    private static string Nombre(object? v) => v switch
    {
        null or DBNull => "0",
        decimal d => d.ToString("0.##", Fr),
        double d => d.ToString("0.##", Fr),
        float f => f.ToString("0.##", Fr),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
    };

    private static string Entier(int n) => n.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^[A-Za-z0-9_]+$")]
    private static partial Regex MotDeCode();

    private static readonly string[] IndicateurNombre = ["Indicateur", "Nombre"];

    /// <summary>Un tableau « Indicateur | Nombre » : une requete par ligne ; sans requete (null), le reglage qui manque.</summary>
    private static async Task<List<LigneStatistique>> LignesAsync(Calcul c, IEnumerable<(string Libelle, string? Sql)> lignes)
    {
        var lues = new List<LigneStatistique>();
        foreach (var (libelle, sql) in lignes)
            lues.Add(new LigneStatistique(libelle, [sql is null ? $"non calculé : {ReglageIllisible}" : await c.UnAsync(sql)]));
        return lues;
    }

    private const string ReglageIllisible = "un réglage STAT_* du greffe n'est pas une liste de codes (« 'JF','ZIND' »)";

    // ============================================================ « Statistiques internes » (MetierStats.cs:23-700)

    private const string Periode = "BETWEEN @debut AND @fin";

    // RequeteAffaires (:170-188) : le motif du numero de role.
    private static string Affaires(string motif)
        => $"SELECT COUNT(distinct INSTN_ID_INSTN) AS NB FROM INSTANCE WITH (NOLOCK) WHERE (INSTN_DATE_ENROLEMENT {Periode}) AND (INSTN_NUM_ROLE LIKE '{motif}')";

    // RequeteJugements (:190-229).
    private static string Jugements(string condition)
        => "SELECT COUNT(DISTINCT INSTANCE.INSTN_ID_INSTN) AS NB FROM INSTANCE WITH (NOLOCK) INNER JOIN "
         + "DECISION_JUDICIAIRE WITH (NOLOCK) ON INSTN_ID_INSTN = DCJDC_ID_INSTN INNER JOIN "
         + "APPEL_AFFAIRE WITH (NOLOCK) ON DCJDC_ID_APPAF_PRONONCER = APPAF_ID_APPAF INNER JOIN "
         + "AUDIENCE_CABINET WITH (NOLOCK) ON APPAF_ID_ADNC = AUDNC_ID_ADNC WHERE "
         + "(INSTN_NUM_ROLE LIKE '[0-9][0-9][0-9][0-9][F,J][0-9]%') AND " + condition + $"(AUDNC_DATE {Periode})";

    // RequeteInjPay / RequeteOrdoReq (:242-260) : une decision rendue, ou prevue, dans la periode.
    private static string DecisionsDuRole(string motif)
        => "SELECT COUNT(DISTINCT INSTANCE.INSTN_ID_INSTN) AS NB FROM INSTANCE WITH (NOLOCK) INNER JOIN "
         + "DECISION_JUDICIAIRE WITH (NOLOCK) ON INSTN_ID_INSTN = DCJDC_ID_INSTN WHERE "
         + $"(INSTN_NUM_ROLE LIKE '{motif}') AND (IsNull(DCJDC_DATE,DCJDC_DATE_PREVUE) {Periode})";

    private const string PcHorsExterieur = "SELECT COUNT(DISTINCT PRCLL_ID_PRCLL) as NB FROM PROCEDURE_COLLECTIVE WITH (NOLOCK) WHERE (PRCLL_EXTERIEUR = 0)";

    // RequetePlan (:419-453) : la solution 1 OU 2 prononcee dans la periode, au prefixe du code.
    private static string Plan(string prefixe)
        => $"{PcHorsExterieur} AND ((PRCLL_DATE_SOLUTION {Periode} AND PRCLL_CODE_SOLUTION LIKE '{prefixe}%')"
         + $" OR (PRCLL_DATE_SOLUTION_2 {Periode} AND PRCLL_CODE_SOLUTION_2 LIKE '{prefixe}%'))";

    private const string PcChiffres = "FROM PROCEDURE_COLLECTIVE WITH (NOLOCK) INNER JOIN CHIFFRES_PC WITH (NOLOCK) ON PRCLL_ID_PRCLL = CHFPC_ID_PRCLL WHERE";

    // RequeteOuverturePC (:549-567) : l'acte de saisine (CODE_ACTE_PROCE.R_CODEACTEPR_SYNO_AMITEL).
    private static string OuverturePc(string syno)
        => "SELECT COUNT(DISTINCT PRCLL_ID_PRCLL) as NB FROM PROCEDURE_COLLECTIVE WITH (nolock) INNER JOIN CODE_ACTE_PROCE WITH (nolock)"
         + " ON PRCLL_CODE_ACTE_PROCE = R_CODEACTEPR_VALEUR WHERE (PRCLL_EXTERIEUR = 0) AND"
         + $" (IsNull(PRCLL_DATE_SAUVEGARDE, PRCLL_DATE_OUVERTURE) {Periode}) AND (R_CODEACTEPR_SYNO_AMITEL = '{syno}')";

    // RequeteDessaisissement / RequeteDeboutes (:529-547).
    private static string EvenementF(string code)
        => "SELECT COUNT(DISTINCT INSTN_ID_INSTN) as NB FROM INSTANCE WITH (nolock) INNER JOIN EVENEMENT_PROCEDURE WITH (nolock) "
         + "ON INSTN_ID_INSTN = EVNPR_ID_INSTN WHERE (INSTN_NUM_ROLE LIKE '[0-9][0-9][0-9][0-9][F][0-9]%') AND "
         + $"(EVNPR_DATE {Periode}) AND (EVNPR_CODE_EVE_PRO = '{code}')";

    // RequeteOrdoFinInst / RequeteExecutoires / RequeteOppositions (:665-698).
    private static string EvenementIp(string codes)
        => "SELECT count(distinct INSTN_ID_INSTN) as NB FROM INSTANCE WITH (nolock) INNER JOIN "
         + "EVENEMENT_PROCEDURE WITH (nolock) ON INSTN_ID_INSTN = EVNPR_ID_INSTN WHERE "
         + $"(INSTN_NUM_ROLE LIKE '%IP%') AND (EVNPR_DATE {Periode}) AND (EVNPR_CODE_EVE_PRO in ({codes}))";

    private static async Task<TableauStatistique[]> StatsInternesAsync(Calcul c)
    {
        // Les types de decision des jugements (:192-208) : a defaut, 'JF','ZIND','ZINCOMP'.
        var lus = c.Codes("STAT_TYPE_DECISION_JUGEMENTS_INSTRUCTION", "'JF','ZIND','ZINCOMP'");
        var lus2 = c.Codes("STAT_TYPE_DECISION_JUGEMENTS_SIEGE", "'JF','ZIND','ZINCOMP'");
        static string? Jug(string? condition) => condition is null ? null : Jugements(condition);
        var judiciaires = new TableauStatistique("Statistiques judiciaires", IndicateurNombre, [.. await LignesAsync(c,
        [
            ("Affaires enrôlées", Affaires("[0-9][0-9][0-9][0-9][F,J][0-9]%")),
            ("Affaires en file R enrôlées", Affaires("%[0-9][R][0-9]%")),
            ("Affaires en file J enrôlées", Affaires("%[0-9][J][0-9]%")),
            ("Affaires en file F enrôlées", Affaires("%[0-9][F][0-9]%")),
            ("Jugements après instruction", Jug(lus is null ? null : $"(DCJDC_TYPE_DECISION IN ({lus})) AND (DCJDC_ID_APPAF_PRONONCER <> DCJDC_ID_APPAF_DELIBERER) AND ")),
            ("Jugements sur siège", Jug(lus2 is null ? null : $"(DCJDC_TYPE_DECISION IN ({lus2})) AND (DCJDC_ID_APPAF_PRONONCER = DCJDC_ID_APPAF_DELIBERER) AND ")),
            ("Autres", Jug(lus is null || lus2 is null ? null : $"(DCJDC_TYPE_DECISION NOT IN ({lus},{lus2})) AND ")),
            // RequeteOrdoRefere (:231-240) : role R, audience de prononce dans la periode.
            ("Ordonnances de référé", "SELECT COUNT(DISTINCT INSTANCE.INSTN_ID_INSTN) AS NB FROM INSTANCE WITH (NOLOCK) INNER JOIN "
                + "DECISION_JUDICIAIRE WITH (NOLOCK) ON INSTN_ID_INSTN = DCJDC_ID_INSTN INNER JOIN "
                + "APPEL_AFFAIRE WITH (NOLOCK) ON DCJDC_ID_APPAF_PRONONCER = APPAF_ID_APPAF INNER JOIN "
                + "AUDIENCE_CABINET WITH (NOLOCK) ON APPAF_ID_ADNC = AUDNC_ID_ADNC WHERE "
                + $"(INSTN_NUM_ROLE LIKE '[0-9][0-9][0-9][0-9][R][0-9]%') AND (AUDNC_DATE {Periode})"),
            ("Injonctions de payer", DecisionsDuRole("[0-9][0-9][0-9][0-9]IP[0-9]%")),
            ("Ordonnances sur requête", DecisionsDuRole("[0-9][0-9][0-9][0-9]OP[0-9]%")),
        ])]);

        const string ouvertureRj = $"(PRCLL_DATE_OUVERTURE {Periode}) AND (PRCLL_DATE_SAUVEGARDE IS NULL)"
            + " AND (PRCLL_CODE_SOLUTION IS NULL OR NOT (PRCLL_CODE_SOLUTION LIKE 'LIJI%'))"
            + " AND (PRCLL_CODE_SOLUTION_2 IS NULL OR PRCLL_CODE_SOLUTION_2 NOT LIKE 'LIJI%')";
        const string ouvertureLj = $"(PRCLL_DATE_SAUVEGARDE IS NULL) AND ((PRCLL_DATE_SOLUTION {Periode} AND PRCLL_CODE_SOLUTION LIKE 'LIJI%')"
            + $" OR (PRCLL_DATE_SOLUTION_2 {Periode} AND PRCLL_CODE_SOLUTION_2 LIKE 'LIJI%'))";
        var pcLignes = new List<(string, string?)>
        {
            ("Ouvertures de procédures RJ", $"{PcHorsExterieur} AND {ouvertureRj}"),                                 // :371-380
            ("Ouvertures de procédures LJ", $"{PcHorsExterieur} AND {ouvertureLj}"),                                 // :382-399, « N »
            ("Ouvertures de procédures LJ simplifiées", $"{PcHorsExterieur} AND {ouvertureLj} AND  (PRCLL_REGIME_PC in ('5', '7', '8'))"),
            ("Ouvertures de sauvegardes", $"{PcHorsExterieur} AND (PRCLL_DATE_SAUVEGARDE {Periode})"),           // :401-407
            ("Conversion de sauvegarde en RJ", $"{PcHorsExterieur} AND (PRCLL_DATE_SAUVEGARDE is not NULL) AND (PRCLL_DATE_OUVERTURE {Periode})"
                + " AND (PRCLL_CODE_SOLUTION IS NULL OR PRCLL_CODE_SOLUTION NOT LIKE 'LIJI%')"
                + " AND (PRCLL_CODE_SOLUTION_2 IS NULL OR PRCLL_CODE_SOLUTION_2 NOT LIKE 'LIJI%')"),                    // :409-417
            ("Conversion en LJ", Plan("LJ")),
            ("Plan de cession", Plan("CS")),
            ("Plans de continuation", Plan("CT")),
            ("Plans de sauvegarde", Plan("PLSAU")),
            ("Salariés concernés par les ouvertures de RJ", $"SELECT isnull(SUM(CHFPC_SALARIES_OUVERTURE),0) AS NB {PcChiffres} (PRCLL_EXTERIEUR = '0') AND {ouvertureRj}"),
            ("Salariés concernés par les ouvertures de LJ", $"SELECT isnull(SUM(CHFPC_SALARIES_OUVERTURE),0) AS NB {PcChiffres} (PRCLL_EXTERIEUR = 0) AND {ouvertureLj}"),
            ("Salariés concernés par les ouvertures de sauvegarde", $"SELECT isnull(SUM(CHFPC_SALARIES_OUVERTURE),0) AS NB {PcChiffres} (PRCLL_EXTERIEUR = 0) AND (PRCLL_DATE_SAUVEGARDE {Periode})"),
            ("Salariés concernés par les ouvertures (total)", $"SELECT isnull(SUM(CHFPC_SALARIES_OUVERTURE),0) AS NB {PcChiffres} (IsNull(PRCLL_DATE_SAUVEGARDE, PRCLL_DATE_OUVERTURE) {Periode}) AND (PRCLL_EXTERIEUR = '0')"),
            ("Salariés repris dans les plans", $"SELECT ISNULL(SUM(CHFPC_SALARIES_PLAN),0) AS NB {PcChiffres} (PRCLL_EXTERIEUR = '0')"
                + $" AND ((PRCLL_DATE_SOLUTION {Periode} AND (PRCLL_CODE_SOLUTION like 'CS%' OR PRCLL_CODE_SOLUTION like 'CT%' OR PRCLL_CODE_SOLUTION = 'PLSAU'))"
                + $" OR (PRCLL_DATE_SOLUTION_2 {Periode} AND (PRCLL_CODE_SOLUTION_2 like 'CS%' OR PRCLL_CODE_SOLUTION_2 like 'CT%' OR PRCLL_CODE_SOLUTION_2 = 'PLSAU')))"),
        };
        var pc = await LignesAsync(c, pcLignes);
        // RequeteCA (:509-516) : une requete, trois colonnes.
        const string ca = "SELECT ISNULL(SUM(ROUND(CHFPC_CHIFFRE_AFFAIRE, 2)),0) AS CA, ISNULL(SUM(CHFPC_DECLARE_TOTAL),0) as PASSIF, "
            + $"ISNULL(SUM(CHFPC_ACTIF_DECLARE),0) as ACTIF {PcChiffres} (PRCLL_EXTERIEUR = '0') AND (IsNull(PRCLL_DATE_SAUVEGARDE, PRCLL_DATE_OUVERTURE) {Periode})";
        pc.Add(new LigneStatistique("Chiffre d'affaires des entreprises", [await c.UnAsync(ca, "CA")]));
        pc.Add(new LigneStatistique("Passif déclaré", [await c.UnAsync(ca, "PASSIF")]));
        pc.Add(new LigneStatistique("Actif déclaré", [await c.UnAsync(ca, "ACTIF")]));
        pc.AddRange(await LignesAsync(c,
        [
            ("Assignations en RJ ou LJ", "SELECT COUNT(DISTINCT INSTN_ID_INSTN) as NB FROM INSTANCE WITH (nolock) INNER JOIN CODE_ACTE_PROCE WITH (nolock)"
                + " ON INSTN_CODE_ACTE_PROCE = R_CODEACTEPR_VALEUR WHERE (INSTN_NUM_ROLE LIKE '[0-9][0-9][0-9][0-9][F][0-9]%') AND "
                + $"(INSTN_DATE_ENROLEMENT {Periode}) AND (R_CODEACTEPR_SYNO_AMITEL = 'ASS')"
                + " AND (INSTN_NATURE_AFFAIRE = '40a' OR INSTN_NATURE_AFFAIRE = '40b' OR INSTN_NATURE_AFFAIRE = '4ae' OR INSTN_NATURE_AFFAIRE = '4af')"),
            ("Dessaisissements", EvenementF("DESRJ")),
            ("Déboutés", EvenementF("DEBOUTRJ")),
            ("Ouvertures de PC par assignation", OuverturePc("ASS")),
            ("Ouvertures de PC par saisine du parquet", OuverturePc("SORP")),
            ("Ouvertures de PC par saisine d'office", OuverturePc("SO")),
            ("Ouvertures de PC de rétablissement professionnel", $"{PcHorsExterieur} AND PRCLL_DATE_RETABL_PROF IS NOT NULL AND PRCLL_DATE_RETABL_PROF {Periode}"), // :569-575
            ("Ouvertures de traitement de sortie de crise", $"{PcHorsExterieur} AND PRCLL_DATE_TRAITEMENT_SORTIE_CRISE IS NOT NULL AND PRCLL_DATE_TRAITEMENT_SORTIE_CRISE {Periode}"),
            ("Plans de traitement de sortie de crise", Plan("PLTSC")),
        ]));
        var tablePc = new TableauStatistique("Procédures collectives", IndicateurNombre, [.. pc]);

        // RequeteCloture (:606-614) : par libelle de code de cloture, la fin a 23:59:59.
        var (clotures, causeClotures) = await c.LireAsync("SELECT R_CODECLOTUR_LIBELLE_ECRAN, COUNT(PRCLL_ID_PRCLL) AS NB"
            + " FROM PROCEDURE_COLLECTIVE INNER JOIN CODE_CLOTURE ON PRCLL_CODE_CLOTURE = R_CODECLOTUR_VALEUR"
            + " WHERE (PRCLL_EXTERIEUR = 0) AND (PRCLL_DATE_CLOTURE BETWEEN @debut AND @finJour)"
            + " GROUP BY R_CODECLOTUR_LIBELLE_ECRAN ORDER BY R_CODECLOTUR_LIBELLE_ECRAN");
        var tableClotures = new TableauStatistique("Procédures collectives - Clôtures", ["Code de clôture", "Nombre"],
            clotures is null ? [] : [.. clotures.Take(Plafond).Select(l => new LigneStatistique(l.Texte("R_CODECLOTUR_LIBELLE_ECRAN"), [Entier(l.Entier("NB"))]))],
            causeClotures is null ? null : $"non calculé : {causeClotures}");

        var ip = new TableauStatistique("Statistiques IP", IndicateurNombre, [.. await LignesAsync(c,
        [
            ("Mises au rôle", $"SELECT count(distinct INSTN_NUM_ROLE) as NB FROM INSTANCE WITH (nolock) WHERE (INSTN_NUM_ROLE LIKE '%IP%') AND (INSTN_DATE_ENROLEMENT {Periode})"),
            ("Ordonnances IP", "SELECT count(distinct INSTN_NUM_ROLE) as NB FROM INSTANCE WITH (nolock) INNER JOIN DECISION_JUDICIAIRE WITH (nolock) "
                + $"ON INSTN_ID_INSTN = DCJDC_ID_INSTN WHERE (INSTN_NUM_ROLE LIKE '%IP%') AND (IsNull(DCJDC_DATE,DCJDC_DATE_PREVUE) {Periode})"),
            ("Ordonnances de fin d'instance", EvenementIp("'ICAUSEFIN'")),
            ("Exécutoires", EvenementIp("'IEXECU','IPEXECUT','IPEXECUTLR','IPEXECUTR'")),
            ("Oppositions", EvenementIp("'IOIP','IPOPOS'")),
        ])]);
        return [judiciaires, tablePc, tableClotures, ip];
    }

    // ============================================================ « Statistiques web » (MetierStats.cs:817-1729)

    private const string FromPcChiffres = " FROM PROCEDURE_COLLECTIVE WITH (nolock) left outer join chiffres_pc WITH (nolock) on CHFPC_ID_PRCLL = PRCLL_ID_PRCLL";

    // Les tranches, les memes pour toutes les familles (_RequeteWeb*CA / *SAL, identiques au texte pres -- relu le 27/11/2026).
    private static readonly string[] TranchesCa =
    [
        "(chfpc_chiffre_affaire is null or chfpc_chiffre_affaire = Cast('' as money) or (chfpc_chiffre_affaire  < Cast('153' as money)))",
        "(chfpc_chiffre_affaire >= Cast('153' as money)) AND (chfpc_chiffre_affaire < Cast('763' as money))",
        "(chfpc_chiffre_affaire >= Cast('763' as money)) AND (chfpc_chiffre_affaire < Cast('3049' as money))",
        "(chfpc_chiffre_affaire >= Cast('3049' as money))",
    ];

    private static readonly string[] TranchesSalaries =
    [
        "(chfpc_salaries_ouverture is null or chfpc_salaries_ouverture = Cast('' as int) or (chfpc_salaries_ouverture  < Cast('0' as int)))",
        "(chfpc_salaries_ouverture >= Cast('1' as int)) AND (chfpc_salaries_ouverture <= Cast('10' as int))",
        "(chfpc_salaries_ouverture >= Cast('11' as int)) AND (chfpc_salaries_ouverture <= Cast('50' as int))",
        "(chfpc_salaries_ouverture >= Cast('51' as int)) AND (chfpc_salaries_ouverture <= Cast('100' as int))",
        "(chfpc_salaries_ouverture > Cast('100' as int))",
    ];

    /// <summary>Les colonnes des tranches : leurs bornes dans les requetes (les alias de RIG v2 : [NB 153-763]...).</summary>
    private static readonly string[] ColonnesTranches =
        ["CA < 153", "CA 153 à 763", "CA 763 à 3049", "CA ≥ 3049", "Salariés : 0 ou inconnu", "1 à 10", "11 à 50", "51 à 100", "Plus de 100"];

    // Les familles (_RequeteWebOuvRJ... :1046-1591), la clause commune de chacune, sans le « AND » final.
    private static IEnumerable<(string Famille, string Commun)> Familles(string? ljLjs)
    {
        const string sansLiji = "(PRCLL_CODE_SOLUTION IS NULL OR PRCLL_CODE_SOLUTION NOT LIKE 'LIJI%') AND (PRCLL_CODE_SOLUTION_2 IS NULL OR PRCLL_CODE_SOLUTION_2 NOT LIKE 'LIJI%')";
        static string Solution(string condition) => $"((PRCLL_DATE_SOLUTION {Periode} AND PRCLL_CODE_SOLUTION {condition}) OR (PRCLL_DATE_SOLUTION_2 {Periode} AND PRCLL_CODE_SOLUTION_2 {condition}))";
        yield return ("Ouvertures RJ", $"{FromPcChiffres} WHERE PRCLL_EXTERIEUR = 0 AND PRCLL_DATE_OUVERTURE {Periode} AND {sansLiji} AND PRCLL_DATE_SAUVEGARDE IS NULL");
        yield return ("Ouvertures LJ", $"{FromPcChiffres} WHERE (PRCLL_EXTERIEUR = 0) AND {Solution("LIKE 'LIJI%'")} AND PRCLL_DATE_SAUVEGARDE IS NULL");
        yield return ("Ouvertures sauvegardes", $"{FromPcChiffres} WHERE (PRCLL_EXTERIEUR = 0) AND (PRCLL_DATE_SAUVEGARDE {Periode})");
        yield return ("Transformations RJ en LJ", $"{FromPcChiffres} WHERE (PRCLL_EXTERIEUR = 0) AND {Solution("= 'LJ'")}");
        yield return ("Conversions LJ en LJS", ljLjs is null ? "" : $"{FromPcChiffres} INNER JOIN EVENEMENT_PROCEDURE ON EVNPR_ID_PRCLL = PRCLL_ID_PRCLL WHERE (PRCLL_EXTERIEUR = 0)"
            + $" AND (EVNPR_DATE {Periode}) AND (EVNPR_CODE_EVE_PRO IN ({ljLjs})) AND (EVNPR_SURVENU = 1)");
        yield return ("Plans de continuation", $"{FromPcChiffres} WHERE (PRCLL_EXTERIEUR = 0) AND {Solution("like 'CT%'")}");
        yield return ("Plans de cession", $"{FromPcChiffres} WHERE (PRCLL_EXTERIEUR = 0) AND {Solution("like 'CS%'")}");
        yield return ("Plans de sauvegarde", $"{FromPcChiffres} WHERE (PRCLL_EXTERIEUR = 0) AND {Solution("like 'PLSAU%'")}");
    }

    /// <summary>Les nombres d'une famille par tranche (CA puis salaries), en une requete par tranche ; la premiere cause sinon.</summary>
    private static async Task<(int[] Nombres, string? Cause)> TranchesAsync(Calcul c, string commun)
    {
        var nombres = new List<int>();
        foreach (var tranche in TranchesCa.Concat(TranchesSalaries))
        {
            var (lignes, cause) = await c.LireAsync($"SELECT count(distinct PRCLL_ID_PRCLL) as NB{commun} AND {tranche}");
            if (cause is not null) return ([], cause);
            nombres.Add(lignes!.Count == 0 ? 0 : lignes[0].Entier("NB"));
        }
        return ([.. nombres], null);
    }

    private static async Task<TableauStatistique[]> StatsWebAsync(Calcul c)
    {
        var ljLjs = c.Codes("STAT_CONV_LJ_LJS", "'1LJLS'");
        var lignes = new List<LigneStatistique>();
        var totauxSalaries = new Dictionary<string, int>();
        foreach (var (famille, commun) in Familles(ljLjs))
        {
            if (commun.Length == 0)
            {
                lignes.Add(new LigneStatistique(famille, [$"non calculé : {ReglageIllisible}"]));
                continue;
            }
            var (nombres, cause) = await TranchesAsync(c, commun);
            if (cause is not null) { lignes.Add(new LigneStatistique(famille, [$"non calculé : {cause}"])); continue; }
            // Total{X}SAL (:883-990) : la somme des tranches de salaries.
            var total = nombres.Skip(4).Sum();
            totauxSalaries[famille] = total;
            lignes.Add(new LigneStatistique(famille, [.. nombres.Select(Entier), Entier(total)]));
        }
        var web = new TableauStatistique("Ouvertures, transformations et plans", ["Famille", .. ColonnesTranches, "Total"], [.. lignes]);

        // Les totaux (:1018-1024) : ouvertures (RJ, LJ, sauvegardes), transformations et conversions, plans.
        string Somme(params string[] familles) => familles.All(totauxSalaries.ContainsKey) ? Entier(familles.Sum(f => totauxSalaries[f])) : "non calculé";
        var totaux = new TableauStatistique("Totaux", IndicateurNombre,
        [
            new("Total des ouvertures", [Somme("Ouvertures RJ", "Ouvertures LJ", "Ouvertures sauvegardes")]),
            new("Total des transformations et conversions", [Somme("Transformations RJ en LJ", "Conversions LJ en LJS")]),
            new("Total des plans", [Somme("Plans de continuation", "Plans de cession", "Plans de sauvegarde")]),
        ]);

        // Les clotures (_RecupDonneesWebClot, :1613-1729) : la liste des codes de la periode, puis leurs tranches.
        var (codes, causeCodes) = await c.LireAsync("SELECT COUNT(DISTINCT PROCEDURE_COLLECTIVE.PRCLL_ID_PRCLL) AS QUANTITE, CODE_CLOTURE.R_CODECLOTUR_LIBELLE_ECRAN AS LIBELLE,"
            + "CODE_CLOTURE.R_CODECLOTUR_VALEUR AS VALEUR FROM PROCEDURE_COLLECTIVE WITH (nolock) INNER JOIN"
            + " CODE_CLOTURE ON PROCEDURE_COLLECTIVE.PRCLL_CODE_CLOTURE = CODE_CLOTURE.R_CODECLOTUR_VALEUR"
            + $" WHERE (PROCEDURE_COLLECTIVE.PRCLL_EXTERIEUR = 0) AND (PRCLL_DATE_CLOTURE {Periode})"
            + " GROUP BY R_CODECLOTUR_LIBELLE_ECRAN, R_CODECLOTUR_VALEUR ORDER BY R_CODECLOTUR_LIBELLE_ECRAN"); // RIG v2 n'ordonne pas : par libelle
        TableauStatistique clotures;
        if (causeCodes is not null)
            clotures = new TableauStatistique("Clôtures", ["Code de clôture", .. ColonnesTranches, "Quantité"], [], $"non calculé : {causeCodes}");
        else
        {
            var lignesClotures = new List<LigneStatistique>();
            foreach (var code in codes!.Take(Plafond))
            {
                // Le code de cloture vient de la base : il passe en litteral apres controle (un mot), comme dans RIG v2 (:1679).
                var valeur = code.Texte("VALEUR");
                if (!MotDeCode().IsMatch(valeur)) { lignesClotures.Add(new LigneStatistique(code.Texte("LIBELLE"), [$"non calculé : code de clôture illisible ({valeur})"])); continue; }
                var (nombres, cause) = await TranchesAsync(c, $"{FromPcChiffres} WHERE (PRCLL_EXTERIEUR = 0) AND (PRCLL_DATE_CLOTURE {Periode}) AND (PRCLL_CODE_CLOTURE = '{valeur}')");
                lignesClotures.Add(cause is not null
                    ? new LigneStatistique(code.Texte("LIBELLE"), [$"non calculé : {cause}"])
                    : new LigneStatistique(code.Texte("LIBELLE"), [.. nombres.Select(Entier), Entier(code.Entier("QUANTITE"))]));
            }
            if (lignesClotures.Count > 0)
                lignesClotures.Add(new LigneStatistique("Total", [.. Enumerable.Repeat("", ColonnesTranches.Length), Entier(codes.Sum(x => x.Entier("QUANTITE")))]));
            clotures = new TableauStatistique("Clôtures", ["Code de clôture", .. ColonnesTranches, "Quantité"], [.. lignesClotures]);
        }
        return [web, totaux, clotures];
    }

    // ============================================================ « Répartitions des JC et OP » (MetierStats.cs:704-814)

    private static string Repartition(string type, string motif)
        => $"SELECT '{type}' as Type, INSTN_NATURE_AFFAIRE + ' - ' + R_NATUREAFFA_LIBELLE_ECRAN AS LIBELLE, COUNT(INSTN_NATURE_AFFAIRE) AS NB "
         + "FROM INSTANCE INNER JOIN DECISION_JUDICIAIRE ON INSTN_ID_INSTN = DCJDC_ID_INSTN "
         + "INNER JOIN NATURE_AFFAIRE ON INSTN_NATURE_AFFAIRE = R_NATUREAFFA_VALEUR WHERE "
         + $"(IsNull(DCJDC_DATE,DCJDC_DATE_PREVUE) {Periode}) AND (INSTANCE.INSTN_NUM_ROLE LIKE '{motif}') "
         + "GROUP BY INSTN_NATURE_AFFAIRE + ' - ' + R_NATUREAFFA_LIBELLE_ECRAN "
         + "ORDER BY Type, INSTN_NATURE_AFFAIRE + ' - ' + R_NATUREAFFA_LIBELLE_ECRAN";

    private static async Task<TableauStatistique[]> RepartitionsAsync(Calcul c)
    {
        async Task<TableauStatistique> Un(string titre, string type, string motif)
        {
            var (lignes, cause) = await c.LireAsync(Repartition(type, motif));
            if (cause is not null) return new TableauStatistique(titre, ["Nature d'affaire", "Nombre"], [], $"non calculé : {cause}");
            var lues = lignes!.Take(Plafond).Select(l => new LigneStatistique(l.Texte("LIBELLE"), [Entier(l.Entier("NB"))])).ToList();
            // TotalJC / TotalOP (:730-731).
            lues.Add(new LigneStatistique("Total", [Entier(lignes!.Sum(l => l.Entier("NB")))]));
            return new TableauStatistique(titre, ["Nature d'affaire", "Nombre"], [.. lues]);
        }
        return [await Un("Jugements de contentieux (JC)", "JC", "%JC%"), await Un("Ordonnances (OP)", "OP", "%OP%")];
    }

    // ============================================================ « Ouverture par activités » (MetierStats.Activites.cs)

    private static async Task<TableauStatistique[]> ActivitesAsync(Calcul c, bool parCa, string? niveauDemande)
    {
        // OPE_NIVEAU (OPE_NIVEAU.cs:17-27) : « 1 » a « 4 », « 1 » par defaut.
        var niveau = niveauDemande is "1" or "2" or "3" or "4" ? niveauDemande : "1";
        var titre = parCa ? $"Ouvertures par activités et chiffre d'affaires, niveau {niveau}" : $"Ouvertures par activités et nombre de salariés, niveau {niveau}";
        // _RequetteCommun (:194-216).
        var commun = "FROM ETABLISSEMENT_PC WITH (NOLOCK) RIGHT OUTER JOIN PROCEDURE_COLLECTIVE WITH (NOLOCK) ON ETBPC_ID_PRCLL = PRCLL_ID_PRCLL LEFT OUTER JOIN "
            + "CHIFFRES_PC WITH (NOLOCK) ON PRCLL_ID_PRCLL = CHFPC_ID_PRCLL LEFT OUTER JOIN SOUS_RUBRIQUE_NAF WITH (NOLOCK) INNER JOIN EQUIV_NAF WITH (NOLOCK) "
            + $"ON SRNAF_CODE = ISNULL(EQNAF_NIV_{niveau}, '') ON ETBPC_CODE_NAF = EQNAF_NIV_5 "
            + "WHERE (PRCLL_EXTERIEUR = 0) AND ("
            + $"(PRCLL_DATE_SAUVEGARDE {Periode} "
            + "OR ((PRCLL_DATE_SAUVEGARDE IS NULL) AND ("
            + $"(PRCLL_DATE_SOLUTION {Periode} AND PRCLL_CODE_SOLUTION LIKE 'LIJI%') OR "
            + $"(PRCLL_DATE_SOLUTION_2 {Periode} AND PRCLL_CODE_SOLUTION_2 LIKE 'LIJI%'))) "
            + $"OR ((PRCLL_DATE_OUVERTURE {Periode}) "
            + "AND (PRCLL_DATE_SAUVEGARDE IS NULL) "
            + "AND (PRCLL_CODE_SOLUTION IS NULL OR PRCLL_CODE_SOLUTION NOT LIKE 'LIJI%') "
            + "AND (PRCLL_CODE_SOLUTION_2 IS NULL OR PRCLL_CODE_SOLUTION_2 NOT LIKE 'LIJI%'))) "
            + "AND ETBPC_NUM_ORDRE = 1 ";
        // _RequeteCA (:291-327) et _RequeteSalaries (:149-192) : une requete par plage, reunies.
        string[] plages = parCa
            ? ["(chfpc_chiffre_affaire is null or chfpc_chiffre_affaire = Cast('' as money) or (chfpc_chiffre_affaire  < Cast('153' as money)))",
               "(chfpc_chiffre_affaire  >= Cast('153' as money)) AND (chfpc_chiffre_affaire < Cast('763' as money))",
               "(chfpc_chiffre_affaire  >= Cast('763' as money)) AND (chfpc_chiffre_affaire < Cast('3049' as money))",
               "(chfpc_chiffre_affaire  >= Cast('3049' as money))"]
            : ["(CHFPC_SALARIES_OUVERTURE IS NULL OR CHFPC_SALARIES_OUVERTURE = CAST('' AS int))",
               "(CHFPC_SALARIES_OUVERTURE >= CAST('1' AS int)) AND (CHFPC_SALARIES_OUVERTURE <= CAST('10' AS int))",
               "(CHFPC_SALARIES_OUVERTURE >= CAST('11' AS int)) AND (CHFPC_SALARIES_OUVERTURE <= CAST('50' AS int))",
               "(CHFPC_SALARIES_OUVERTURE >= CAST('51' AS int)) AND (CHFPC_SALARIES_OUVERTURE <= CAST('100' AS int))",
               "(CHFPC_SALARIES_OUVERTURE >= CAST('101' AS int))"];
        var requete = string.Join(" UNION ALL ", plages.Select((p, i) =>
            $"SELECT '{i + 1}' AS plage, ISNULL(EQNAF_NIV_{niveau}, '') AS NIV_31,COUNT(DISTINCT PRCLL_NUM_PC) AS [NB], "
            + $"ISNULL(SRNAF_LIBELLE,'Sans activité ou activité inconnue') AS libelle {commun}AND {p}) GROUP BY ISNULL(EQNAF_NIV_{niveau}, ''),srnaf_libelle"));
        string[] colonnes = parCa
            ? ["Code", "Activité", "CA < 153", "CA 153 à 763", "CA 763 à 3049", "CA ≥ 3049", "Total"]
            : ["Code", "Activité", "0 ou inconnu", "1 à 10", "11 à 50", "51 à 100", "Plus de 100", "Total"];
        var (lignes, cause) = await c.LireAsync(requete);
        if (cause is not null) return [new TableauStatistique(titre, colonnes, [], $"non calculé : {cause}")];
        // _RemplirTableauTmp (:122-147) : une ligne par code, son premier libelle ; triees par code (:118).
        var parCode = new SortedDictionary<string, (string Libelle, int[] Plages)>(StringComparer.Ordinal);
        foreach (var l in lignes!)
        {
            var code = l.Texte("NIV_31");
            if (!parCode.TryGetValue(code, out var a)) parCode[code] = a = (l.Texte("libelle"), new int[plages.Length]);
            a.Plages[int.Parse(l.Texte("plage"), CultureInfo.InvariantCulture) - 1] += l.Entier("NB");
        }
        var lues = parCode.Take(Plafond).Select(kv => new LigneStatistique(kv.Key, [kv.Value.Libelle, .. kv.Value.Plages.Select(Entier), Entier(kv.Value.Plages.Sum())])).ToList();
        // La rangee « TOTAL » (:43-60).
        if (lues.Count > 0)
            lues.Add(new LigneStatistique("TOTAL", ["", .. Enumerable.Range(0, plages.Length).Select(i => Entier(parCode.Values.Sum(v => v.Plages[i]))),
                Entier(parCode.Values.Sum(v => v.Plages.Sum()))]));
        return [new TableauStatistique(titre, colonnes, [.. lues])];
    }

    // ============================================================ « Statistiques semestrielles » (RapportStatMandataire.cs)

    // REQUETE_STATS (:15-56), « dbo. » retire des colonnes (les tables V3 n'ont pas ce schema), les dates en parametres.
    private const string RequeteMandats = """
        SELECT PROCEDURE_COLLECTIVE.PRCLL_ID_PRCLL, PROCEDURE_COLLECTIVE.PRCLL_NUM_PC, ENTREPRISE.ENTRP_SIREN,
                        PROCEDURE_COLLECTIVE.PRCLL_NOM_AFFAIRE, CHIFFRES_PC.CHFPC_CHIFFRE_AFFAIRE * 1000 AS ChiffreAffaire, CHIFFRES_PC.CHFPC_ACTIF_DECLARE,
                        CHIFFRES_PC.CHFPC_SALARIES_OUVERTURE,
        		case when (GERER_PC.GRRPC_FONCTION_PC IN ('LQJ','LIQ','RDC','MDJ')
        				AND (SELECT top 1 PETHT_PCETAT from PC_ETAT_HISTO where PETHT_ID_PRCLL = PRCLL_ID_PRCLL order by PETHT_DATE)
        							IN ('TSC')) then 'MJ'
        			 when (GERER_PC.GRRPC_FONCTION_PC IN ('LQJ','LIQ')
        				AND (SELECT top 1 PETHT_PCETAT from PC_ETAT_HISTO where PETHT_ID_PRCLL = PRCLL_ID_PRCLL order by PETHT_DATE)
        							IN ('RPSRJ', 'RJ', 'RP','SACC', 'SAUV' ,'SFA')) then 'MDJ'
        			 when (GERER_PC.GRRPC_FONCTION_PC IN ('LQJ','LIQ')
        				AND (SELECT top 1 PETHT_PCETAT from PC_ETAT_HISTO where PETHT_ID_PRCLL = PRCLL_ID_PRCLL order by PETHT_DATE)
        							NOT IN ('RPSRJ', 'RJ', 'RP','SACC', 'SAUV' ,'SFA', 'TSC')) then 'LIQ'
                     when GERER_PC.GRRPC_FONCTION_PC = 'RDC' then 'MDJ'
        			 else GERER_PC.GRRPC_FONCTION_PC end as Fonction,
        		MANDATAIRE_PC.MNDPC_ABREGE,
                case when MANDATAIRE_PC.MNDPC_CIVILITE in ('PM','ND') then '' else R_CIVILITE_ABREGE_EDIT end as Civilite,
                MANDATAIRE_PC.MNDPC_NOM, MANDATAIRE_PC.MNDPC_COMPLEMENT_NOM
        FROM   PROCEDURE_COLLECTIVE INNER JOIN
               GERER_PC ON PROCEDURE_COLLECTIVE.PRCLL_ID_PRCLL = GERER_PC.GRRPC_ID_PRCLL INNER JOIN
               MANDATAIRE_PC ON GERER_PC.GRRPC_ID_MNDPC = MANDATAIRE_PC.MNDPC_ID_MNDPC LEFT OUTER JOIN
               ADRESSE_BASE ON PROCEDURE_COLLECTIVE.PRCLL_ID_ADRBS = ADRESSE_BASE.ADRBS_ID_ADRBS LEFT OUTER JOIN
               TYPE_VOIE ON TYPE_VOIE.R_TYPEVOIE_VALEUR = ADRESSE_BASE.ADRBS_TYPE_VOIE INNER JOIN
               ENTREPRISE ON PROCEDURE_COLLECTIVE.PRCLL_ID_RPRTR = ENTREPRISE.ENTRP_ID_RPRTR LEFT OUTER JOIN
               CIVILITE ON MANDATAIRE_PC.MNDPC_CIVILITE = CIVILITE.R_CIVILITE_VALEUR LEFT OUTER JOIN
               CHIFFRES_PC ON CHIFFRES_PC.CHFPC_ID_PRCLL = PROCEDURE_COLLECTIVE.PRCLL_ID_PRCLL
        WHERE  (PROCEDURE_COLLECTIVE.PRCLL_EXTERIEUR = '0')
        		AND (((GERER_PC.GRRPC_FONCTION_PC = 'RDC' OR GERER_PC.GRRPC_FONCTION_PC = 'MDJ'
        						OR GERER_PC.GRRPC_FONCTION_PC = 'LQJ' OR GERER_PC.GRRPC_FONCTION_PC = 'LIQ'
        						OR GERER_PC.GRRPC_FONCTION_PC = 'MJ' OR GERER_PC.GRRPC_FONCTION_PC = 'COP')
        				AND (GRRPC_DATE_DEBUT IS NULL OR GRRPC_DATE_DEBUT = (SELECT top 1 PETHT_DATE from PC_ETAT_HISTO where PETHT_ID_PRCLL = PRCLL_ID_PRCLL order by PETHT_DATE))
        				)
        			OR ((GERER_PC.GRRPC_FONCTION_PC = 'ADJ')
        				AND (GRRPC_DATE_DEBUT IS NULL OR GRRPC_DATE_DEBUT = (SELECT top 1 PETHT_DATE from PC_ETAT_HISTO where PETHT_ID_PRCLL = PRCLL_ID_PRCLL order by PETHT_DATE)
        					OR (GRRPC_DATE_DEBUT >= @debut and GRRPC_DATE_DEBUT < @fin))
        			)
        		)
                AND (COALESCE ( PROCEDURE_COLLECTIVE.PRCLL_DATE_RETABL_PROF, PROCEDURE_COLLECTIVE.PRCLL_DATE_SAUVEGARDE, PROCEDURE_COLLECTIVE.PRCLL_DATE_TRAITEMENT_SORTIE_CRISE,
                                 PROCEDURE_COLLECTIVE.PRCLL_DATE_OUVERTURE) >= @debut)
        		AND (COALESCE ( PROCEDURE_COLLECTIVE.PRCLL_DATE_RETABL_PROF, PROCEDURE_COLLECTIVE.PRCLL_DATE_SAUVEGARDE, PROCEDURE_COLLECTIVE.PRCLL_DATE_TRAITEMENT_SORTIE_CRISE,
                                 PROCEDURE_COLLECTIVE.PRCLL_DATE_OUVERTURE) < @lendemain)
                AND (PROCEDURE_COLLECTIVE.PRCLL_ID_PRCLL_PC_JOINTE IS NULL)
        ORDER BY PROCEDURE_COLLECTIVE.PRCLL_NUM_PC
        """;

    private sealed record Mandat(string NumPc, string Siren, string NomAffaire, decimal ChiffreAffaire, int Salaries, string Fonction,
                                 string Abrege, string NomComplet, decimal Actif);

    private static async Task<StatistiquesLues> SemestriellesAsync(Calcul c, string volet)
    {
        string[] colonnesMandats = ["N° RG", "SIREN", "Débiteur", "Chiffre d'affaires", "Nb salariés", "Administrateur", "Mandataire judiciaire",
                                    "Liquidateur", "Commissaire-priseur", "Actif déclaré", "Mandataire de justice"];
        var (lignes, cause) = await c.LireAsync(RequeteMandats);
        if (cause is not null)
            return new StatistiquesLues(volet, [new TableauStatistique("Analyse des mandats", colonnesMandats, [], $"non calculé : {cause}")], NombrePc: "non calculé");
        var mandats = lignes!.Select(l => new Mandat(l.Texte("PRCLL_NUM_PC"), l.Texte("ENTRP_SIREN"), l.Texte("PRCLL_NOM_AFFAIRE"),
            l.Decimal("ChiffreAffaire"), l.Entier("CHFPC_SALARIES_OUVERTURE"), l.Texte("Fonction"), l.Texte("MNDPC_ABREGE"),
            // NomComplet (:131-137) : civilite, nom, complement, les blancs reduits.
            Regex.Replace($"{l.Texte("Civilite")} {l.Texte("MNDPC_NOM")} {l.Texte("MNDPC_COMPLEMENT_NOM")}".Trim(), @"\s+", " "),
            l.Decimal("CHFPC_ACTIF_DECLARE"))).ToList();

        string Montant(decimal d) => d.ToString("0.##", Fr);
        string Noms(IEnumerable<Mandat> g, string fonction) => string.Join(" et ", g.Where(x => x.Fonction == fonction).Select(x => x.NomComplet));
        // listMandataireRapport (:176-189) : une ligne par affaire et ses chiffres.
        var affaires = mandats.GroupBy(m => (m.ChiffreAffaire, m.NomAffaire, m.NumPc, m.Salaries, m.Siren, m.Actif))
            .Select(g => (Cle: g.Key, Adj: Noms(g, "ADJ"), Mdj: Noms(g, "MDJ"), Cop: Noms(g, "COP"), Liq: Noms(g, "LIQ"), Mj: Noms(g, "MJ"))).ToList();
        var tableMandats = new TableauStatistique("Analyse des mandats", colonnesMandats,
            [.. affaires.Take(Plafond).Select(a => new LigneStatistique(a.Cle.NumPc, [a.Cle.Siren, a.Cle.NomAffaire, Montant(a.Cle.ChiffreAffaire),
                Entier(a.Cle.Salaries), a.Adj, a.Mdj, a.Liq, a.Cop, a.Cle.Actif > 0m ? Montant(a.Cle.Actif) : "0", a.Mj]))]);

        // Les totaux par mandataire (:191-234), groupes par abrege, au nom du premier.
        TableauStatistique ParMandataire(string titre, Func<Mandat, bool> fonction, bool avecActif)
        {
            var groupes = mandats.Where(fonction).GroupBy(m => m.Abrege).ToList();
            string[] colonnes = avecActif ? ["Nom", "Nb affaires", "Nb salariés", "Chiffre d'affaires", "Actif déclaré"] : ["Nom", "Nb affaires", "Nb salariés", "Chiffre d'affaires"];
            var lues = groupes.Take(Plafond).Select(g => new LigneStatistique(g.First().NomComplet, avecActif
                ? [Entier(g.Count()), Entier(g.Sum(x => x.Salaries)), Montant(g.Sum(x => x.ChiffreAffaire)), Montant(g.Sum(x => x.Actif))]
                : [Entier(g.Count()), Entier(g.Sum(x => x.Salaries)), Montant(g.Sum(x => x.ChiffreAffaire))])).ToList();
            var tous = groupes.SelectMany(g => g).ToList();
            // Total* (:250-269) : les affaires, le chiffre d'affaires, les salaries (et l'actif).
            lues.Add(new LigneStatistique("Total", avecActif
                ? [Entier(tous.Count), Entier(tous.Sum(x => x.Salaries)), Montant(tous.Sum(x => x.ChiffreAffaire)), Montant(tous.Sum(x => x.Actif))]
                : [Entier(tous.Count), Entier(tous.Sum(x => x.Salaries)), Montant(tous.Sum(x => x.ChiffreAffaire))]));
            return new TableauStatistique(titre, colonnes, [.. lues]);
        }
        var sansAdj = affaires.Where(a => a.Adj.Length == 0).ToList();
        var sansCop = affaires.Where(a => a.Cop.Length == 0).ToList();
        var synthese = new TableauStatistique("Affaires sans administrateur ou sans commissaire-priseur", ["", "Nb affaires", "Chiffre d'affaires", "Nb salariés"],
        [
            new("Sans administrateur judiciaire", [Entier(sansAdj.Count), Montant(sansAdj.Sum(a => a.Cle.ChiffreAffaire)), Entier(sansAdj.Sum(a => a.Cle.Salaries))]),
            new("Sans commissaire-priseur", [Entier(sansCop.Count), Montant(sansCop.Sum(a => a.Cle.ChiffreAffaire)), Entier(sansCop.Sum(a => a.Cle.Salaries))]),
        ]);
        return new StatistiquesLues(volet,
        [
            tableMandats,
            ParMandataire("Administrateurs judiciaires", m => m.Fonction == "ADJ", false),
            ParMandataire("Mandataires judiciaires et liquidateurs", m => m.Fonction is "MDJ" or "LIQ", true),
            ParMandataire("Commissaires-priseurs", m => m.Fonction == "COP", false),
            ParMandataire("Mandataires de justice", m => m.Fonction == "MJ", true),
            synthese,
        ], NombrePc: Entier(mandats.Select(m => m.NumPc).Distinct().Count()));
    }
}
