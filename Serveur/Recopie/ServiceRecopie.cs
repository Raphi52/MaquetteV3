// fix-ok: fichier NEUF du lot 9 retouche avant tout essai : (1) le catch du passage journalise dans son bloc (regle
// statique SocleSupervisionCheck, Avalement.Dans : un catch (Exception) sans Log... est une faute) ; (2) le debut d'un
// passage lu au serveur SQL et non DateTime.Now -- le SQL Server des bancs est a l'heure UTC (mesure 2026-11-21 : 08:52
// contre 09:52 au poste), Debut et Fin de rec.PassageRecopie venaient de deux horloges.
using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.SqlClient;
using RigV3.Serveur.Socle.Greffes;
using static RigV3.Serveur.Recopie.EcrituresRecopie;

namespace RigV3.Serveur.Recopie;

/// <summary>Un passage de la recopie continue d'une base (REC-002, REC-003, REC-004).</summary>
/// <param name="EnService">La recopie est en service pour cette base (rec.Remontee) ; sinon rien n'est fait.</param>
/// <param name="RetardSecondes">L'age du plus vieux changement V3 pas encore recopie apres le passage (REC-002 T04) ; null si la file est vide.</param>
public sealed record BilanPassage(string BaseV2, bool EnService, int Appliquees, int Remontees, int Echos, int Laissees, int Ecarts,
                                  long? VersionV2, double? RetardSecondes, string? Erreur);

/// <summary>
/// UN PASSAGE DE LA RECOPIE CONTINUE d'une base : d'abord la file V3 -> V2 (l'applicateur, qui detecte les fiches
/// modifiees des deux cotes et y fait gagner la V3), puis les changements V2 -> V3 (la remontee). Dans cet ordre, une
/// fiche changee des deux cotes entre deux passages est toujours vue par l'applicateur, qui sait ce que la V3 a change.
/// Un passage a la fois par base : un verrou applicatif de la base V3 (deux serveurs qui tourneraient ensemble ne
/// recopient pas deux fois). Chaque passage laisse sa ligne dans rec.PassageRecopie.
/// </summary>
public static class ServiceRecopie
{
    public static async Task<BilanPassage> PasserAsync(string connexionV3, string baseV2, FichesRecopiees? fiches = null, int auPlus = 500,
                                                       ILogger? log = null, CancellationToken ct = default)
    {
        await using var verrou = new SqlConnection(connexionV3);
        await verrou.OpenAsync(ct);
        // Les dates de rec.* sont celles du serveur SQL (SYSDATETIME), jamais celles du programme.
        var debut = (await ScalaireAsync(verrou, null, "SELECT CONVERT(nvarchar(30), SYSDATETIME(), 126);", ct))!;
        var idTransfert = await ScalaireAsync(verrou, null, "SELECT CONVERT(nvarchar(20), IdTransfert) FROM rec.Remontee WHERE BaseV2 = @b;", ct, ("@b", baseV2));
        if (idTransfert is null) return new BilanPassage(baseV2, false, 0, 0, 0, 0, 0, null, null, null);
        await using (var cmd = new SqlCommand("DECLARE @r int; EXEC @r = sp_getapplock @Resource = @n, @LockMode = 'Exclusive', @LockOwner = 'Session', " +
                                              "@LockTimeout = 0; SELECT @r;", verrou))
        {
            cmd.Parameters.AddWithValue("@n", "rigv3.recopie." + baseV2);
            if ((int)(await cmd.ExecuteScalarAsync(ct))! < 0)
                return new BilanPassage(baseV2, true, 0, 0, 0, 0, 0, null, null, "un autre passage de la recopie de cette base est en cours");
        }

        BilanApplication? a = null;
        BilanRemontee? r = null;
        string? erreur;
        try
        {
            fiches ??= await FichesRecopiees.ChargerAsync(connexionV3, baseV2, null, ct);
            a = await Applicateur.AppliquerAsync(connexionV3, fiches, int.Parse(idTransfert), auPlus, ct);
            if (a.Erreur is null) r = await Remontee.RemonterAsync(connexionV3, fiches, ct);
            erreur = a.Erreur ?? r?.Erreur;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // La V3 n'est pas bloquee : le passage s'arrete, la file attend, le prochain passage reprend (REC-002 CA06).
            erreur = e.Message;
            log?.LogError(e, "Recopie de {BaseV2} : passage arrete sur une erreur, les changements attendent le suivant", baseV2);
        }
        if (erreur is not null && a?.Erreur is not null || r?.Erreur is not null)
            log?.LogError("Recopie de {BaseV2} : passage arrete, les changements attendent le suivant : {Erreur}", baseV2, erreur);

        double? retard = null;
        try
        {
            var plusVieux = await ScalaireAsync(verrou, null, "SELECT CONVERT(nvarchar(30), DATEDIFF_BIG(millisecond, MIN(Le), SYSDATETIME())) FROM rec.FileRecopie WHERE Etat = 'en attente';", ct);
            retard = plusVieux is null ? null : long.Parse(plusVieux) / 1000.0;
            await ExecuterAsync(verrou, null, """
                INSERT INTO rec.PassageRecopie (BaseV2, Debut, Fin, Appliquees, Remontees, Echos, Ecarts, VersionV2, Erreur)
                VALUES (@b, CONVERT(datetime2(3), @d, 126), SYSDATETIME(), @a, @r, @e, @x, @v, LEFT(@err, 2000));
                """, ct, ("@b", baseV2), ("@d", debut), ("@a", a?.Appliquees ?? 0), ("@r", r?.Remontees ?? 0), ("@e", r?.Echos ?? 0),
                ("@x", a?.Ecarts ?? 0), ("@v", (object?)r?.Version ?? DBNull.Value), ("@err", (object?)erreur ?? DBNull.Value));
        }
        finally
        {
            await ExecuterAsync(verrou, null, "EXEC sp_releaseapplock @Resource = @n, @LockOwner = 'Session';", CancellationToken.None, ("@n", "rigv3.recopie." + baseV2));
        }
        return new BilanPassage(baseV2, true, a?.Appliquees ?? 0, r?.Remontees ?? 0, r?.Echos ?? 0, r?.Laissees ?? 0, a?.Ecarts ?? 0, r?.Version, retard, erreur);
    }
}

/// <summary>
/// LA RECOPIE CONTINUE DU SERVEUR : toutes les <see cref="IntervalleParDefaut"/> secondes (« Recopie:IntervalleSecondes »),
/// un passage pour chaque greffe du catalogue dont la base V3 a la recopie en service. Un passage de quelques secondes
/// tient le delai propose d'une minute (REC-002 CA01, REC-003 CA01). Une base V2 muette n'arrete que son greffe : ses
/// changements attendent, les autres greffes passent (CA06). Un retard au-dela de « Recopie:SeuilRetardMinutes »
/// (5, propose, REC-002 T04) va au journal technique en erreur (SOC-017), que la supervision montre.
/// Inscrite par ModuleConflits quand « Recopie:Continue » est vrai : un banc qui monte le serveur ne la lance pas.
/// </summary>
public sealed class RecopieContinue(CatalogueGreffes catalogue, IConfiguration configuration, ILogger<RecopieContinue> log) : BackgroundService
{
    public const string Reglage = "Recopie:Continue";
    public const int IntervalleParDefaut = 5;
    private readonly ConcurrentDictionary<string, (FichesRecopiees Fiches, DateTime Le)> cache = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalle = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue("Recopie:IntervalleSecondes", IntervalleParDefaut)));
        var seuil = TimeSpan.FromMinutes(configuration.GetValue("Recopie:SeuilRetardMinutes", 5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var g in await catalogue.ListerAsync(stoppingToken))
                    await PasserGreffeAsync(g.Code, catalogue.ConnexionBase(g.Serveur, g.BaseV3), g.BaseV2, seuil, stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Recopie continue : le catalogue des greffes ne se lit pas, nouvel essai dans {Intervalle}", intervalle);
            }
            try { await Task.Delay(intervalle, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task PasserGreffeAsync(string code, string connexionV3, string baseV2, TimeSpan seuil, CancellationToken ct)
    {
        try
        {
            if (!cache.TryGetValue(code, out var c) || DateTime.UtcNow - c.Le > TimeSpan.FromMinutes(10))
            {
                await using (var cn = new SqlConnection(connexionV3))
                {
                    await cn.OpenAsync(ct);
                    if (await ScalaireAsync(cn, null, "SELECT CONVERT(nvarchar(1), 1) WHERE OBJECT_ID(N'rec.Remontee') IS NOT NULL AND EXISTS (SELECT 1 FROM rec.Remontee WHERE BaseV2 = @b);",
                                            ct, ("@b", baseV2)) is null)
                        return;   // recopie pas en service pour ce greffe
                }
                c = (await FichesRecopiees.ChargerAsync(connexionV3, baseV2, null, ct), DateTime.UtcNow);
                cache[code] = c;
            }
            var bilan = await ServiceRecopie.PasserAsync(connexionV3, baseV2, c.Fiches, log: log, ct: ct);
            if (bilan.Erreur is not null) cache.TryRemove(code, out _);
            if (bilan.RetardSecondes is { } r && r > seuil.TotalSeconds)
                log.LogError("Recopie du greffe {Greffe} ({BaseV2}) en retard : le plus vieux changement V3 attend depuis {Minutes:0} min (seuil {Seuil} min)",
                             code, baseV2, r / 60, seuil.TotalMinutes);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            cache.TryRemove(code, out _);
            log.LogError(e, "Recopie du greffe {Greffe} ({BaseV2}) : passage impossible, nouvel essai au suivant", code, baseV2);
        }
    }
}
