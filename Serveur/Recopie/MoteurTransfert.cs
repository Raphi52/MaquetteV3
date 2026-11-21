using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Recopie;

/// <summary>Une ligne du rapport de fin de nuit (REC-001 T04) : une table, ce qui en a ete lu, ecrit, rejete.</summary>
public sealed record LigneRapport(string TableV2, int Ordre, string Regle, string Story, string? TableV3, long? Lues, long? Ecrites,
                                  long? Rejetees, long? LiensCasses, long? Correspondances, long? Octets, long DureeMs, string? Detail);

/// <param name="Etat">fini ; arrete (un ecart ou une erreur pendant l'ecriture) ; refuse (avant toute ecriture de donnees).</param>
public sealed record RapportTransfert(int IdTransfert, string Etat, string? Motif, string BaseV2, string? Instantane, DateTime? InstantaneLe,
                                      long? VersionSuivi, long DureeMs, IReadOnlyList<LigneRapport> Tables, IReadOnlyList<string> Schema)
{
    /// <summary>Code de sortie de l'outil : 0 fini, 1 arrete, 2 refuse.</summary>
    public int CodeSortie => Etat switch { "fini" => 0, "refuse" => 2, _ => 1 };
}

/// <param name="ConnexionV2">La base V2 du greffe (Database=RIG_&lt;GREFFE&gt;) sur son serveur : le transfert y prend son
/// instantane et la lit en lecture seule.</param>
/// <param name="ConnexionV3">Sa base V3, a jour des migrations (REC-001 compris), sur la MEME instance (Q4).</param>
public sealed record OptionsTransfert(string ConnexionV2, string ConnexionV3)
{
    /// <summary>Les regles ; null = celles du serveur (<see cref="ReglesTransfert.Decouvrir()"/>).</summary>
    public IReadOnlyList<RegleTable>? Regles { get; init; }
    public IReadOnlyList<IRepriseTransfert>? Reprises { get; init; }
    /// <summary>Lignes par lot d'ecriture.</summary>
    public int TailleLot { get; init; } = 50_000;
    public Action<EvenementTransfert>? Suivre { get; init; }
    public ILogger? Journal { get; init; }
}

/// <summary>
/// LE MOTEUR DU TRANSFERT INITIAL D'UNE BASE V2 VERS SA BASE V3 (REC-001) — le « script de transfert des donnees ».
///
///  1. Journal ouvert (rec.Transfert). Refus si la base est deja transferee (la reprise apres incident est REC-005)
///     ou si la base V2 n'est pas sur l'instance de la base V3 (Q4 ; num.Reprendre la lit par son nom).
///  2. Instantane date de la base V2 (CA05) : tout est lu dans cet etat fige, sans bloquer la V2.
///  3. Catalogue REEL de la base (tables, colonnes, cles, declencheurs) compare aux regles (CA01, T03) : les tables et
///     colonnes absentes ou en plus sont imprimees ; une table attendue absente, une table sans regle, une table
///     « telle quelle » sans cle primaire ou d'un type non porte : REFUS, avant toute ecriture de donnees.
///  4. Les reprises de la phase 1 placees avant les tables (reglages, comptes et droits) : appelees, pas refaites.
///  5. Chaque table « telle quelle », dans l'ordre deduit des cles etrangeres (CopieTelleQuelle, une transaction par
///     table, controle de fin compris).
///  6. Les reprises placees apres les tables (compteurs : num.Reprendre lit la V2 vivante, au dernier moment).
///  7. Fini : totaux, duree, evenement 210 ; l'instantane est supprime.
/// Un ecart ou une erreur : la table en cours est annulee, rien de ce qui suit n'est fait, l'etat « arrete » et son
/// motif sont au journal, l'instantane est supprime (ARRET PROPRE). La base V2 n'est jamais ecrite (CA06).
/// Mode d'emploi : docs/reconstruction/recopie.md.
/// </summary>
public static class MoteurTransfert
{
    public static async Task<RapportTransfert> TransfererAsync(OptionsTransfert o, CancellationToken ct = default)
    {
        var chrono = Stopwatch.StartNew();
        var decouvertes = o.Regles is null || o.Reprises is null ? ReglesTransfert.Decouvrir() : default;
        var regles = o.Regles ?? decouvertes.Regles;
        var reprises = o.Reprises ?? decouvertes.Reprises;
        var v2 = new SqlConnectionStringBuilder(o.ConnexionV2);
        var baseV2 = v2.InitialCatalog;
        if (string.IsNullOrWhiteSpace(baseV2)) throw new ArgumentException("La chaine de la base V2 doit nommer sa base (Database=...).", nameof(o));

        await using (var v3 = new SqlConnection(o.ConnexionV3))
        {
            await v3.OpenAsync(ct);
            await using var cmd = new SqlCommand("SELECT OBJECT_ID(N'rec.Transfert');", v3);
            if (await cmd.ExecuteScalarAsync(ct) is DBNull)
                return new RapportTransfert(0, "refuse", "La base V3 n'a pas la migration REC-001_transfert : lancer Base\\RigV3.Migrations sur elle d'abord.",
                                            baseV2, null, null, null, chrono.ElapsedMilliseconds, [], []);
        }

        var journal = await JournalTransfert.OuvrirAsync(o.ConnexionV3, baseV2, v2.DataSource, o.Suivre, ct);
        var lignes = new List<LigneRapport>();
        var schema = new List<string>();
        Instantane? instantane = null;
        // Les totaux ne comptent que les tables portees une ligne pour une ligne (lues = ecrites + rejetees) : une table
        // traduite par une reprise (droits, compteurs) a sa preuve dans le comparateur de sa story.
        (long Lues, long Ecrites, long Rejetees, long Liens) Totaux()
        {
            var comptees = lignes.Where(l => l.Ecrites is not null).ToList();
            return (comptees.Sum(l => l.Lues ?? 0), comptees.Sum(l => l.Ecrites ?? 0), comptees.Sum(l => l.Rejetees ?? 0), comptees.Sum(l => l.LiensCasses ?? 0));
        }
        RapportTransfert Rapport(string etat, string? motif) => new(journal.Id, etat, motif, baseV2, instantane?.Nom, instantane?.Le,
                                                                    instantane?.VersionSuivi, chrono.ElapsedMilliseconds, lignes, schema);
        try
        {
            await journal.EcrireAsync("debut", $"Transfert {journal.Id} de la base V2 {baseV2} ({v2.DataSource}) : {regles.Count} regles, " +
                                               $"{reprises.Count} reprises ({string.Join(", ", reprises.Select(r => r.Nom))})", ct);
            var refus = new List<string>();
            await using (var v3 = new SqlConnection(o.ConnexionV3))
            {
                await v3.OpenAsync(ct);
                await using (var cmd = new SqlCommand(
                    "SELECT TOP (1) CONCAT(N'transfert ', Id, N' (', Etat, N', commence le ', CONVERT(nvarchar(19), Debut, 120), N')') FROM rec.Transfert " +
                    "WHERE BaseV2 = @b AND Id <> @id AND Etat IN ('en cours', 'fini', 'arrete') ORDER BY Id DESC;", v3))
                {
                    cmd.Parameters.AddWithValue("@b", baseV2);
                    cmd.Parameters.AddWithValue("@id", journal.Id);
                    if (await cmd.ExecuteScalarAsync(ct) is string deja)
                        refus.Add($"la base {baseV2} a deja un transfert dans cette base V3 ({deja}) : reprendre apres un incident est REC-005");
                }
                await using (var cmd = new SqlCommand("SELECT DB_ID(@b);", v3))
                {
                    cmd.Parameters.AddWithValue("@b", baseV2);
                    if (await cmd.ExecuteScalarAsync(ct) is DBNull)
                        refus.Add($"la base V2 {baseV2} n'est pas sur l'instance de la base V3 (Q4 : la V3 est posee a cote de la V2 ; num.Reprendre la lit par son nom)");
                }
            }
            if (refus.Count > 0) return await RefuserAsync(journal, refus, Rapport, chrono, ct);

            instantane = await Instantane.CreerAsync(o.ConnexionV2, ct);
            await journal.InstantaneAsync(instantane, ct);
            await journal.EcrireAsync("instantane", $"Etat fige de {baseV2} au {instantane.Le:yyyy-MM-dd HH:mm:ss.fff} : instantane {instantane.Nom}, " +
                (instantane.VersionSuivi is { } v ? $"version de suivi des changements {v} (la recopie continue, REC-003, repart de la)"
                                                  : "suivi des changements non active sur la base V2 (REC-003 T01)"), ct);

            await using var lecture = new SqlConnection(instantane.Connexion);
            await lecture.OpenAsync(ct);
            var catalogue = await CatalogueV2.LireAsync(lecture, ct);
            await journal.EcrireAsync("catalogue", $"{catalogue.Tables.Count} tables, {catalogue.Tables.Values.Sum(t => t.Colonnes.Count)} colonnes, " +
                $"{catalogue.NombreCles} cles etrangeres ({catalogue.Tables.Values.Sum(t => t.Cles.Count(k => k.Desactivee || k.NonVerifiee))} desactivees ou non verifiees), " +
                $"{catalogue.NombreDeclencheurs} declencheurs, {catalogue.Tables.Values.Sum(t => t.Lignes)} lignes", ct);

            // 3. Le schema reel contre la liste attendue (CA01) et les regles (T03).
            var parTable = regles.ToDictionary(r => r.Table, StringComparer.OrdinalIgnoreCase);
            foreach (var r in regles.OrderBy(r => r.Table, StringComparer.Ordinal))
            {
                if (!catalogue.Tables.TryGetValue(r.Table, out var t))
                {
                    schema.Add(r.Facultative ? $"table facultative absente : {r.Table} ({r.Libelle})" : $"table attendue absente : {r.Table} ({r.Libelle})");
                    if (!r.Facultative) refus.Add($"table attendue absente : {r.Table}");
                    continue;
                }
                foreach (var c in r.Colonnes.Where(c => !t.Colonnes.Any(x => string.Equals(x.Nom, c, StringComparison.OrdinalIgnoreCase))))
                    schema.Add($"colonne attendue absente : {r.Table}.{c}");
                foreach (var c in t.Colonnes.Where(x => !r.Colonnes.Contains(x.Nom, StringComparer.OrdinalIgnoreCase)))
                    schema.Add($"colonne en plus : {r.Table}.{c.Nom} ({c.TypeSql ?? c.TypeNom})");
                if (r.Sorte == SorteRegle.TelleQuelle && CopieTelleQuelle.Empechement(t) is { } empeche) refus.Add(empeche);
                if (r.Sorte == SorteRegle.Reprise && !reprises.Any(p => string.Equals(p.Nom, r.Reprise, StringComparison.OrdinalIgnoreCase)))
                    refus.Add($"{r.Table} : la reprise « {r.Reprise} » de sa regle n'existe pas");
            }
            foreach (var t in catalogue.Tables.Values.Where(t => !parTable.ContainsKey(t.Nom)).OrderBy(t => t.Nom, StringComparer.Ordinal))
            {
                schema.Add($"table en plus, sans regle : {t.Nom} ({t.Lignes} lignes)");
                refus.Add($"table sans regle : {t.Nom}");
            }
            foreach (var ligne in schema) await journal.EcrireAsync("schema", ligne, ct);

            var tellesQuelles = regles.Where(r => r.Sorte == SorteRegle.TelleQuelle && catalogue.Tables.ContainsKey(r.Table))
                                      .Select(r => catalogue.Tables[r.Table]).ToList();
            await using (var v3 = new SqlConnection(o.ConnexionV3))
            {
                await v3.OpenAsync(ct);
                foreach (var t in tellesQuelles)
                {
                    await using var cmd = new SqlCommand("SELECT OBJECT_ID(@n);", v3);
                    cmd.Parameters.AddWithValue("@n", CopieTelleQuelle.NomV3(t));
                    if (await cmd.ExecuteScalarAsync(ct) is not DBNull) refus.Add($"la table {CopieTelleQuelle.NomV3Texte(t)} existe deja dans la base V3");
                }
            }
            if (refus.Count > 0) return await RefuserAsync(journal, refus, Rapport, chrono, ct);

            var (ordre, cycles) = OrdreEcriture(tellesQuelles);
            await journal.EcrireAsync("ordre", "Ordre d'ecriture (cles etrangeres) : " + string.Join(", ", ordre.Select(t => t.Nom)) +
                (cycles.Count > 0 ? $" ; cycles de cles, ordre par nom : {string.Join(", ", cycles)}" : ""), ct);

            var contexte = new ContexteReprise(instantane.Connexion, o.ConnexionV3, baseV2, journal.Id);
            var reglesReprise = regles.Where(r => r.Sorte == SorteRegle.Reprise).ToList();
            foreach (var r in reprises.Where(r => !r.ApresLesTables))
                await ReprendreAsync(r, reglesReprise, catalogue, lecture, contexte, journal, lignes, ct);

            // 5. Les tables telles quelles.
            var avecRejets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var v3 = new SqlConnection(o.ConnexionV3))
            {
                await v3.OpenAsync(ct);
                for (var i = 0; i < ordre.Count; i++)
                {
                    var t = ordre[i];
                    await journal.EcrireAsync("table", $"{t.Nom} : copie telle quelle ({t.Lignes} lignes au catalogue)", ct);
                    var l = await CopieTelleQuelle.CopierAsync(lecture, v3, catalogue, t, parTable[t.Nom], i + 1, journal.Id, baseV2,
                                                               o.TailleLot, avecRejets, ct);
                    lignes.Add(l);
                    if (l.Rejetees > 0) avecRejets.Add(t.Nom);
                    await journal.EcrireAsync("table", $"{t.Nom} : {l.Lues} lues, {l.Ecrites} ecrites, {l.Rejetees} rejetees, " +
                                                       $"{l.LiensCasses} liens casses gardes, {l.DureeMs} ms — {l.Detail}", ct);
                }
            }

            foreach (var r in reprises.Where(r => r.ApresLesTables))
                await ReprendreAsync(r, reglesReprise, catalogue, lecture, contexte, journal, lignes, ct);

            var totaux = Totaux();
            await journal.EcrireAsync("fin", $"Transfert fini en {chrono.ElapsedMilliseconds} ms : {totaux.Lues} lignes lues, {totaux.Ecrites} ecrites, " +
                                             $"{totaux.Rejetees} rejetees, {totaux.Liens} liens casses gardes", ct);
            await journal.TerminerAsync("fini", null, chrono.ElapsedMilliseconds, totaux, baseV2, ct);
            return Rapport("fini", null);
        }
        catch (EcartTransfert e)
        {
            await ArreterAsync(journal, "Arret sur un ecart : " + e.Message, Totaux(), baseV2, chrono);
            return Rapport("arrete", "Arret sur un ecart : " + e.Message);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            o.Journal?.LogError(e, "Transfert {Id} de {BaseV2} arrete sur une erreur", journal.Id, baseV2);
            await ArreterAsync(journal, "Arret sur une erreur : " + e.Message, Totaux(), baseV2, chrono);
            return Rapport("arrete", "Arret sur une erreur : " + e.Message);
        }
        catch (OperationCanceledException)
        {
            await ArreterAsync(journal, "Transfert interrompu (annulation demandee)", Totaux(), baseV2, chrono);
            throw;
        }
        finally
        {
            if (instantane is not null) await instantane.DisposeAsync();
        }
    }

    private static async Task<RapportTransfert> RefuserAsync(JournalTransfert journal, List<string> refus, Func<string, string?, RapportTransfert> rapport,
                                                             Stopwatch chrono, CancellationToken ct)
    {
        var motif = "Refuse avant toute ecriture : " + string.Join(" ; ", refus);
        await journal.EcrireAsync("refus", motif, ct);
        await journal.TerminerAsync("refuse", motif, chrono.ElapsedMilliseconds, (0, 0, 0, 0), "", ct);
        return rapport("refuse", motif);
    }

    private static async Task ArreterAsync(JournalTransfert journal, string motif, (long, long, long, long) totaux, string baseV2, Stopwatch chrono)
    {
        await journal.EcrireAsync("arret", motif, CancellationToken.None);
        await journal.TerminerAsync("arrete", motif, chrono.ElapsedMilliseconds, totaux, baseV2, CancellationToken.None);
    }

    /// <summary>Appelle une reprise de la phase 1, puis compte et relie ce qu'elle porte une ligne pour une ligne (CA03).</summary>
    private static async Task ReprendreAsync(IRepriseTransfert r, List<RegleTable> reglesReprise, CatalogueV2 catalogue, SqlConnection lecture,
                                             ContexteReprise contexte, JournalTransfert journal, List<LigneRapport> lignes, CancellationToken ct)
    {
        var debut = DateTime.Now;
        var chrono = Stopwatch.StartNew();
        await journal.EcrireAsync("reprise", $"Reprise « {r.Nom} » ({r.Story}) : appel", ct);
        var bilan = await r.ExecuterAsync(contexte, ct);
        await journal.EcrireAsync("reprise", $"Reprise « {r.Nom} » : {(bilan.DejaFaite ? "deja faite dans cette base V3, non rejouee ; " : "")}{bilan.Detail}", ct);
        if (bilan.Ecarts.Count > 0)
            throw new EcartTransfert($"reprise « {r.Nom} » ({r.Story}) : {bilan.Ecarts.Count} ecart(s) : {string.Join(" ; ", bilan.Ecarts.Take(10))}");

        var portees = bilan.Tables.ToDictionary(p => p.TableV2, StringComparer.OrdinalIgnoreCase);
        foreach (var regle in reglesReprise.Where(g => string.Equals(g.Reprise, r.Nom, StringComparison.OrdinalIgnoreCase) && catalogue.Tables.ContainsKey(g.Table)))
        {
            var t = catalogue.Tables[regle.Table];
            await using var compte = new SqlCommand($"SELECT COUNT_BIG(*) FROM {t.NomSql}", lecture) { CommandTimeout = 0 };
            LectureV2.ExigerLecture(compte.CommandText);
            var lues = (long)(await compte.ExecuteScalarAsync(ct))!;
            LigneRapport ligne;
            if (portees.TryGetValue(t.Nom, out var p))
            {
                var ecrites = await CorrespondancesAsync(contexte, t.Nom, p, ct);
                if (ecrites != lues)
                    throw new EcartTransfert($"{t.Nom} : {lues} lignes lues dans la V2, {ecrites} portees par la reprise « {r.Nom} » dans {p.TableV3}");
                ligne = new LigneRapport(t.Nom, 0, "reprise", regle.Story, p.TableV3, lues, ecrites, 0, 0, ecrites, null, chrono.ElapsedMilliseconds, bilan.Detail);
            }
            else
                ligne = new LigneRapport(t.Nom, 0, "reprise", regle.Story, null, lues, null, null, null, null, null, chrono.ElapsedMilliseconds,
                                         $"traduite par la reprise « {r.Nom} » (pas une ligne pour une ligne ; preuve : le comparateur de sa story) — {bilan.Detail}");
            lignes.Add(ligne);
            await journal.TableAsync(ligne, debut, ct);
        }
    }

    private static async Task<long> CorrespondancesAsync(ContexteReprise c, string tableV2, TablePortee p, CancellationToken ct)
    {
        await using var cn = new SqlConnection(c.ConnexionV3);
        await cn.OpenAsync(ct);
        await using (var cmd = new SqlCommand(
            "INSERT INTO rec.Correspondance (BaseV2, TableV2, IdV2, TableV3, IdV3, IdTransfert) " +
            $"SELECT @b, @t, x.IdV2, @t3, x.IdV3, @id FROM ({p.SelectionV2V3}) AS x (IdV2, IdV3);", cn) { CommandTimeout = 0 })
        {
            cmd.Parameters.AddWithValue("@b", c.BaseV2);
            cmd.Parameters.AddWithValue("@t", tableV2);
            cmd.Parameters.AddWithValue("@t3", p.TableV3);
            cmd.Parameters.AddWithValue("@id", c.IdTransfert);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await using var compte = new SqlCommand("SELECT COUNT_BIG(*) FROM rec.Correspondance WHERE BaseV2 = @b AND TableV2 = @t AND IdTransfert = @id;", cn);
        compte.Parameters.AddWithValue("@b", c.BaseV2);
        compte.Parameters.AddWithValue("@t", tableV2);
        compte.Parameters.AddWithValue("@id", c.IdTransfert);
        return (long)(await compte.ExecuteScalarAsync(ct))!;
    }

    /// <summary>L'ordre d'ecriture : une table apres celles que ses cles etrangeres visent (le lien sur soi-meme ne compte
    /// pas) ; a egalite, par nom. Un cycle de cles entre tables : ses tables a la suite, par nom, et nommees.</summary>
    internal static (List<TableV2> Ordre, List<string> Cycles) OrdreEcriture(IReadOnlyList<TableV2> tables)
    {
        var noms = tables.ToDictionary(t => t.Nom, StringComparer.OrdinalIgnoreCase);
        var attend = tables.ToDictionary(t => t.Nom,
            t => new HashSet<string>(t.Cles.Select(k => k.TableVisee).Where(v => noms.ContainsKey(v) && !string.Equals(v, t.Nom, StringComparison.OrdinalIgnoreCase)),
                                     StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var ordre = new List<TableV2>();
        var cycles = new List<string>();
        while (attend.Count > 0)
        {
            var pretes = attend.Where(a => a.Value.Count == 0).Select(a => a.Key).OrderBy(n => n, StringComparer.Ordinal).ToList();
            if (pretes.Count == 0)
            {
                pretes = attend.Keys.OrderBy(n => n, StringComparer.Ordinal).Take(1).ToList();
                cycles.AddRange(pretes);
            }
            foreach (var n in pretes)
            {
                ordre.Add(noms[n]);
                attend.Remove(n);
                foreach (var a in attend.Values) a.Remove(n);
            }
        }
        return (ordre, cycles);
    }
}
