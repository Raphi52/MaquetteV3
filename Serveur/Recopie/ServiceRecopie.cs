// fix-ok: fichier NEUF du lot 9 retouche avant tout essai : (1) le catch du passage journalise dans son bloc (regle
// statique SocleSupervisionCheck, Avalement.Dans : un catch (Exception) sans Log... est une faute) ; (2) le debut d'un
// passage lu au serveur SQL et non DateTime.Now -- le SQL Server des bancs est a l'heure UTC (mesure 2026-11-21 : 08:52
// contre 09:52 au poste), Debut et Fin de rec.PassageRecopie venaient de deux horloges. (3) Relecture apres la suite verte :
// la boucle ouvrait toutes les 5 s la base V3 de CHAQUE greffe du catalogue, meme pas encore creee (BaseV3CreeeLe nul),
// et journalisait chaque passage en panne -- une erreur toutes les 5 s par greffe ; elle ne vise plus que les bases V3
// creees, et une meme panne n'est signalee qu'une fois par 10 minutes.
// fix-ok: refus du chef (21/11) mesure par RecopieDeuxSensCheck sur l'ancien code (REC-005 CA02/CA03 rouges) : la relance d'un
// changement mis de cote n'existait pas, et le bilan d'un passage ne disait ni les refus ni les mises de cote -- d'ou
// RelancerAsync / RelancerRemonteeAsync, Refusees / MisesDeCote / TableArret au journal des passages (rec.PassageRecopie).
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
                                  long? VersionV2, double? RetardSecondes, string? Erreur, int Refusees = 0, int MisesDeCote = 0);

/// <summary>
/// UN PASSAGE DE LA RECOPIE CONTINUE d'une base : d'abord la file V3 -> V2 (l'applicateur, qui detecte les fiches
/// modifiees des deux cotes et y fait gagner la V3), puis les changements V2 -> V3 (la remontee). Dans cet ordre, une
/// fiche changee des deux cotes entre deux passages est toujours vue par l'applicateur, qui sait ce que la V3 a change.
/// Un passage a la fois par base : un verrou applicatif de la base V3 (deux serveurs qui tourneraient ensemble ne
/// recopient pas deux fois). Chaque passage laisse sa ligne dans rec.PassageRecopie.
/// </summary>
public static class ServiceRecopie
{
    /// <summary>REC-005 CA02 (seuil propose) : au 3e essai refuse par la base d'en face, un changement est mis de cote.</summary>
    public const int EssaisAvantMiseDeCote = 3;

    /// <summary>Relance un changement V3 -> V2 mis de cote, une fois la cause corrigee (REC-005 CA03) : il repasse au passage
    /// suivant, puis ceux de la meme fiche qui attendaient derriere lui, dans l'ordre. Faux si la ligne n'est pas mise de cote.</summary>
    public static async Task<bool> RelancerAsync(string connexionV3, long ligne, string par, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(connexionV3);
        await cn.OpenAsync(ct);
        return await MisesDeCote.RelancerV3Async(cn, ligne, par, ct);
    }

    /// <summary>Relance une fiche de la V2 que la V3 refusait (REC-005 CA03) : elle remonte au passage suivant, de son etat
    /// courant dans la V2 (ses changements suivants compris). Faux si elle n'est pas mise de cote.</summary>
    public static async Task<bool> RelancerRemonteeAsync(string connexionV3, string baseV2, string tableV2, string idV2, string par, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(connexionV3);
        await cn.OpenAsync(ct);
        return await MisesDeCote.RelancerV2Async(cn, baseV2, tableV2, idV2, par, ct);
    }

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
        if ((a?.Erreur ?? r?.Erreur) is { } motif)
            log?.LogError("Recopie de {BaseV2} : passage arrete, les changements attendent le suivant : {Erreur}", baseV2, motif);

        var refusees = (a?.Refusees ?? 0) + (r?.Refusees ?? 0);
        var misesDeCote = (a?.MisesDeCote ?? 0) + (r?.MisesDeCote ?? 0);
        var tableArret = a?.TableArret ?? r?.TableArret;
        double? retard = null;
        try
        {
            // Le retard (REC-002 T04) : le plus vieux changement V3 qui attend un passage -- pas ceux qui attendent derriere un
            // changement mis de cote, qui a sa propre alerte (REC-005 CA05).
            var plusVieux = await ScalaireAsync(verrou, null, """
                SELECT CONVERT(nvarchar(30), DATEDIFF_BIG(millisecond, MIN(f.Le), SYSDATETIME())) FROM rec.FileRecopie AS f
                WHERE f.Etat = 'en attente' AND NOT EXISTS (SELECT 1 FROM rec.FileRecopie AS m WHERE m.TableV3 = f.TableV3 AND m.IdV3 = f.IdV3 AND m.Etat = 'mise de cote');
                """, ct);
            retard = plusVieux is null ? null : long.Parse(plusVieux) / 1000.0;
            await ExecuterAsync(verrou, null, """
                INSERT INTO rec.PassageRecopie (BaseV2, Debut, Fin, Appliquees, Remontees, Echos, Ecarts, VersionV2, Erreur, Refusees, MisesDeCote, TableArret)
                VALUES (@b, CONVERT(datetime2(3), @d, 126), SYSDATETIME(), @a, @r, @e, @x, @v, LEFT(@err, 2000), @ref, @cote, @arret);
                """, ct, ("@b", baseV2), ("@d", debut), ("@a", a?.Appliquees ?? 0), ("@r", r?.Remontees ?? 0), ("@e", r?.Echos ?? 0),
                ("@x", a?.Ecarts ?? 0), ("@v", (object?)r?.Version ?? DBNull.Value), ("@err", (object?)erreur ?? DBNull.Value),
                ("@ref", refusees), ("@cote", misesDeCote), ("@arret", (object?)tableArret ?? DBNull.Value));
        }
        finally
        {
            await ExecuterAsync(verrou, null, "EXEC sp_releaseapplock @Resource = @n, @LockOwner = 'Session';", CancellationToken.None, ("@n", "rigv3.recopie." + baseV2));
        }
        if (misesDeCote > 0)
            log?.LogError("Recopie de {BaseV2} : {MisesDeCote} changement(s) mis de cote apres {Seuil} essais refuses (les autres fiches continuent ; " +
                          "a corriger puis relancer, alerte d'exploitation « recopie-incident »)", baseV2, misesDeCote, EssaisAvantMiseDeCote);
        return new BilanPassage(baseV2, true, a?.Appliquees ?? 0, r?.Remontees ?? 0, r?.Echos ?? 0, r?.Laissees ?? 0, a?.Ecarts ?? 0, r?.Version, retard, erreur,
                                refusees, misesDeCote);
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
    private readonly ConcurrentDictionary<string, DateTime> signales = new(StringComparer.Ordinal);

    /// <summary>Une meme panne d'un greffe (base V2 coupee, retard) ne va au journal technique qu'une fois par 10 minutes :
    /// a un passage toutes les 5 s, elle le remplirait. Chaque passage garde son erreur dans rec.PassageRecopie.</summary>
    private bool ASignaler(string cle)
    {
        var maintenant = DateTime.UtcNow;
        if (signales.TryGetValue(cle, out var le) && maintenant - le < TimeSpan.FromMinutes(10)) return false;
        signales[cle] = maintenant;
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalle = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue("Recopie:IntervalleSecondes", IntervalleParDefaut)));
        var seuil = TimeSpan.FromMinutes(configuration.GetValue("Recopie:SeuilRetardMinutes", 5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Seuls les greffes dont la base V3 existe (SOC-008) : un greffe pas encore passe n'a rien a recopier.
                foreach (var g in (await catalogue.ListerAsync(stoppingToken)).Where(g => g.BaseV3CreeeLe is not null && !string.IsNullOrWhiteSpace(g.BaseV3)))
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
            // Le passage ne journalise pas lui-meme (log: null) : son erreur revient dans le bilan, signalee ici une fois par 10 min.
            var bilan = await ServiceRecopie.PasserAsync(connexionV3, baseV2, c.Fiches, log: null, ct: ct);
            if (bilan.Erreur is not null)
            {
                cache.TryRemove(code, out _);
                if (ASignaler($"{code}|passage|{bilan.Erreur}"))
                    log.LogError("Recopie du greffe {Greffe} ({BaseV2}) : passage arrete, les changements attendent le suivant : {Erreur}", code, baseV2, bilan.Erreur);
            }
            // Une mise de cote arrive une fois par changement : elle va au journal technique sans filtre.
            if (bilan.MisesDeCote > 0)
                log.LogError("Recopie du greffe {Greffe} ({BaseV2}) : {MisesDeCote} changement(s) mis de cote apres {Seuil} essais refuses ; les autres fiches " +
                             "continuent, l'alerte d'exploitation « recopie-incident » nomme la table et l'erreur", code, baseV2, bilan.MisesDeCote,
                             ServiceRecopie.EssaisAvantMiseDeCote);
            if (bilan.RetardSecondes is { } r && r > seuil.TotalSeconds && ASignaler($"{code}|retard"))
                log.LogError("Recopie du greffe {Greffe} ({BaseV2}) en retard : le plus vieux changement V3 attend depuis {Minutes:0} min (seuil {Seuil} min)",
                             code, baseV2, r / 60, seuil.TotalMinutes);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            cache.TryRemove(code, out _);
            if (ASignaler($"{code}|exception|{e.Message}"))
                log.LogError(e, "Recopie du greffe {Greffe} ({BaseV2}) : passage impossible, nouvel essai au suivant", code, baseV2);
        }
    }
}
