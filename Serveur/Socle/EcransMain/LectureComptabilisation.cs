using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Comptabilisation » (PROC_COMPTABILISATION, web/src/ecrans/EcranComptabilisation.tsx) : l'arbre des JOURNÉES à comptabiliser,
/// lu dans la base V3 du greffe (v2.FACTURE, v2.DEMANDE, v2.AFFAIRE, v2.ENCAISSEMENT, v2.VENTILATION, v2.MODE_PAIEMENT,
/// v2.ARTICLE_UTILISE recopiées ; le réglage dans soc.Reglage, reprise de PARAMETRAGE). LECTURE SEULE (LectureV2). Plan « RIG v3
/// 100 % fonctionnel », lot 13, 27/11/2026, sur la marche du lot 17.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_COMPTABILISATION.
///   - OPE_COMPTABILISATION.ChargerTreeView (:180-218) : l'agrégat des factures par jour ; s'il y a une facture sans demande,
///     le message d'ANOMALIE (_AfficherAnomalieFacturesSansDemande, :489-501, au plus 20 factures nommées) et l'arbre VIDE ;
///   - _GetDateCompta (:421-446) : les journées = les jours de création des factures non comptabilisées ayant une demande ET une
///     affaire, UNION les jours d'encaissement des encaissements non comptabilisés ventilés sur une affaire, du plus ancien au plus
///     récent ;
///   - _GetFacturesParJour (:454-483) : par jour, nombre, somme TTC, affaires en cours (AFFRE_ID_UTLST_EN_COURS &gt; 0), factures sans
///     demande (Classes/FactureJour) ;
///   - _GetFacturesSansDemande (:503-525) : le numéro de la facture et son affaire par ARTICLE_UTILISE ;
///   - _GetEncaissementRemis (:532-555) : les encaissements remis non comptabilisés par (remis, mode) : nombre, somme, date du dernier ;
///   - Encaissement.GetEncaissementsNonCompta(greffe, false) (RigMetier\Facture\Encaissement.cs:201-209) : les NON remis ;
///   - TreeViewJoursCompta.ChargerTreeView (TreeView\TreeViewJoursCompta.cs:226-353) : sous chaque journée, les factures, puis
///     les remis dont la date (du dernier encaissement) est ce jour, puis les non remis REG, PV, puis les autres modes non remis
///     un nœud par mode (ordre du code), avec « Attention des encaissements remis du même type sont déjà présents » quand un remis
///     du même mode est daté du même jour ; un encaissement sans mode est ignoré ; une journée sans nœud est retirée.
///   - _InitTreeview (:143-155) : EXPRESSO_FIGERFACTAUTO à « 0 » (« 1 » par défaut) affiche le libellé du paramètre.
/// Les quatre traitements (comptabiliser, remettre en banque, lettrer) ne sont PAS repris : l'écran les montre sans les jouer.
/// </summary>
public static class LectureComptabilisation
{
    /// <summary>Au plus autant de journées (le plafond des écrans faits main).</summary>
    public const int Plafond = 500;

    /// <summary>RIG v2 n'énumère qu'au plus autant de factures sans demande dans son message (MAX_AFFICHE, :491).</summary>
    public const int FacturesNommees = 20;

    /// <summary>La phrase de RIG v2 sur un mode non remis quand un remis du même mode est daté du même jour (:334).</summary>
    public const string RemisDejaPresents = "Attention des encaissements remis du même type sont déjà présents";

    private const string Journees = """
        SELECT j.Jour FROM (
            SELECT CAST(f.FACTU_DATE_CREATION AS date) AS Jour
            FROM v2.FACTURE AS f
            INNER JOIN v2.DEMANDE AS d ON d.DMND_NUM_DEMANDE = f.FACTU_NUM_DEMANDE
            INNER JOIN v2.AFFAIRE AS a ON a.AFFRE_ID_AFFRE = d.DMND_ID_AFFRE
            WHERE f.FACTU_DATE_COMPTABILISATION IS NULL
            UNION
            SELECT CAST(e.ENCMT_DATE_ENCAISSEMENT AS date)
            FROM v2.ENCAISSEMENT AS e
            INNER JOIN v2.VENTILATION AS v ON v.VENTI_ID_ENCMT = e.ENCMT_ID_ENCMT
            INNER JOIN v2.AFFAIRE AS a ON a.AFFRE_ID_AFFRE = v.VENTI_ID_AFFRE
            WHERE e.ENCMT_DATE_COMPTABILISATION IS NULL) AS j
        WHERE j.Jour IS NOT NULL
        ORDER BY j.Jour
        """;

    private const string FacturesParJour = """
        SELECT CAST(f.FACTU_DATE_CREATION AS date) AS Jour, COUNT(*) AS Nombre, SUM(f.FACTU_MONTANT_TTC) AS Ttc,
               SUM(CASE WHEN a.AFFRE_ID_UTLST_EN_COURS > 0 THEN 1 ELSE 0 END) AS EnCours,
               SUM(CASE WHEN d.DMND_NUM_DEMANDE IS NULL THEN 1 ELSE 0 END) AS SansDemande
        FROM v2.FACTURE AS f
        LEFT JOIN v2.DEMANDE AS d ON d.DMND_NUM_DEMANDE = f.FACTU_NUM_DEMANDE
        LEFT JOIN v2.AFFAIRE AS a ON a.AFFRE_ID_AFFRE = d.DMND_ID_AFFRE
        WHERE f.FACTU_DATE_COMPTABILISATION IS NULL
        GROUP BY CAST(f.FACTU_DATE_CREATION AS date)
        """;

    private const string FacturesSansDemande = """
        SELECT f.FACTU_NUMERO_FACTURE, ISNULL(MAX(au.ARTUT_ID_AFFRE), 0) AS IdAffaire
        FROM v2.FACTURE AS f
        LEFT JOIN v2.DEMANDE AS d ON d.DMND_NUM_DEMANDE = f.FACTU_NUM_DEMANDE
        LEFT JOIN v2.ARTICLE_UTILISE AS au ON au.ARTUT_NUMERO_FACTURE = f.FACTU_NUMERO_FACTURE
        WHERE f.FACTU_DATE_COMPTABILISATION IS NULL AND d.DMND_NUM_DEMANDE IS NULL
        GROUP BY f.FACTU_NUMERO_FACTURE
        ORDER BY f.FACTU_NUMERO_FACTURE
        """;

    // Remis (ENCMT_REMIS, par remis et mode, date du dernier encaissement) ou non remis (NULL, par jour et par mode) ; le libelle
    // et le type du mode par MODE_PAIEMENT. Un non remis sans mode est ignore, comme RIG v2 (:269).
    private const string Encaissements = """
        SELECT x.Remis, x.Mode, mp.R_MODEPAIEME_LIBELLE_ECRAN AS Libelle, mp.R_MODEPAIEME_TYPE AS TypeMode,
               COUNT(*) AS Nombre, SUM(x.Montant) AS Somme, CAST(MAX(x.Encaisse) AS date) AS Jour
        FROM (SELECT e.ENCMT_REMIS AS Remis, e.ENCMT_MODE_PAIEMENT AS Mode, e.ENCMT_MONTANT AS Montant, e.ENCMT_DATE_ENCAISSEMENT AS Encaisse,
                     CASE WHEN e.ENCMT_REMIS IS NULL THEN CAST(e.ENCMT_DATE_ENCAISSEMENT AS date) END AS JourNonRemis
              FROM v2.ENCAISSEMENT AS e
              WHERE e.ENCMT_DATE_COMPTABILISATION IS NULL AND (e.ENCMT_REMIS IS NOT NULL OR e.ENCMT_MODE_PAIEMENT IS NOT NULL)) AS x
        LEFT JOIN v2.MODE_PAIEMENT AS mp ON mp.R_MODEPAIEME_VALEUR = x.Mode
        GROUP BY x.Remis, x.Mode, mp.R_MODEPAIEME_LIBELLE_ECRAN, mp.R_MODEPAIEME_TYPE, x.JourNonRemis
        """;

    /// <summary>Les journées à comptabiliser, au plus <paramref name="plafond"/>.</summary>
    public static async Task<ComptabilisationLue> LireAsync(SqlConnection baseGreffe, CancellationToken ct, int plafond = Plafond)
    {
        var seuls = await ReglageAsync(baseGreffe, "EXPRESSO_FIGERFACTAUTO", ct) is "0";

        var factures = new Dictionary<DateTime, (int Nombre, decimal Ttc, int EnCours, int SansDemande)>();
        await using (var r = await LectureV2.LireAsync(baseGreffe, FacturesParJour, ct))
            while (await r.ReadAsync(ct))
                if (FormeLigne.DateDe(r, "Jour") is { } jour)
                    factures[jour] = (Entier(r, "Nombre"), Montant(r, "Ttc"), Entier(r, "EnCours"), Entier(r, "SansDemande"));

        // RIG v2 bloque l'arbre entier sur une facture non comptabilisee sans demande (:186-193), datee ou non.
        if (await AnomalieAsync(baseGreffe, ct) is { } anomalie)
            return new ComptabilisationLue([], 0, anomalie, seuls);

        var jours = new List<DateTime>();
        await using (var r = await LectureV2.LireAsync(baseGreffe, Journees, ct))
            while (await r.ReadAsync(ct))
                if (FormeLigne.DateDe(r, "Jour") is { } jour) jours.Add(jour);

        var remis = new List<(DateTime Jour, RemisComptaLu Noeud)>();
        var nonRemis = new List<(DateTime Jour, RemisComptaLu Noeud)>();
        await using (var r = await LectureV2.LireAsync(baseGreffe, Encaissements, ct))
            while (await r.ReadAsync(ct))
            {
                if (FormeLigne.DateDe(r, "Jour") is not { } jour) continue;
                var mode = FormeLigne.Texte(r, "Mode").Trim();
                var noeud = new RemisComptaLu(r["Remis"] is DBNull ? null : Entier(r, "Remis"), mode,
                    FormeLigne.Texte(r, "Libelle") is { Length: > 0 } l ? l : mode, FormeLigne.Texte(r, "TypeMode").Trim(),
                    Entier(r, "Nombre"), Montant(r, "Somme"), jour, "");
                (noeud.Remis is null ? nonRemis : remis).Add((jour, noeud));
            }

        var lues = new List<JourneeComptaLue>();
        foreach (var jour in jours)
        {
            var f = factures.GetValueOrDefault(jour);
            var siens = remis.Where(x => x.Jour == jour).Select(x => x.Noeud).OrderBy(x => x.Remis).ToList();
            // Non remis : REG, puis PV, puis les autres modes par code (TreeViewJoursCompta.cs:310-344).
            var nonRemisDuJour = nonRemis.Where(x => x.Jour == jour).Select(x => x.Noeud)
                .OrderBy(x => x.Mode == "REG" ? 0 : x.Mode == "PV" ? 1 : 2).ThenBy(x => x.Mode, StringComparer.Ordinal);
            foreach (var n in nonRemisDuJour)
                siens.Add(n.Mode is "REG" or "PV" || !siens.Any(x => x.Remis is not null && x.Mode == n.Mode) ? n : n with { Observation = RemisDejaPresents });
            // Une journee sans facture ni encaissement est retiree (:345-347).
            if (f.Nombre == 0 && siens.Count == 0) continue;
            lues.Add(new JourneeComptaLue(jour, f.Nombre, f.Ttc, f.EnCours, f.SansDemande, [.. siens]));
        }
        return new ComptabilisationLue([.. lues.Take(plafond)], lues.Count, null, seuls);
    }

    /// <summary>Le message de RIG v2 (_AfficherAnomalieFacturesSansDemande, :489-501) ; null sans facture sans demande.</summary>
    private static async Task<string?> AnomalieAsync(SqlConnection cn, CancellationToken ct)
    {
        var lignes = new List<string>();
        await using (var r = await LectureV2.LireAsync(cn, FacturesSansDemande, ct))
            while (await r.ReadAsync(ct))
            {
                var affaire = Entier(r, "IdAffaire");
                lignes.Add($"Facture N° {FormeLigne.Texte(r, "FACTU_NUMERO_FACTURE")} ({(affaire > 0 ? "affaire N° " + affaire : "affaire non identifiée")})");
            }
        if (lignes.Count == 0) return null;
        var detail = string.Join("\n", lignes.Take(FacturesNommees));
        if (lignes.Count > FacturesNommees) detail += $"\n… et {lignes.Count - FacturesNommees} autre(s).";
        return $"ANOMALIE : {lignes.Count} facture(s) ne sont rattachées à aucune demande. Merci de contacter Amitel.\n\n{detail}";
    }

    /// <summary>Un reglage du greffe (PARAMETRAGE de RIG v2, repris dans soc.Reglage) ; null s'il est absent.</summary>
    private static async Task<string?> ReglageAsync(SqlConnection cn, string code, CancellationToken ct)
    {
        await using var r = await LectureV2.LireAsync(cn, "SELECT Valeur FROM soc.Reglage WHERE Code = @c", ct, new SqlParameter("@c", code));
        return await r.ReadAsync(ct) && r["Valeur"] is string v ? v.Trim() : null;
    }

    private static int Entier(SqlDataReader r, string c) => r[c] is DBNull ? 0 : Convert.ToInt32(r[c], CultureInfo.InvariantCulture);

    private static decimal Montant(SqlDataReader r, string c) => r[c] is DBNull ? 0m : Convert.ToDecimal(r[c], CultureInfo.InvariantCulture);
}
