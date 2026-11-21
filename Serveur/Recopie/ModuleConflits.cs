// fix-ok: fichier NEUF du lot 9 ; l'age d'un ecart (rouge a 5 jours ouvres) se mesurait avec DateTime.Now du programme alors
// que DetecteLe vient de SYSDATETIME() -- le SQL Server des bancs est a l'heure UTC (mesure 2026-11-21 : 08:52 contre 09:52
// au poste) ; « maintenant » est desormais lu au serveur SQL, dans la meme requete.
// fix-ok: refus du chef (21/11) : un changement mis de cote n'avait ni liste ni relance (REC-005 CA03 rouge dans
// RecopieDeuxSensCheck), et l'ecran lisait les noms de colonnes (CLIEN_EMAIL) -- routes de l'exploitation et LibellesRecopie.
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Recopie;
using RigV3.Serveur.Socle.Comptes;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;
using RigV3.Serveur.Socle.Supervision;

namespace RigV3.Serveur.Recopie;

/// <summary>
/// LES FICHES EN CONFLIT (REC-004 CA02, CA03) : les ecarts de la recopie d'un greffe, pour l'agent designe -- celui qui a
/// la fonction <see cref="RoutesConflits.FonctionSuivi"/> dans la base de CE greffe (403 sinon : un agent non designe n'a
/// ni la liste, ni l'alerte). Marquer un ecart traite le sort de l'alerte ; il reste consultable avec l'agent et la date,
/// et le geste va au journal (SOC-016). Le module inscrit aussi la recopie continue du serveur (RecopieContinue) quand
/// « Recopie:Continue » est vrai.
/// </summary>
internal sealed class ModuleConflits : IModuleServeur
{
    /// <summary>Le nom de l'ecran, ecrit au journal des gestes.</summary>
    public const string NomEcran = "Fiches en conflit";

    public void Inscrire(IServiceCollection services, IConfiguration configuration)
    {
        if (configuration.GetValue(RecopieContinue.Reglage, false)) services.AddHostedService<RecopieContinue>();
    }

    public void Brancher(IEndpointRouteBuilder routes)
    {
        var groupe = routes.MapGroup(string.Empty).RequireAuthorization(PolitiquesSocle.AgentAdmis);
        BrancherMisesDeCote(routes);

        groupe.MapGet(RoutesConflits.Ecarts, async (string greffe, bool? traites, string? table, HttpContext http, CatalogueGreffes c,
                                                    ServiceDroits droits, BaseRig commune, CancellationToken ct) =>
        {
            if (await NonDesigne(http, droits, commune, greffe, ct) is { } refus) return refus;
            return await SurBaseDuGreffe(c, greffe, ct, async cn =>
                Results.Ok(await LireAsync(cn, traites ?? false, string.IsNullOrWhiteSpace(table) ? null : table.Trim(), null, ct)));
        });

        groupe.MapGet(RoutesConflits.Alerte, async (string greffe, HttpContext http, CatalogueGreffes c, ServiceDroits droits, BaseRig commune,
                                                    CancellationToken ct) =>
        {
            if (await NonDesigne(http, droits, commune, greffe, ct) is { } refus) return refus;
            return await SurBaseDuGreffe(c, greffe, ct, async cn => Results.Ok(await AlerteAsync(cn, ct)));
        });

        groupe.MapPost(RoutesConflits.Traiter, async (string greffe, long id, HttpContext http, CatalogueGreffes c, ServiceDroits droits,
                                                      BaseRig commune, CancellationToken ct) =>
        {
            if (await NonDesigne(http, droits, commune, greffe, ct) is { } refus) return refus;
            return await SurBaseDuGreffe(c, greffe, ct, async cn =>
            {
                var moi = await droits.AppelantAsync(http, commune, cn, ct);
                if (moi is null) return Results.Json(new { motif = "Agent inconnu dans la base de ce greffe." }, statusCode: StatusCodes.Status403Forbidden);
                await using var tx = cn.BeginTransaction();
                string? fiche;
                await using (var cmd = new SqlCommand("""
                    UPDATE rec.Ecart SET TraiteLe = SYSDATETIME(), TraitePar = @par OUTPUT inserted.Fiche WHERE Id = @id AND TraiteLe IS NULL;
                    """, cn, tx))
                {
                    cmd.Parameters.AddWithValue("@par", moi.NomAffiche);
                    cmd.Parameters.AddWithValue("@id", id);
                    fiche = await cmd.ExecuteScalarAsync(ct) as string;
                }
                if (fiche is not null)
                    await JournalGestes.EcrireAsync(cn, tx, moi, NomEcran, "ecart-recopie", $"l'ecart {id} : {fiche}", id.ToString(), "a valide",
                                                    new { traite = false }, new { traite = true }, ct);
                await tx.CommitAsync(ct);
                // Deja traite : rien n'est reecrit, l'ecart est rendu tel qu'il est (le premier agent et sa date restent).
                var vue = await LireAsync(cn, null, null, id, ct);
                return vue.Ecarts.Length == 0 ? Results.NotFound(new { motif = $"L'ecart {id} n'existe pas dans le greffe {greffe}." }) : Results.Ok(vue.Ecarts[0]);
            });
        });
    }

    /// <summary>
    /// LES CHANGEMENTS MIS DE COTE (REC-005 CA02, CA03 sur la file continue) : reserves a l'exploitation d'Amitel (agent admis
    /// du greffe marque Amitel, comme la supervision). Elle lit la liste, corrige la cause, puis relance un changement : il
    /// repasse au passage suivant de la recopie, puis ceux qui attendaient derriere lui. Le geste va au journal (SOC-016).
    /// </summary>
    internal static void BrancherMisesDeCote(IEndpointRouteBuilder routes)
    {
        var exploitation = new[] { PolitiquesSocle.AgentAdmis, PolitiquesSupervision.ExploitationAmitel };
        routes.MapGet(RoutesMisesDeCote.Liste, async (string greffe, CatalogueGreffes c, CancellationToken ct) =>
                await SurBaseDuGreffe(c, greffe, ct, async cn => Results.Ok(await MisesDeCote.ListerAsync(cn, ct))))
            .RequireAuthorization(exploitation);

        routes.MapPost(RoutesMisesDeCote.Relancer, async (string greffe, DemandeRelanceRecopie d, HttpContext http, CatalogueGreffes c, ServiceDroits droits,
                                                          BaseRig commune, CancellationToken ct) =>
                await SurBaseDuGreffe(c, greffe, ct, async cn =>
                {
                    var moi = await droits.AppelantAsync(http, commune, cn, ct);
                    if (moi is null) return Results.Json(new { motif = "Agent inconnu dans la base de ce greffe." }, statusCode: StatusCodes.Status403Forbidden);
                    var (relance, quoi) = d.Sens switch
                    {
                        RoutesMisesDeCote.SensV3VersV2 when d.Ligne is { } ligne => (await MisesDeCote.RelancerV3Async(cn, ligne, moi.NomAffiche, ct), $"ligne {ligne}"),
                        RoutesMisesDeCote.SensV2VersV3 when d.TableV2 is { Length: > 0 } t && d.IdV2 is { Length: > 0 } id =>
                            (await MisesDeCote.RelancerV2Async(cn, (await BaseV2Async(cn, ct))!, t, id, moi.NomAffiche, ct), $"{t} {id}"),
                        _ => (false, (string?)null),
                    };
                    if (quoi is null)
                        return Results.BadRequest(new { motif = $"Sens « {d.Sens} » : « {RoutesMisesDeCote.SensV3VersV2} » demande la ligne, « {RoutesMisesDeCote.SensV2VersV3} » la table et l'identifiant V2." });
                    if (!relance) return Results.NotFound(new { motif = $"Aucun changement mis de cote pour {quoi} dans le greffe {greffe}." });
                    var vue = (await MisesDeCote.ListerAsync(cn, ct, d.Ligne, d.TableV2, d.IdV2))
                        .First(m => m.Sens == d.Sens && (d.Ligne is null ? m.TableV2 == d.TableV2 && m.Fiche == d.IdV2 : m.Ligne == d.Ligne));
                    await using (var tx = cn.BeginTransaction())
                    {
                        await JournalGestes.EcrireAsync(cn, tx, moi, "Recopie (exploitation)", "fiche-partagee", $"changement mis de cote {vue.TableV2} {vue.Fiche}",
                                                        vue.Fiche, "a modifie", new { etat = "mise de cote", sens = d.Sens }, new { etat = "relance", sens = d.Sens }, ct);
                        await tx.CommitAsync(ct);
                    }
                    return Results.Ok(vue);
                }))
            .RequireAuthorization(exploitation);
    }

    private static async Task<string?> BaseV2Async(SqlConnection cn, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SELECT TOP (1) BaseV2 FROM rec.Remontee;", cn);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    /// <summary>Les ecarts : non traites (<paramref name="traites"/> faux), traites (vrai) ou tous (null), le plus recent d'abord.</summary>
    internal static async Task<VueEcarts> LireAsync(SqlConnection cn, bool? traites, string? table, long? id, CancellationToken ct)
    {
        var ecarts = new List<EcartRecopie>();
        var champs = new Dictionary<long, List<ChampEcart>>();
        await using (var cmd = new SqlCommand("""
            SELECT TOP (500) Id, TableV2, IdV2, IdV3, Fiche, Cible, DetecteLe, DateV2, AuteurV2, DateV3, AuteurV3, TraiteLe, TraitePar
            FROM rec.Ecart
            WHERE (@traites IS NULL OR (@traites = 1 AND TraiteLe IS NOT NULL) OR (@traites = 0 AND TraiteLe IS NULL))
              AND (@table IS NULL OR TableV2 = @table) AND (@id IS NULL OR Id = @id)
            ORDER BY DetecteLe DESC, Id DESC;
            SELECT c.IdEcart, c.Champ, c.ValeurV2, c.ValeurV3 FROM rec.EcartChamp AS c JOIN rec.Ecart AS e ON e.Id = c.IdEcart
            WHERE (@traites IS NULL OR (@traites = 1 AND e.TraiteLe IS NOT NULL) OR (@traites = 0 AND e.TraiteLe IS NULL))
              AND (@table IS NULL OR e.TableV2 = @table) AND (@id IS NULL OR e.Id = @id)
            ORDER BY c.IdEcart, c.Champ;
            SELECT DISTINCT TableV2 FROM rec.Ecart ORDER BY TableV2;
            SELECT SYSDATETIME();
            """, cn))
        {
            cmd.Parameters.AddWithValue("@traites", traites is null ? DBNull.Value : traites.Value);
            cmd.Parameters.AddWithValue("@table", (object?)table ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", (object?)id ?? DBNull.Value);
            await using var l = await cmd.ExecuteReaderAsync(ct);
            var lus = new List<(long Id, object[] V)>();
            while (await l.ReadAsync(ct))
            {
                var v = new object[l.FieldCount];
                l.GetValues(v);
                lus.Add((l.GetInt64(0), v));
            }
            await l.NextResultAsync(ct);
            var tableDe = lus.ToDictionary(x => x.Id, x => (string)x.V[1]);
            while (await l.ReadAsync(ct))
            {
                var idEcart = l.GetInt64(0);
                if (!champs.TryGetValue(idEcart, out var liste)) champs[idEcart] = liste = [];
                var (tableEcart, champ) = (tableDe[idEcart], l.GetString(1));
                // Ce que l'agent lit : le titre du champ sur la fiche de la V2, jamais la colonne. Une valeur masquee (le mot
                // de passe d'un compte web) ne quitte pas le serveur.
                string? Valeur(int i) => l.IsDBNull(i) ? null : LibellesRecopie.Masque(tableEcart, champ) ? "(masqué)" : LigneJson.Lisible(l.GetString(i));
                liste.Add(new ChampEcart(champ, LibellesRecopie.Champ(tableEcart, champ), Valeur(2), Valeur(3)));
            }
            await l.NextResultAsync(ct);
            var tables = new List<TableEcarts>();
            while (await l.ReadAsync(ct)) tables.Add(new TableEcarts(l.GetString(0), LibellesRecopie.Table(l.GetString(0))));
            // L'age d'un ecart se mesure a l'horloge qui l'a date : celle du serveur SQL (DetecteLe = SYSDATETIME()).
            await l.NextResultAsync(ct);
            await l.ReadAsync(ct);
            var maintenant = l.GetDateTime(0);
            foreach (var (n, v) in lus)
            {
                DateTime? traiteLe = v[11] is DBNull ? null : (DateTime)v[11];
                ecarts.Add(new EcartRecopie(n, (string)v[1], LibellesRecopie.Table((string)v[1]), (string)v[2], (string)v[3], (string)v[4], v[5] as string, (DateTime)v[6], (DateTime)v[7],
                    v[8] as string, (DateTime)v[9], (string)v[10], [.. champs.GetValueOrDefault(n) ?? []], traiteLe, v[12] as string,
                    traiteLe is null && JoursOuvres.Entre((DateTime)v[6], maintenant) >= RoutesConflits.JoursOuvresAvantRouge));
            }
            return new VueEcarts([.. ecarts], [.. tables]);
        }
    }

    /// <summary>L'alerte de l'accueil : combien d'ecarts non traites, dont combien en rouge (CA02, CA03).</summary>
    internal static async Task<AlerteEcarts> AlerteAsync(SqlConnection cn, CancellationToken ct)
    {
        var dates = new List<DateTime>();
        DateTime maintenant;
        await using (var cmd = new SqlCommand("SELECT DetecteLe FROM rec.Ecart WHERE TraiteLe IS NULL; SELECT SYSDATETIME();", cn))
        await using (var l = await cmd.ExecuteReaderAsync(ct))
        {
            while (await l.ReadAsync(ct)) dates.Add(l.GetDateTime(0));
            await l.NextResultAsync(ct);
            await l.ReadAsync(ct);
            maintenant = l.GetDateTime(0);
        }
        return new AlerteEcarts(dates.Count, dates.Count(d => JoursOuvres.Entre(d, maintenant) >= RoutesConflits.JoursOuvresAvantRouge),
                                dates.Count == 0 ? null : dates.Min());
    }

    /// <summary>403 si l'agent n'a pas la fonction de suivi dans la base de CE greffe ; null s'il l'a.</summary>
    private static async Task<IResult?> NonDesigne(HttpContext http, ServiceDroits droits, BaseRig commune, string greffe, CancellationToken ct)
    {
        var moi = await droits.AppelantAsync(http, commune, ct);
        var droit = moi is null ? null : await droits.VerifierAsync(http, commune, moi, RoutesConflits.FonctionSuivi, ct);
        if (droit is { Autorise: true }) return null;
        return Results.Json(new { motif = $"Refuse : le compte {moi?.CompteWindows ?? http.User.Identity?.Name} n'a pas la fonction {RoutesConflits.FonctionSuivi} " +
                                          $"(suivre les fiches modifiees des deux cotes) dans le greffe {greffe}." },
                            statusCode: StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> SurBaseDuGreffe(CatalogueGreffes c, string greffe, CancellationToken ct, Func<SqlConnection, Task<IResult>> travail)
    {
        SqlConnection cn;
        try { cn = await c.OuvrirBaseV3Async(greffe, ct); }
        catch (GreffeInconnu e) { return ReponseGreffeNonOuvert.Pour(e); }
        catch (BaseGreffeInjoignable e) { return ReponseGreffeNonOuvert.Pour(e); }
        await using (cn) return await travail(cn);
    }
}

/// <summary>
/// Les jours ouvres (REC-004 CA03, seuil de l'alerte rouge) : du lundi au vendredi, hors les 11 jours feries legaux de
/// metropole (Code du travail, L3133-1), lundi de Paques, Ascension et lundi de Pentecote calcules sur la date de Paques.
/// Les jours feries propres a l'outre-mer (abolition de l'esclavage...) et a l'Alsace-Moselle n'y sont pas : a valider.
/// </summary>
public static class JoursOuvres
{
    /// <summary>Les jours ouvres ecoules depuis <paramref name="depuis"/> jusqu'a <paramref name="jusqua"/> (le jour de
    /// depart ne compte pas, celui d'arrivee oui).</summary>
    public static int Entre(DateTime depuis, DateTime jusqua)
    {
        var n = 0;
        for (var d = depuis.Date.AddDays(1); d <= jusqua.Date; d = d.AddDays(1))
            if (EstOuvre(d)) n++;
        return n;
    }

    public static bool EstOuvre(DateTime d) => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !EstFerie(d.Date);

    public static bool EstFerie(DateTime d)
    {
        if ((d.Month, d.Day) is (1, 1) or (5, 1) or (5, 8) or (7, 14) or (8, 15) or (11, 1) or (11, 11) or (12, 25)) return true;
        var paques = Paques(d.Year);
        return d == paques.AddDays(1) || d == paques.AddDays(39) || d == paques.AddDays(50);
    }

    /// <summary>Le dimanche de Paques (calendrier gregorien, algorithme de Meeus/Jones/Butcher).</summary>
    public static DateTime Paques(int annee)
    {
        int a = annee % 19, b = annee / 100, c = annee % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30, i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451;
        int mois = (h + l - 7 * m + 114) / 31, jour = (h + l - 7 * m + 114) % 31 + 1;
        return new DateTime(annee, mois, jour);
    }
}
