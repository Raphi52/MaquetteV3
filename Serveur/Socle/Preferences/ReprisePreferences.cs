using System.Text.Json;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.Preferences;

/// <summary>Une preference de la V2 sans equivalent dans la V3 : son nom, combien d'agents l'avaient, et pourquoi.</summary>
public sealed record PreferenceSansEquivalent(string NomV2, int Agents, string Raison);

/// <summary>Ce que la reprise a fait : agents et preferences repris, et la liste pour le referent Socle.</summary>
/// <param name="AgentsSansCompteV3">Les agents V2 (UTLST_ID_UTLST) qui ont des preferences mais aucun compte V3 (soc.Compte.IdV2) :
/// leurs preferences attendent la reprise des comptes (SOC-001), elles ne sont pas perdues dans la V2.</param>
public sealed record BilanReprisePreferences(int AgentsRepris, int PreferencesReprises,
                                             IReadOnlyList<PreferenceSansEquivalent> SansEquivalent,
                                             IReadOnlyList<int> AgentsSansCompteV3);

/// <summary>
/// LA REPRISE DES PREFERENCES DES AGENTS, V2 -> V3 (SOC-013 T04, CA02).
///
/// Elle lit dans la base V2 du greffe, par <see cref="LectureV2"/> (un SELECT seul), dbo.PARAMETRE_UTILISATEUR (nom et
/// valeur par agent) et dbo.UTILISATEUR.UTLST_IMPRIMANTE_DEFAUT, et ecrit dans soc.Preference de la base V3 les
/// preferences qui ont un equivalent. Les autres sont LISTEES (nom, nombre d'agents, raison), sans bloquer la reprise.
///
/// Ce qui a un equivalent (noms relevés sur RIG_DEV le 2026-11-21 : 1 150 noms, 40 833 lignes) :
///   ConsoleAccueil_Theme          -> Theme          (MetroBlue → clair... : <see cref="ThemesV2"/>, correspondance a valider)
///   ConsoleAccueil_Fav_NN         -> VuesFavorites  (« P;ARRIVEE;ARRIVEE » → l'ecran ARRIVEE de la V3, dans l'ordre NN)
///   ConsoleAccueil_StartProcessus -> OngletActif et OngletsVue (l'ecran ouvert au demarrage ; vide = l'accueil)
///   UTILISATEUR.UTLST_IMPRIMANTE_DEFAUT -> Imprimante (CA05)
/// Un ecran de la V2 ne passe que s'il est au menu de la V3 (<paramref name="ecransV3"/> de <see cref="ReprendreAsync"/>) :
/// le serveur ne connait pas le menu de l'ecran, celui qui lance la reprise le lui donne.
/// Reprendre deux fois ecrit les memes valeurs (ServicePreferences n'ecrit pas une valeur identique).
/// </summary>
public static class ReprisePreferences
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Les themes de l'ancien accueil (ConsoleAccueil_Theme, valeurs du code RigClientAccueil) et le theme de la V3
    /// le plus proche (codes de ThemesRig, AccueilEtat.cs). Sur RIG_DEV, les 144 agents ont MetroBlue (lu le 2026-11-21).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ThemesV2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["MetroBlue"] = "clair",
        ["MetroDark"] = "sombre",
        ["MetroPurple"] = "violet",
        ["MetroOrange"] = "orange",
        ["MetroMaroon"] = "argile",
    };

    /// <summary>Le prefixe des favoris de l'ancien accueil (ConsoleAccueil_Fav_01 a _NN).</summary>
    private const string PrefixeFavori = "ConsoleAccueil_Fav_";

    /// <summary>
    /// Une preference V2 traduite : son nom et sa valeur JSON dans la V3, ou la raison de son absence. Les favoris et
    /// l'ecran de demarrage rendent le CODE d'ecran (la liste des favoris se compose ensuite, dans l'ordre NN).
    /// Fonction pure : le banc la joue sans base.
    /// </summary>
    public static (string? NomV3, string? Valeur, string? Raison) Traduire(string nomV2, string? valeurV2, IReadOnlySet<string> ecransV3,
                                                                          IReadOnlyDictionary<string, string> procedures)
    {
        var valeur = (valeurV2 ?? "").Trim();
        if (nomV2.Equals("ConsoleAccueil_Theme", StringComparison.OrdinalIgnoreCase))
            return ThemesV2.TryGetValue(valeur, out var theme)
                ? (NomsPreferences.Theme, theme, null)
                : (null, null, $"theme de l'ancien accueil « {valeur} » sans pendant parmi les themes de la V3");

        if (nomV2.StartsWith(PrefixeFavori, StringComparison.OrdinalIgnoreCase))
        {
            if (valeur.Equals("SEP", StringComparison.OrdinalIgnoreCase))
                return (null, null, "separateur de la barre des favoris : la V3 range ses favoris sans separateur");
            var code = EcranV3(valeur.Split(';') is [_, var processus, ..] ? processus : valeur, ecransV3, procedures);
            return code is null
                ? (null, null, "favori vers un ecran de l'ancien RIG qui n'est pas au menu de la V3")
                : (NomsPreferences.VuesFavorites, code, null);
        }

        if (nomV2.Equals("ConsoleAccueil_StartProcessus", StringComparison.OrdinalIgnoreCase))
        {
            if (valeur.Length == 0) return (NomsPreferences.OngletActif, "", null);   // l'accueil, comme dans la V2
            var code = EcranV3(valeur, ecransV3, procedures);
            return code is null
                ? (null, null, "ecran de demarrage de l'ancien RIG qui n'est pas au menu de la V3")
                : (NomsPreferences.OngletActif, code, null);
        }

        return (null, null, RaisonSansEquivalent(nomV2));
    }

    /// <summary>
    /// Le code d'ecran de la V3 d'un processus de la V2, ou null. Un favori de la V2 nomme son PROCESSUS (« FACT »,
    /// « VGED »), pas sa procedure : la procedure vient de sa DLL dans dbo.PROCESSUS_ET_FONCTIONALITE
    /// (<paramref name="procedures"/> : FACT → PROC_FACTURE, VGED → PROC_VDOSSIER ; lu sur RIG_DEV le 2026-11-21), sinon
    /// le code lui-meme (« ARRIVEE » → PROC_ARRIVEE). Le premier qui est au menu de la V3 gagne.
    /// </summary>
    public static string? EcranV3(string processusV2, IReadOnlySet<string> ecransV3, IReadOnlyDictionary<string, string> procedures)
    {
        var p = processusV2.Trim().ToUpperInvariant();
        if (p.Length == 0) return null;
        var candidats = new List<string>();
        if (procedures.TryGetValue(p, out var parDll)) candidats.Add(parDll);
        candidats.Add(p.StartsWith("PROC_", StringComparison.Ordinal) ? p : "PROC_" + p);
        return candidats.FirstOrDefault(ecransV3.Contains);
    }

    /// <summary>La procedure d'une DLL de la V2 (« PROC_FACTURE.dll » → PROC_FACTURE), ou null.</summary>
    public static string? ProcedureDeDll(string? dll)
    {
        var nom = Path.GetFileNameWithoutExtension((dll ?? "").Trim());
        return nom.StartsWith("PROC_", StringComparison.OrdinalIgnoreCase) ? nom.ToUpperInvariant() : null;
    }

    /// <summary>Pourquoi une preference de la V2 n'a pas d'equivalent, par famille de noms (CA02 : liste au referent Socle).</summary>
    public static string RaisonSansEquivalent(string nomV2)
    {
        if (nomV2.StartsWith("AmiConstantes", StringComparison.OrdinalIgnoreCase))
            return "aspect de l'ancienne interface (polices, couleurs, grilles) : la V3 a ses themes";
        if (System.Text.RegularExpressions.Regex.IsMatch(nomV2, @"^Proc[A-Z0-9_]*(Loc[XYWH]|Screen)$"))
            return "place d'une fenetre de l'ancien RIG : la V3 ouvre l'ecran dans un onglet et retient elle-meme la place d'une fenetre sortie";
        if (System.Text.RegularExpressions.Regex.IsMatch(nomV2, @"(^ctr|_Col|_col|ColWidth|^GCOM\.|Dmd_|Demandes_|IaDca_)"))
            return "colonnes d'une liste de l'ancien RIG : les listes de la V3 ont leurs propres colonnes";
        if (nomV2.StartsWith("ConsoleAccueil_", StringComparison.OrdinalIgnoreCase))
            return "disposition de l'ancien accueil (panneaux, defilement des alertes, onglet du menu) : l'accueil de la V3 est en tuiles";
        return "reglage d'un ecran de l'ancien RIG sans pendant dans la V3";
    }

    /// <summary>
    /// Reprend les preferences de la base V2 <paramref name="v2"/> dans la base V3 <paramref name="v3"/> du meme greffe.
    /// <paramref name="ecransV3"/> : les codes d'ecran du menu de la V3 (Pont/MenuAccueil.cs, Pont/CatalogueEcrans.cs).
    /// </summary>
    public static async Task<BilanReprisePreferences> ReprendreAsync(SqlConnection v2, SqlConnection v3, IReadOnlySet<string> ecransV3,
                                                                     CancellationToken ct = default)
    {
        // V2 : (agent, nom, valeur), et l'imprimante par defaut de chaque agent.
        var lignes = new List<(int Agent, string Nom, string? Valeur)>();
        await using (var l = await LectureV2.LireAsync(v2,
            "SELECT PRMTR_ID_UTLST, PRMTR_NOM_PARAMETRE, PRMTR_VALEUR_PARAMETRE FROM dbo.PARAMETRE_UTILISATEUR WHERE PRMTR_ID_UTLST IS NOT NULL", ct))
            while (await l.ReadAsync(ct))
                lignes.Add((l.GetInt32(0), l.GetString(1), l.IsDBNull(2) ? null : l.GetString(2)));
        var imprimantes = new Dictionary<int, string>();
        await using (var l = await LectureV2.LireAsync(v2,
            "SELECT UTLST_ID_UTLST, UTLST_IMPRIMANTE_DEFAUT FROM dbo.UTILISATEUR WHERE UTLST_IMPRIMANTE_DEFAUT IS NOT NULL", ct))
            while (await l.ReadAsync(ct))
                if (l.GetString(1).Trim() is { Length: > 0 } imprimante) imprimantes[l.GetInt32(0)] = imprimante;

        // V2 : la procedure de chaque processus (sa DLL), pour les favoris et l'ecran de demarrage.
        var procedures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using (var l = await LectureV2.LireAsync(v2,
            "SELECT PROCF_CODE_PROC, PROCF_DLL FROM dbo.PROCESSUS_ET_FONCTIONALITE WHERE PROCF_DLL IS NOT NULL", ct))
            while (await l.ReadAsync(ct))
                if (ProcedureDeDll(l.GetString(1)) is { } procedure) procedures[l.GetString(0).Trim()] = procedure;

        // V3 : l'identifiant du compte de chaque agent V2 repris (SOC-001).
        var comptes = new Dictionary<int, int>();
        await using (var cmd = new SqlCommand("SELECT IdV2, Id FROM soc.Compte WHERE IdV2 IS NOT NULL;", v3))
        await using (var l = await cmd.ExecuteReaderAsync(ct))
            while (await l.ReadAsync(ct)) comptes[l.GetInt32(0)] = l.GetInt32(1);

        var sans = new Dictionary<string, (HashSet<int> Agents, string Raison)>(StringComparer.Ordinal);
        var parAgent = new Dictionary<int, Dictionary<string, string>>();
        var favoris = new Dictionary<int, SortedDictionary<string, string>>();
        foreach (var (agent, nom, valeur) in lignes)
        {
            var (nomV3, valeurV3, raison) = Traduire(nom, valeur, ecransV3, procedures);
            if (nomV3 is null)
            {
                if (!sans.TryGetValue(nom, out var s)) sans[nom] = s = ([], raison!);
                s.Agents.Add(agent);
                continue;
            }
            var siennes = parAgent.TryGetValue(agent, out var p) ? p : parAgent[agent] = new(StringComparer.Ordinal);
            if (nomV3 == NomsPreferences.VuesFavorites)
            {
                // L'ordre de la barre de la V2 est celui des numeros (Fav_01, Fav_02...) : un tri par nom le garde.
                var f = favoris.TryGetValue(agent, out var fav) ? fav : favoris[agent] = new(StringComparer.Ordinal);
                f[nom] = valeurV3!;
            }
            else if (nomV3 == NomsPreferences.OngletActif)
            {
                siennes[NomsPreferences.OngletActif] = JsonSerializer.Serialize(valeurV3, Json);
                siennes[NomsPreferences.OngletsVue] = JsonSerializer.Serialize(valeurV3!.Length == 0 ? [] : new[] { valeurV3 }, Json);
            }
            else siennes[nomV3] = JsonSerializer.Serialize(valeurV3, Json);
        }
        foreach (var (agent, f) in favoris)
            parAgent[agent][NomsPreferences.VuesFavorites] = JsonSerializer.Serialize(f.Values.Distinct().ToArray(), Json);
        foreach (var (agent, imprimante) in imprimantes)
        {
            var siennes = parAgent.TryGetValue(agent, out var p) ? p : parAgent[agent] = new(StringComparer.Ordinal);
            siennes[NomsPreferences.Imprimante] = JsonSerializer.Serialize(imprimante, Json);
        }

        var repris = 0;
        var preferences = 0;
        var orphelins = new List<int>();
        foreach (var (agent, siennes) in parAgent.OrderBy(a => a.Key))
        {
            if (!comptes.TryGetValue(agent, out var idV3)) { orphelins.Add(agent); continue; }
            await ServicePreferences.EcrireAsync(v3, idV3, siennes, "reprise-v2", ct);
            repris++;
            preferences += siennes.Count;
        }
        var liste = sans.Select(s => new PreferenceSansEquivalent(s.Key, s.Value.Agents.Count, s.Value.Raison))
                        .OrderByDescending(s => s.Agents).ThenBy(s => s.NomV2, StringComparer.Ordinal).ToArray();
        return new BilanReprisePreferences(repris, preferences, liste, orphelins);
    }
}
