using Microsoft.Data.SqlClient;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Passage;

/// <summary>Les comptes actifs d'un service de la V2, et le metier dont il releve.</summary>
public sealed record ComptageService(string Service, string NomService, string Metier, int Actifs);

/// <summary>Le comptage d'un greffe : par service, puis par metier. Des nombres, jamais un nom ni un compte.</summary>
public sealed record ComptageGreffe(string Greffe, string Base, IReadOnlyList<ComptageService> Services, int AmitelExclus, int Desactives)
{
    public int Total => Services.Sum(s => s.Actifs);
    public IReadOnlyList<FormationMetier> ParMetier => [.. Services.GroupBy(s => s.Metier).OrderBy(g => g.Key, StringComparer.Ordinal)
                                                                  .Select(g => new FormationMetier(g.Key, g.Sum(s => s.Actifs), 0))];
}

/// <summary>
/// LE NOMBRE D'AGENTS A FORMER (SOC-026 CA01, T01) : un comptage des comptes ACTIFS de chaque base V2, par service et
/// par metier, sans aucune donnee personnelle (la requete ne lit ni nom, ni login, ni mot de passe : COUNT par
/// service). Exclus : les comptes desactives (UTILISATEUR.UTLST_DESACTIVE) et ceux d'Amitel (UTLST_ISAMITEL, ou
/// service « AMT ») — ils ne sont pas des agents du greffe.
/// Metier d'un service : par son abrege, releve dans RIG_DEV le 2026-11-21 (5 services : RCS, JUD, INS, CDG, AMT).
/// Un abrege inconnu reste « a classer » : le greffe dit de quel metier il releve, l'outil ne devine pas.
/// La comptabilite n'est pas un service dans RIG_DEV : ses agents sont comptes dans leur service (ecart, a valider).
/// </summary>
public static class ComptageAgents
{
    public const string AClasser = "a classer";

    public static readonly IReadOnlyDictionary<string, string> MetierDuService = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["RCS"] = "RCS",
        ["JUD"] = "judiciaire",
        ["INS"] = "endettement",
        ["CDG"] = "greffiers (tous metiers)",
    };

    private const string Requete =
        "SELECT ISNULL(s.SRVC_ABREGE, '(sans service)'), ISNULL(s.SRVC_NOM, ''), " +
        "SUM(CASE WHEN ISNULL(u.UTLST_DESACTIVE, 0) = 0 AND ISNULL(u.UTLST_ISAMITEL, 0) = 0 AND ISNULL(s.SRVC_ABREGE, '') <> 'AMT' THEN 1 ELSE 0 END), " +
        "SUM(CASE WHEN ISNULL(u.UTLST_DESACTIVE, 0) = 0 AND (ISNULL(u.UTLST_ISAMITEL, 0) = 1 OR ISNULL(s.SRVC_ABREGE, '') = 'AMT') THEN 1 ELSE 0 END), " +
        "SUM(CASE WHEN ISNULL(u.UTLST_DESACTIVE, 0) = 1 THEN 1 ELSE 0 END) " +
        "FROM dbo.UTILISATEUR u LEFT JOIN dbo.SERVICE s ON s.SRVC_ID_SRVC = u.UTLST_ID_SRVC " +
        "GROUP BY s.SRVC_ABREGE, s.SRVC_NOM ORDER BY 1";

    /// <summary>Compte une base V2 ouverte en lecture (LectureV2 : un SELECT seul).</summary>
    public static async Task<ComptageGreffe> CompterAsync(SqlConnection v2, string greffe, CancellationToken ct = default)
    {
        var services = new List<ComptageService>();
        int amitel = 0, desactives = 0;
        await using (var l = await LectureV2.LireAsync(v2, Requete, ct))
            while (await l.ReadAsync(ct))
            {
                var abrege = l.GetString(0).Trim();
                var actifs = l.GetInt32(2);
                amitel += l.GetInt32(3);
                desactives += l.GetInt32(4);
                if (actifs > 0)
                    services.Add(new ComptageService(abrege, l.GetString(1).Trim(), MetierDuService.GetValueOrDefault(abrege, AClasser), actifs));
            }
        return new ComptageGreffe(greffe, v2.Database, services, amitel, desactives);
    }

    public static string Texte(IEnumerable<ComptageGreffe> comptages)
    {
        var s = new System.Text.StringBuilder();
        foreach (var g in comptages)
        {
            s.AppendLine($"Greffe {g.Greffe} ({g.Base}) : {g.Total} agent(s) a former ; exclus : {g.AmitelExclus} compte(s) Amitel actif(s), {g.Desactives} desactive(s)");
            foreach (var m in g.ParMetier) s.AppendLine($"  {m.Metier,-26} {m.AFormer,5}");
            foreach (var sv in g.Services) s.AppendLine($"    service {sv.Service,-16} {sv.Actifs,5}  -> {sv.Metier}");
        }
        return s.ToString();
    }

    /// <summary>Le squelette de formation.json : a former par metier, formes a 0 (l'equipe de formation les tient).</summary>
    public static PlanFormation Plan(IEnumerable<ComptageGreffe> comptages)
        => new([.. comptages.Select(g => new FormationGreffe(g.Greffe, g.ParMetier))]);
}
