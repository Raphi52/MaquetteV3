using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Supervision;

namespace RigV3.Serveur.Recopie;

/// <summary>
/// LES ALERTES DE LA RECOPIE (REC-005 CA05, REC-006 CA04), lues par le surveillant de la supervision a chaque passage
/// (<see cref="ISourceAlertes"/>) : meme canal, meme table d'alertes, fermees quand la cause disparait.
///
///  - « recopie-incident » : le dernier transfert d'une base est interrompu depuis plus de <see cref="CleDelai"/> minutes
///    (5 par defaut, CA05), ou « en cours » sans processus derriere (verrou libre) et sans lot ni evenement depuis ce
///    delai ; ou le dernier controle de nuit s'est arrete. L'alerte nomme la base, la table et l'erreur.
///  - « recopie-ecart » : le dernier controle de nuit a trouve au moins un ecart, un lien orphelin ou une ligne sans
///    correspondance (CA04) ; l'alerte nomme la base, les tables et les comptes, jamais une valeur.
/// La file des changements de la recopie continue (REC-002, lot 9) branchera ici ses changements mis de cote (REC-005
/// CA05, second cas) : sa table n'est pas encore dans ce depot.
/// </summary>
public sealed class AlertesRecopie(CatalogueGreffes catalogue, IConfiguration configuration) : ISourceAlertes
{
    public const string RegleIncident = "recopie-incident";
    public const string RegleEcart = "recopie-ecart";
    public const string Traitement = "recopie V2-V3";

    /// <summary>Minutes d'incident avant l'alerte (5 par defaut, seuil propose par REC-005 CA05).</summary>
    public const string CleDelai = "Recopie:DelaiIncidentMinutes";

    private readonly int delai = int.TryParse(configuration[CleDelai], NumberStyles.None, CultureInfo.InvariantCulture, out var m) && m > 0 ? m : 5;

    public IReadOnlyList<string> Regles => [RegleIncident, RegleEcart];

    public async Task<IReadOnlyList<CauseAlerte>> CausesAsync(DateTime maintenantUtc, CancellationToken ct)
    {
        var fiches = (await catalogue.ListerAsync(ct)).Where(f => f.BaseV3CreeeLe is not null).ToList();
        // Toutes les bases ENSEMBLE, comme le surveillant : un greffe muet ne retarde pas les autres. Une base qui ne repond
        // pas a deja son alerte « base injoignable » : elle n'en a pas une seconde ici.
        var parGreffe = await Task.WhenAll(fiches.Select(async f =>
        {
            try { return await CausesAsync(f, ct); }
            catch (BaseGreffeInjoignable) { return []; }
        }));
        return [.. parGreffe.SelectMany(c => c)];
    }

    private async Task<List<CauseAlerte>> CausesAsync(FicheGreffe f, CancellationToken ct)
    {
        var causes = new List<CauseAlerte>();
        await using var cn = await catalogue.OuvrirBaseV3Async(f.Code, ct);
        if (await Valeur(cn, "SELECT CASE WHEN OBJECT_ID(N'rec.TransfertLot', N'U') IS NULL THEN 0 ELSE 1 END;", ct) is 1)
        {
            await using var cmd = new SqlCommand($"""
                SELECT TOP (1) t.Id, t.BaseV2, t.Etat, t.Motif, t.PointDeReprise,
                       APPLOCK_TEST(N'public', N'rigv3-transfert:' + t.BaseV2, N'Exclusive', N'Session'),
                       DATEDIFF(second, CASE WHEN t.Etat = 'interrompu' THEN t.Fin
                                             ELSE (SELECT MAX(x.Le) FROM (SELECT t.Debut AS Le
                                                                          UNION ALL SELECT MAX(l.Le) FROM rec.TransfertLot l WHERE l.IdTransfert = t.Id
                                                                          UNION ALL SELECT MAX(e.Le) FROM rec.TransfertEvenement e WHERE e.IdTransfert = t.Id) AS x) END,
                                SYSDATETIME()),
                       (SELECT TOP (1) l.TableV2 FROM rec.TransfertLot l WHERE l.IdTransfert = t.Id
                        AND NOT EXISTS (SELECT 1 FROM rec.TransfertTable r WHERE r.IdTransfert = l.IdTransfert AND r.TableV2 = l.TableV2) ORDER BY l.Le DESC)
                FROM rec.Transfert t WHERE t.Etat <> 'refuse' ORDER BY t.Id DESC;
                """, cn);
            await using var l = await cmd.ExecuteReaderAsync(ct);
            if (await l.ReadAsync(ct))
            {
                var (id, baseV2, etat) = (l.GetInt32(0), l.GetString(1), l.GetString(2));
                var secondes = l.IsDBNull(6) ? 0 : l.GetInt32(6);
                var mort = etat == "en cours" && l.GetInt32(5) == 1;
                if ((etat == "interrompu" || mort) && secondes >= delai * 60)
                {
                    var table = l.IsDBNull(7) ? "aucune table entamee" : l.GetString(7);
                    var motif = l.IsDBNull(3) ? "arret brutal : le processus du transfert a disparu sans fermer son journal" : l.GetString(3);
                    causes.Add(new CauseAlerte($"{RegleIncident}|{f.Code}|{baseV2}", RegleIncident, f.Code, Traitement,
                        $"Transfert {id} de la base {baseV2} (greffe {f.Code}) interrompu depuis {secondes / 60} min, table {table} : {motif}. " +
                        $"Point de reprise : {(l.IsDBNull(4) ? "voir RigV3.Transfert rapport" : l.GetString(4))}. Relancer le transfert : il reprend de la."));
                }
            }
        }
        if (await Valeur(cn, "SELECT CASE WHEN OBJECT_ID(N'rec.Comparaison', N'U') IS NULL THEN 0 ELSE 1 END;", ct) is 1)
        {
            await using var cmd = new SqlCommand("""
                SELECT TOP (1) c.Id, c.BaseV2, c.Etat, c.Motif, CONVERT(nvarchar(16), c.Debut, 120), ISNULL(c.Ecarts, 0), ISNULL(c.Orphelins, 0), ISNULL(c.SansCorrespondance, 0),
                       (SELECT STRING_AGG(CONVERT(nvarchar(max), CONCAT(t.TableV2, N' (', t.Ecarts, N' ecarts, ',
                                          t.SansCorrespondanceV2 + t.SansCorrespondanceV3 + t.SansLigneV2 + t.SansLigneV3, N' sans correspondance)')), N', ')
                        FROM rec.ComparaisonTable t WHERE t.IdComparaison = c.Id
                        AND t.Ecarts + t.SansCorrespondanceV2 + t.SansCorrespondanceV3 + t.SansLigneV2 + t.SansLigneV3 > 0),
                       (SELECT STRING_AGG(CONVERT(nvarchar(max), x.TableV2 + N' : ' + CONVERT(nvarchar(20), x.n)), N', ')
                        FROM (SELECT TableV2, COUNT(*) AS n FROM rec.ComparaisonEcart WHERE IdComparaison = c.Id AND Genre = 'lien orphelin' GROUP BY TableV2) x)
                FROM rec.Comparaison c WHERE c.Etat <> 'en cours' ORDER BY c.Id DESC;
                """, cn);
            await using var l = await cmd.ExecuteReaderAsync(ct);
            if (await l.ReadAsync(ct))
            {
                var (id, baseV2, etat, le) = (l.GetInt32(0), l.GetString(1), l.GetString(2), l.GetString(4));
                if (etat == "arrete")
                    causes.Add(new CauseAlerte($"{RegleIncident}|{f.Code}|controle", RegleIncident, f.Code, Traitement,
                        $"Controle de nuit {id} des bases {baseV2} et V3 (greffe {f.Code}) arrete le {le} : {(l.IsDBNull(3) ? "sans motif" : l.GetString(3))}"));
                else if (l.GetInt64(5) + l.GetInt64(6) + l.GetInt64(7) > 0)
                    causes.Add(new CauseAlerte($"{RegleEcart}|{f.Code}", RegleEcart, f.Code, Traitement,
                        $"Controle de nuit {id} du {le}, base {baseV2} (greffe {f.Code}) : {l.GetInt64(5)} ecart(s), {l.GetInt64(6)} lien(s) orphelin(s), " +
                        $"{l.GetInt64(7)} ligne(s) sans correspondance" + (l.IsDBNull(8) ? "" : $" — tables : {l.GetString(8)}") +
                        (l.IsDBNull(9) ? "" : $" — liens orphelins : {l.GetString(9)}") + $". Detail : rec.ComparaisonEcart, controle {id}."));
            }
        }
        return causes;
    }

    private static async Task<object?> Valeur(SqlConnection cn, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, cn);
        return await cmd.ExecuteScalarAsync(ct);
    }
}
