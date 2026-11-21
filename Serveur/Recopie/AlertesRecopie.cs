// fix-ok: refus du chef (21/11) : la mise de cote d'un changement de la recopie continue n'avait pas d'alerte (le lot 8 l'avait
// laissee « a brancher », RecopieRepriseCheck RESTE REC-005 CA05 second cas) ; CausesRecopieContinueAsync la porte, avec la panne
// et le retard de plus de 5 minutes, verifies par RecopieDeuxSensCheck (REC-005 CA05).
using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Recopie;
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
///  - « recopie-incident » encore, pour la recopie CONTINUE (REC-002, REC-003 ; lot 9) : un changement mis de cote apres
///    3 essais refuses, ou la recopie arretee (panne, ou plus aucun passage) depuis plus du delai
///    (<see cref="CausesRecopieContinueAsync"/>).
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
        if (await Valeur(cn, "SELECT CASE WHEN OBJECT_ID(N'rec.RemonteeEchec', N'U') IS NULL THEN 0 ELSE 1 END;", ct) is 1)
            causes.AddRange(await CausesRecopieContinueAsync(cn, f, ct));
        return causes;
    }

    /// <summary>
    /// La recopie CONTINUE d'une base (REC-002, REC-003 ; REC-005 CA05 sur sa file) : « un incident qui dure plus de 5 minutes
    /// ou met un changement de cote » nomme la base, la table et l'erreur.
    ///  - un changement MIS DE COTE (3 essais refuses), une alerte par sens et par table, tant qu'il n'est pas relance ;
    ///  - la recopie ARRETEE : les passages echouent (panne) depuis le delai, ou -- aucun passage ne reussit plus, la boucle
    ///    du serveur arretee -- le plus vieux changement de la V3 qui attend un passage a depasse le delai (REC-002 T04).
    /// </summary>
    private async Task<List<CauseAlerte>> CausesRecopieContinueAsync(SqlConnection cn, FicheGreffe f, CancellationToken ct)
    {
        var causes = new List<CauseAlerte>();
        if (await Texte(cn, "SELECT TOP (1) BaseV2 FROM rec.Remontee;", ct) is not { } baseV2) return causes;   // recopie pas en service
        var tables = ReglesRecopie.Decouvrir().ToDictionary(r => r.TableV3, r => r.TableV2, StringComparer.OrdinalIgnoreCase);
        string TableV2(string table, string sens) => sens != RoutesMisesDeCote.SensV3VersV2 ? table
            : tables.TryGetValue(table, out var t2) ? t2 : table[(table.IndexOf('.') + 1)..];

        await using (var cmd = new SqlCommand("""
            WITH m AS (
                SELECT 'V3 vers V2' AS Sens, f.TableV3 AS TableFiche, f.IdV3 AS Fiche, f.Id AS Ligne, f.Erreur, f.MiseDeCoteLe
                FROM rec.FileRecopie AS f WHERE f.Etat = 'mise de cote'
                UNION ALL
                SELECT 'V2 vers V3', r.TableV2, r.IdV2, NULL, r.Erreur, r.MiseDeCoteLe FROM rec.RemonteeEchec AS r WHERE r.Etat = 'mise de cote'),
            n AS (SELECT *, COUNT(*) OVER (PARTITION BY Sens, TableFiche) AS Nombre,
                         ROW_NUMBER() OVER (PARTITION BY Sens, TableFiche ORDER BY MiseDeCoteLe, Ligne) AS Rang FROM m)
            SELECT Sens, TableFiche, Nombre, Fiche, Ligne, Erreur, CONVERT(nvarchar(16), MiseDeCoteLe, 120) FROM n WHERE Rang = 1 ORDER BY Sens, TableFiche;
            """, cn))
        await using (var l = await cmd.ExecuteReaderAsync(ct))
            while (await l.ReadAsync(ct))
            {
                var (sens, nombre, fiche) = (l.GetString(0), l.GetInt32(2), l.GetString(3));
                var table = TableV2(l.GetString(1), sens);
                var ligne = l.IsDBNull(4) ? "" : $" (ligne {l.GetInt64(4)})";
                causes.Add(new CauseAlerte($"{RegleIncident}|{f.Code}|{baseV2}|cote|{sens}|{table}", RegleIncident, f.Code, Traitement,
                    $"Recopie {sens} de la base {baseV2} (greffe {f.Code}) : {nombre} changement(s) mis de cote apres {ServiceRecopie.EssaisAvantMiseDeCote} essais " +
                    $"refuses, table {table} ; le premier, fiche {fiche}{ligne}, le {(l.IsDBNull(6) ? "?" : l.GetString(6))} : {(l.IsDBNull(5) ? "sans message" : l.GetString(5))}. " +
                    $"Les autres fiches continuent ; corriger la cause puis le relancer (POST {RoutesMisesDeCote.Relancer}) : il repasse, puis ceux qui attendaient derriere lui."));
            }

        // La panne en cours : le dernier passage en erreur, depuis le premier passage en erreur qui suit le dernier reussi.
        await using (var cmd = new SqlCommand("""
            SELECT TOP (1) p.Erreur, p.TableArret,
                   DATEDIFF(second, (SELECT MIN(e.Debut) FROM rec.PassageRecopie AS e WHERE e.BaseV2 = p.BaseV2 AND e.Erreur IS NOT NULL
                                     AND e.Id > ISNULL((SELECT MAX(o.Id) FROM rec.PassageRecopie AS o WHERE o.BaseV2 = p.BaseV2 AND o.Erreur IS NULL), 0)), SYSDATETIME())
            FROM rec.PassageRecopie AS p WHERE p.BaseV2 = @b ORDER BY p.Id DESC;
            SELECT TOP (1) f.TableV3, f.IdV3, f.Id, f.Erreur, DATEDIFF(second, f.Le, SYSDATETIME()) FROM rec.FileRecopie AS f
            WHERE f.Etat = 'en attente' AND NOT EXISTS (SELECT 1 FROM rec.FileRecopie AS m WHERE m.TableV3 = f.TableV3 AND m.IdV3 = f.IdV3 AND m.Etat = 'mise de cote')
            ORDER BY f.Le, f.Id;
            SELECT CONVERT(nvarchar(16), MAX(Fin), 120) FROM rec.PassageRecopie WHERE BaseV2 = @b AND Erreur IS NULL;
            """, cn))
        {
            cmd.Parameters.AddWithValue("@b", baseV2);
            await using var l = await cmd.ExecuteReaderAsync(ct);
            (string Erreur, string? Table, int Secondes)? panne = null;
            if (await l.ReadAsync(ct) && !l.IsDBNull(0) && !l.IsDBNull(2))
                panne = (l.GetString(0), l.IsDBNull(1) ? null : l.GetString(1), l.GetInt32(2));
            await l.NextResultAsync(ct);
            (string Table, string Fiche, long Ligne, string? Erreur, int Secondes)? vieux = null;
            if (await l.ReadAsync(ct))
                vieux = (TableV2(l.GetString(0), RoutesMisesDeCote.SensV3VersV2), l.GetString(1), l.GetInt64(2), l.IsDBNull(3) ? null : l.GetString(3), l.GetInt32(4));
            await l.NextResultAsync(ct);
            var dernierReussi = await l.ReadAsync(ct) && !l.IsDBNull(0) ? l.GetString(0) : null;
            var cle = $"{RegleIncident}|{f.Code}|{baseV2}|continue";
            if (panne is { } p && p.Secondes >= delai * 60)
                causes.Add(new CauseAlerte(cle, RegleIncident, f.Code, Traitement,
                    $"Recopie de la base {baseV2} (greffe {f.Code}) arretee depuis {p.Secondes / 60} min, table {p.Table ?? vieux?.Table ?? "inconnue"} : {p.Erreur}. " +
                    "Les changements attendent dans la file ; ils repartent d'eux-memes quand la base repond."));
            else if (vieux is { } v && v.Secondes >= delai * 60)
                causes.Add(new CauseAlerte(cle, RegleIncident, f.Code, Traitement,
                    $"Recopie de la base {baseV2} (greffe {f.Code}) en retard : le plus vieux changement de la V3, table {v.Table}, fiche {v.Fiche} (ligne {v.Ligne}), " +
                    $"attend depuis {v.Secondes / 60} min ; dernier passage reussi : {dernierReussi ?? "aucun"}" + (v.Erreur is null ? "" : $" ; dernier motif : {v.Erreur}") +
                    ". La boucle de la recopie du serveur (« Recopie:Continue ») tourne-t-elle ?"));
        }
        return causes;
    }

    private static async Task<string?> Texte(SqlConnection cn, string sql, CancellationToken ct) => await Valeur(cn, sql, ct) as string;

    private static async Task<object?> Valeur(SqlConnection cn, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, cn);
        return await cmd.ExecuteScalarAsync(ct);
    }
}
