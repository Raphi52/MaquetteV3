using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Rédaction d'une décision » (PROC_REDA, web/src/ecrans/EcranRedaction.tsx) : les décisions judiciaires des instances que
/// RIG v2 rédige, lues dans la base V3 du greffe (v2.DECISION_JUDICIAIRE et ses tables recopiées). LECTURE SEULE (LectureV2) :
/// l'écran n'a aucun geste. Plan « RIG v3 100 % fonctionnel », lot 10, 27/11/2026, sur la marche du lot 17.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_REDA (fiche proc_reda.md, le « Détail OPE » fait foi).
///   - QUELLES DÉCISIONS : RIG v2 ouvre une instance des séries « Instance=JRCT, JRPC, JRREF » (PROC_REDA.InitParam, mode par
///     défaut ; « TypeEntite=PC » ne fait que nommer le champ « Num PC » de la recherche, FormRechercheInstance.cs:922-929) et
///     montre TOUTES ses décisions (Instance.ListeDecisionJudiciaire, PROC_REDA.cs:106-110). L'écran fait main n'a pas de
///     recherche d'instance : il montre les décisions de toutes ces instances (série = CODE_AFFAIRE.R_CODEAFFAIR_TYPE_NUM_ROLE,
///     comme LecteurInstanceJudiciaire.cs). Le mode « PR » (préventions, JRPR/JRMC) n'est pas repris.
///   - ORDRE (PROC_REDA.cs:106-110) : sans date d'abord, puis « date ou date prévue » décroissante (DecisionJudiciaire.cs:58-66 :
///     DCJDC_DATE, à défaut DCJDC_DATE_PREVUE), puis identifiant décroissant.
///   - LES CHAMPS (OPE_DECISION_JUDICIAIRE.cs:97-106, Link) : type (TYPE_DECISION), ressort (RESSORT), avant dire droit
///     (AVANT_DIRE_DROIT), contradictoire (CONTRADICTOIRE), exécution provisoire (EXECUTION_PROVISOIRE), sens général
///     (SENS_GENERAL) -- six LISTES (ULT_COMBO_*, Rig_Ult_Metier\COMBO\JUDICIAIRE), lues par leur libellé (le code s'il n'en a
///     pas) ; date, date prévue ; deux cases, débats publics et décision publique (Ult_CheckBox, Designer.cs:51-52).
///   - APPELS D'AFFAIRE (OPE_DECISION_JUDICIAIRE.cs:108-118) : « Mise en délibéré le » et « Prononcé le » portent le titre
///     AppelAffaire.TitreSimplifieREDA (AppelAffaire.cs:162-172) : date d'audience, heure, « - Chambre » et son code ;
///     « &lt;Aucune audience ni cabinet selectionné&gt; » pour un appel sans audience ; vide sans appel (RIG v2 écrit alors
///     « Aucun appel d'affaire délibéré / prononcé »).
///   - MESURE D'INSTRUCTION : RIG v2 montre l'opération « Mesure d'instruction » quand le type de décision porte une mesure liée
///     (R_TYPEDECISI_TYPE_MESURE_LIE, OPE_DECISION_JUDICIAIRE.cs:376-386) ; les mesures de la décision : MESURE.MSINS_ID_DCJDC
///     (Mesure.cs:554), ordre MSINS_NUM_ORDRE, titrées comme DecisionJudiciaire.cs:167-174 (« Expertise n° », ou en attente
///     de numérotation).
///   - DOCUMENTS : « Décision judiciaire liée à N documents numérisés en GED » compte VIDIGED_DJ.VGDDJ_ID_DCJDC
///     (OPE_DECISION_JUDICIAIRE.cs:122-125, VidigedDJ.cs:76-80).
/// </summary>
public static class LectureRedaction
{
    /// <summary>Au plus autant de décisions (le plafond des écrans faits main).</summary>
    public const int Plafond = 500;

    /// <summary>Les séries d'instance de la rédaction (PROC_REDA.InitParam, mode par défaut).</summary>
    internal const string Series = "('JRCT', 'JRPC', 'JRREF')";

    /// <summary>Le titre d'un appel sans audience (AppelAffaire.cs:164, au mot près).</summary>
    public const string AppelSansAudience = "<Aucune audience ni cabinet selectionné>";

    private const string Requete = $$"""
        SELECT TOP (@plafond) d.DCJDC_ID_DCJDC, i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT,
               ISNULL(td.R_TYPEDECISI_LIBELLE_ECRAN, ISNULL(d.DCJDC_TYPE_DECISION, '')) AS TypeDecision,
               ISNULL(sg.R_SENSGENERA_LIBELLE_ECRAN, ISNULL(d.DCJDC_SENS_GENERAL, '')) AS SensGeneral,
               ISNULL(rs.R_RESSORT_LIBELLE_ECRAN, ISNULL(d.DCJDC_RESSORT, '')) AS Ressort,
               ISNULL(ad.R_AVANTDIRED_LIBELLE_ECRAN, ISNULL(d.DCJDC_AVANT_DIRE_DROIT, '')) AS AvantDireDroit,
               ISNULL(co.R_CONTRADICT_LIBELLE_ECRAN, ISNULL(d.DCJDC_CONTRADICTOIRE, '')) AS Contradictoire,
               ISNULL(ep.R_EXECUTIONP_LIBELLE_ECRAN, ISNULL(d.DCJDC_EXECUTION_PROVISOIRE, '')) AS ExecutionProvisoire,
               d.DCJDC_DATE, d.DCJDC_DATE_PREVUE, d.DCJDC_DEBAT_PUBLIC, d.DCJDC_DECISION_PUBLIQUE, td.R_TYPEDECISI_TYPE_MESURE_LIE,
               d.DCJDC_ID_APPAF_DELIBERER, aud.AUDNC_ID_ADNC AS AudienceDelibere, aud.AUDNC_DATE AS DateDelibere, aud.AUDNC_HEURE AS HeureDelibere, aud.AUDNC_CHAMBRE AS ChambreDelibere,
               d.DCJDC_ID_APPAF_PRONONCER, aup.AUDNC_ID_ADNC AS AudiencePrononce, aup.AUDNC_DATE AS DatePrononce, aup.AUDNC_HEURE AS HeurePrononce, aup.AUDNC_CHAMBRE AS ChambrePrononce,
               (SELECT COUNT(*) FROM v2.VIDIGED_DJ AS v WHERE v.VGDDJ_ID_DCJDC = d.DCJDC_ID_DCJDC) AS Documents,
               COUNT(*) OVER () AS Total
        FROM v2.DECISION_JUDICIAIRE AS d
        JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = d.DCJDC_ID_INSTN
        JOIN v2.CODE_AFFAIRE AS ca ON ca.R_CODEAFFAIR_VALEUR = i.INSTN_CODE_AFFAIRE
        LEFT JOIN v2.TYPE_DECISION AS td ON td.R_TYPEDECISI_VALEUR = d.DCJDC_TYPE_DECISION
        LEFT JOIN v2.SENS_GENERAL AS sg ON sg.R_SENSGENERA_VALEUR = d.DCJDC_SENS_GENERAL
        LEFT JOIN v2.RESSORT AS rs ON rs.R_RESSORT_VALEUR = d.DCJDC_RESSORT
        LEFT JOIN v2.AVANT_DIRE_DROIT AS ad ON ad.R_AVANTDIRED_VALEUR = d.DCJDC_AVANT_DIRE_DROIT
        LEFT JOIN v2.CONTRADICTOIRE AS co ON co.R_CONTRADICT_VALEUR = d.DCJDC_CONTRADICTOIRE
        LEFT JOIN v2.EXECUTION_PROVISOIRE AS ep ON ep.R_EXECUTIONP_VALEUR = d.DCJDC_EXECUTION_PROVISOIRE
        LEFT JOIN v2.APPEL_AFFAIRE AS apd ON apd.APPAF_ID_APPAF = d.DCJDC_ID_APPAF_DELIBERER
        LEFT JOIN v2.AUDIENCE_CABINET AS aud ON aud.AUDNC_ID_ADNC = apd.APPAF_ID_ADNC
        LEFT JOIN v2.APPEL_AFFAIRE AS app ON app.APPAF_ID_APPAF = d.DCJDC_ID_APPAF_PRONONCER
        LEFT JOIN v2.AUDIENCE_CABINET AS aup ON aup.AUDNC_ID_ADNC = app.APPAF_ID_ADNC
        WHERE ca.R_CODEAFFAIR_TYPE_NUM_ROLE IN {{Series}}
        ORDER BY CASE WHEN COALESCE(d.DCJDC_DATE, d.DCJDC_DATE_PREVUE) IS NULL THEN 0 ELSE 1 END,
                 COALESCE(d.DCJDC_DATE, d.DCJDC_DATE_PREVUE) DESC, d.DCJDC_ID_DCJDC DESC
        """;

    /// <summary>Les décisions à rédiger, au plus <paramref name="plafond"/>, avec leurs mesures.</summary>
    public static async Task<DecisionsRedactionLues> LireAsync(SqlConnection baseGreffe, CancellationToken ct, int plafond = Plafond)
    {
        var lignes = new List<DecisionRedactionLue>();
        var total = 0;
        await using (var r = await LectureV2.LireAsync(baseGreffe, Requete, ct, new SqlParameter("@plafond", plafond)))
        {
            while (await r.ReadAsync(ct))
            {
                total = Convert.ToInt32(r["Total"], CultureInfo.InvariantCulture);
                lignes.Add(new DecisionRedactionLue(
                    Convert.ToInt32(r["DCJDC_ID_DCJDC"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "INSTN_NUM_ROLE").Trim(),
                    FormeLigne.Texte(r, "INSTN_NOM_COURANT"), FormeLigne.Texte(r, "TypeDecision"), FormeLigne.Texte(r, "SensGeneral"),
                    FormeLigne.Texte(r, "Ressort"), FormeLigne.DateDe(r, "DCJDC_DATE"), FormeLigne.DateDe(r, "DCJDC_DATE_PREVUE"),
                    TitreAppel(r, "DCJDC_ID_APPAF_DELIBERER", "AudienceDelibere", "DateDelibere", "HeureDelibere", "ChambreDelibere"),
                    TitreAppel(r, "DCJDC_ID_APPAF_PRONONCER", "AudiencePrononce", "DatePrononce", "HeurePrononce", "ChambrePrononce"),
                    FormeLigne.Texte(r, "AvantDireDroit"), FormeLigne.Texte(r, "Contradictoire"), FormeLigne.Texte(r, "ExecutionProvisoire"),
                    r["DCJDC_DEBAT_PUBLIC"] is true, r["DCJDC_DECISION_PUBLIQUE"] is true,
                    FormeLigne.Texte(r, "R_TYPEDECISI_TYPE_MESURE_LIE").Trim().Length > 0,
                    Convert.ToInt32(r["Documents"], CultureInfo.InvariantCulture), []));
            }
        }
        if (lignes.Count == 0) return new DecisionsRedactionLues([], total);

        // Les mesures des décisions lues (Mesure.cs:554). Les identifiants sont des entiers lus ci-dessus : écrits tels quels.
        var mesures = new Dictionary<int, List<string>>();
        var ids = string.Join(", ", lignes.Select(l => l.Id.ToString(CultureInfo.InvariantCulture)));
        await using (var r = await LectureV2.LireAsync(baseGreffe,
            $"SELECT m.MSINS_ID_DCJDC, m.MSINS_NUM_MESURE FROM v2.MESURE AS m WHERE m.MSINS_ID_DCJDC IN ({ids}) ORDER BY m.MSINS_NUM_ORDRE, m.MSINS_ID_MSINS", ct))
        {
            while (await r.ReadAsync(ct))
            {
                var dj = Convert.ToInt32(r["MSINS_ID_DCJDC"], CultureInfo.InvariantCulture);
                var numero = FormeLigne.Texte(r, "MSINS_NUM_MESURE").Trim();
                (mesures.TryGetValue(dj, out var liste) ? liste : mesures[dj] = []).Add(
                    numero.Length > 0 ? $"Expertise n°{numero}" : "Expertise en attente de numérotation...");
            }
        }
        return new DecisionsRedactionLues(
            [.. lignes.Select(l => mesures.TryGetValue(l.Id, out var m) ? l with { Mesures = [.. m] } : l)], total);
    }

    /// <summary>TitreSimplifieREDA (AppelAffaire.cs:162-172) ; vide sans appel.</summary>
    private static string TitreAppel(SqlDataReader r, string appel, string audience, string date, string heure, string chambre)
    {
        if (r[appel] is DBNull) return "";
        if (r[audience] is DBNull) return AppelSansAudience;
        var titre = $"{FormeLigne.Date(FormeLigne.DateDe(r, date))} {FormeLigne.Texte(r, heure)}".Trim();
        return FormeLigne.Texte(r, chambre) is { Length: > 0 } c ? $"{titre} - Chambre {c}" : titre;
    }
}
