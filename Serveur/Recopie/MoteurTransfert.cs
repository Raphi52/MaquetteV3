using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Recopie;

/// <summary>Une ligne du rapport de fin de nuit (REC-001 T04) : une table, ce qui en a ete lu, ecrit, rejete.</summary>
public sealed record LigneRapport(string TableV2, int Ordre, string Regle, string Story, string? TableV3, long? Lues, long? Ecrites,
                                  long? Rejetees, long? LiensCasses, long? Correspondances, long? Octets, long DureeMs, string? Detail);

/// <param name="Etat">fini ; arrete (un ecart : il ne se reprend pas) ; refuse (avant toute ecriture de donnees) ;
/// interrompu (un incident technique : relance, il reprend au dernier lot enregistre, REC-005).</param>
public sealed record RapportTransfert(int IdTransfert, string Etat, string? Motif, string BaseV2, string? Instantane, DateTime? InstantaneLe,
                                      long? VersionSuivi, long DureeMs, IReadOnlyList<LigneRapport> Tables, IReadOnlyList<string> Schema)
{
    /// <summary>Code de sortie de l'outil : 0 fini, 1 arrete, 2 refuse, 4 interrompu (relancer : il reprend).</summary>
    public int CodeSortie => Etat switch { "fini" => 0, "refuse" => 2, "interrompu" => 4, _ => 1 };

    /// <summary>0 : premier passage ; n : la n-ieme reprise de ce transfert (REC-005 CA04).</summary>
    public int Passage { get; init; }

    /// <summary>Interrompu : ou il reprendra (table, lot, lignes, derniere cle) ; le rapport du matin le dit.</summary>
    public string? PointDeReprise { get; init; }

    /// <summary>Tables transferees par une regle PROVISOIRE (REC-008) : comptees, et refusees par le feu vert.</summary>
    public int Provisoires { get; init; }
}

/// <param name="ConnexionV2">La base V2 du greffe (Database=RIG_&lt;GREFFE&gt;) sur son serveur : le transfert y prend son
/// instantane et la lit en lecture seule.</param>
/// <param name="ConnexionV3">Sa base V3, a jour des migrations (REC-001 compris), sur la MEME instance (Q4).</param>
public sealed record OptionsTransfert(string ConnexionV2, string ConnexionV3)
{
    /// <summary>Les regles ; null = celles du serveur (<see cref="ReglesTransfert.Decouvrir()"/>).</summary>
    public IReadOnlyList<RegleTable>? Regles { get; init; }
    public IReadOnlyList<IRepriseTransfert>? Reprises { get; init; }
    /// <summary>Lignes par lot d'ecriture : un lot est une transaction et un point de reprise (REC-005).</summary>
    public int TailleLot { get; init; } = 50_000;
    public Action<EvenementTransfert>? Suivre { get; init; }
    public ILogger? Journal { get; init; }

    /// <summary>
    /// REPETITION seulement (REC-008) : une table du catalogue sans regle recoit une regle « telle quelle » PROVISOIRE
    /// au lieu d'arreter le transfert (T03). Marquee, comptee, refusee par le feu vert. Jamais par defaut.
    /// </summary>
    public bool ReglesProvisoires { get; init; }

    /// <summary>Appele apres chaque lot enregistre (rec.TransfertLot ecrit et valide).</summary>
    public CopieTelleQuelle.ApresLot? ApresLot { get; init; }
}

/// <summary>
/// LE MOTEUR DU TRANSFERT INITIAL D'UNE BASE V2 VERS SA BASE V3 (REC-001) — le « script de transfert des donnees ».
///
///  1. Journal ouvert (rec.Transfert), sous le verrou de la base (un seul transfert a la fois). Le dernier transfert de
///     la base « interrompu », ou « en cours » sans processus derriere (verrou libre) : il est REPRIS (REC-005 CA04),
///     meme identifiant, meme instantane. « fini » ou « arrete » : refus. Refus aussi si la base V2 n'est pas sur
///     l'instance de la base V3 (Q4 ; num.Reprendre la lit par son nom).
///  2. Instantane date de la base V2 (CA05) : tout est lu dans cet etat fige, sans bloquer la V2.
///  3. Catalogue REEL de la base (tables, colonnes, cles, declencheurs) compare aux regles (CA01, T03) : les tables et
///     colonnes absentes ou en plus sont imprimees ; une table attendue absente, une table sans regle, une table
///     « telle quelle » sans cle primaire ou d'un type non porte : REFUS, avant toute ecriture de donnees. En
///     repetition seulement (REC-008), une table sans regle recoit une regle PROVISOIRE, marquee et comptee.
///  4. Les reprises de la phase 1 placees avant les tables (reglages, comptes et droits) : appelees, pas refaites.
///  5. Chaque table « telle quelle », dans l'ordre deduit des cles etrangeres (CopieTelleQuelle : une transaction par
///     lot et son point de reprise, puis la transaction de fin de table et son controle).
///  6. Les reprises placees apres les tables (compteurs : num.Reprendre lit la V2 vivante, au dernier moment).
///  7. Fini : totaux, duree, evenement 210 ; l'instantane est supprime.
/// Un ECART : la table en cours est annulee, rien de ce qui suit n'est fait, l'etat « arrete » et son motif sont au
/// journal, l'instantane est supprime (ARRET PROPRE) ; il ne se reprend pas. Un INCIDENT (toute autre erreur, une
/// annulation, un arret brutal) : ce qui est enregistre reste, l'etat « interrompu » et le point de reprise sont au
/// journal, l'instantane est garde ; relancer reprend au lot suivant (REC-005). La base V2 n'est jamais ecrite (CA06).
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
            await using var cmd = new SqlCommand("SELECT CONCAT(CASE WHEN OBJECT_ID(N'rec.Transfert') IS NULL THEN N'REC-001_transfert ' END, " +
                                                 "CASE WHEN OBJECT_ID(N'rec.TransfertLot') IS NULL THEN N'REC-005_reprise' END);", v3);
            if (await cmd.ExecuteScalarAsync(ct) is string { Length: > 0 } manque)
                return new RapportTransfert(0, "refuse", $"La base V3 n'a pas la migration {manque.Trim()} : lancer Base\\RigV3.Migrations sur elle d'abord.",
                                            baseV2, null, null, null, chrono.ElapsedMilliseconds, [], []);
        }

        // Un seul transfert d'une base V2 a la fois dans sa base V3 (verrou de session : un processus mort le rend). Le
        // dernier transfert de la base dit s'il faut REPRENDRE (interrompu, ou « en cours » sans personne derriere) (REC-005).
        await using var verrou = await VerrouAsync(o.ConnexionV3, baseV2, ct);
        var precedent = await DernierAsync(o.ConnexionV3, baseV2, ct);
        var reprise = verrou is not null && precedent is { Etat: "en cours" or "interrompu" };
        var journal = reprise
            ? await JournalTransfert.ReprendreAsync(o.ConnexionV3, precedent!.Id, o.Suivre, ct)
            : await JournalTransfert.OuvrirAsync(o.ConnexionV3, baseV2, v2.DataSource, o.Suivre, ct);
        var lignes = new List<LigneRapport>();
        var schema = new List<string>();
        Instantane? instantane = null;
        TableV2? enCours = null;
        var provisoires = 0;
        // Les totaux ne comptent que les tables portees une ligne pour une ligne (lues = ecrites + rejetees) : une table
        // traduite par une reprise (droits, compteurs) a sa preuve dans le comparateur de sa story.
        (long Lues, long Ecrites, long Rejetees, long Liens) Totaux()
        {
            var comptees = lignes.Where(l => l.Ecrites is not null).ToList();
            return (comptees.Sum(l => l.Lues ?? 0), comptees.Sum(l => l.Ecrites ?? 0), comptees.Sum(l => l.Rejetees ?? 0), comptees.Sum(l => l.LiensCasses ?? 0));
        }
        RapportTransfert Rapport(string etat, string? motif, string? point = null) => new(journal.Id, etat, motif, baseV2, instantane?.Nom, instantane?.Le,
                                                                    instantane?.VersionSuivi, journal.DureePrecedente + chrono.ElapsedMilliseconds, lignes, schema)
        { Passage = journal.Passage, PointDeReprise = point, Provisoires = provisoires };
        try
        {
            if (reprise)
                await journal.EcrireAsync("reprise", $"Reprise {journal.Passage} du transfert {journal.Id} de la base V2 {baseV2} ({v2.DataSource}), " +
                    $"{(precedent!.Etat == "interrompu" ? $"interrompu le {precedent.Fin} ({precedent.Motif})" : $"commence le {precedent.Debut} et arrete sans fin (processus disparu)")} ; " +
                    $"point de reprise : {precedent.Point ?? "aucune ligne ecrite"}", ct);
            await journal.EcrireAsync("debut", $"Transfert {journal.Id} de la base V2 {baseV2} ({v2.DataSource}) : {regles.Count} regles, " +
                                               $"{reprises.Count} reprises ({string.Join(", ", reprises.Select(r => r.Nom))})" +
                                               (o.ReglesProvisoires ? " ; REPETITION : regles provisoires pour les tables sans regle (REC-008)" : ""), ct);
            var refus = new List<string>();
            if (verrou is null)
                refus.Add($"un transfert de la base {baseV2} tourne deja dans cette base V3 (son verrou est tenu) : attendre sa fin");
            else if (!reprise && precedent is { Etat: "fini" })
                refus.Add($"la base {baseV2} a deja un transfert dans cette base V3 (transfert {precedent.Id}, fini, commence le {precedent.Debut})");
            else if (!reprise && precedent is { Etat: "arrete" })
                refus.Add($"la base {baseV2} a deja un transfert dans cette base V3 (transfert {precedent.Id}, arrete sur un ecart le {precedent.Debut} : {precedent.Motif}) ; " +
                          "un ecart ne se reprend pas : corriger sa cause et repartir d'une base V3 neuve");
            await using (var v3 = new SqlConnection(o.ConnexionV3))
            {
                await v3.OpenAsync(ct);
                await using var cmd = new SqlCommand("SELECT DB_ID(@b);", v3);
                cmd.Parameters.AddWithValue("@b", baseV2);
                if (await cmd.ExecuteScalarAsync(ct) is DBNull)
                    refus.Add($"la base V2 {baseV2} n'est pas sur l'instance de la base V3 (Q4 : la V3 est posee a cote de la V2 ; num.Reprendre la lit par son nom)");
            }
            if (refus.Count > 0) return await RefuserOuSuspendreAsync(reprise, journal, refus, Rapport, chrono, ct);

            if (reprise && precedent!.Instantane is { } nom)
            {
                // La reprise lit le MEME etat fige que le premier passage : sinon les lignes deja ecrites ne seraient plus
                // celles de la V2 lue, et la version de suivi (REC-003) mentirait.
                instantane = await Instantane.RattacherAsync(o.ConnexionV2, nom, ct)
                    ?? throw new EcartTransfert($"l'instantane {nom} du premier passage n'existe plus : la reprise ne relirait pas le meme etat fige de {baseV2} ; " +
                                                "repartir d'une base V3 neuve");
                await journal.EcrireAsync("instantane", $"Etat fige repris : instantane {instantane.Nom} du {instantane.Le:yyyy-MM-dd HH:mm:ss.fff}", ct);
            }
            else
            {
                instantane = await Instantane.CreerAsync(o.ConnexionV2, ct);
                await journal.InstantaneAsync(instantane, ct);
                await journal.EcrireAsync("instantane", $"Etat fige de {baseV2} au {instantane.Le:yyyy-MM-dd HH:mm:ss.fff} : instantane {instantane.Nom}, " +
                    (instantane.VersionSuivi is { } v ? $"version de suivi des changements {v} (la recopie continue, REC-003, repart de la)"
                                                      : "suivi des changements non active sur la base V2 (REC-003 T01)"), ct);
            }

            await using var lecture = new SqlConnection(instantane.Connexion);
            await lecture.OpenAsync(ct);
            var catalogue = await CatalogueV2.LireAsync(lecture, ct);
            await journal.EcrireAsync("catalogue", $"{catalogue.Tables.Count} tables, {catalogue.Tables.Values.Sum(t => t.Colonnes.Count)} colonnes, " +
                $"{catalogue.NombreCles} cles etrangeres ({catalogue.Tables.Values.Sum(t => t.Cles.Count(k => k.Desactivee || k.NonVerifiee))} desactivees ou non verifiees), " +
                $"{catalogue.NombreDeclencheurs} declencheurs, {catalogue.Tables.Values.Sum(t => t.Lignes)} lignes", ct);
            if (o.ReglesProvisoires)
            {
                regles = ReglesProvisoires.Completer(regles, catalogue);
                provisoires = regles.Count(r => r.Provisoire);
                await journal.ProvisoiresAsync(provisoires, ct);
                await journal.EcrireAsync("provisoire", $"REPETITION : {provisoires} tables sans regle recoivent une regle PROVISOIRE « telle quelle » (REC-008) : " +
                    string.Join(", ", regles.Where(r => r.Provisoire).Select(r => r.Table).Take(40)) + (provisoires > 40 ? "..." : ""), ct);
            }

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
                if (r.Sorte == SorteRegle.TelleQuelle && CopieTelleQuelle.Empechement(t, r.Provisoire) is { } empeche) refus.Add(empeche);
                if (r.Sorte == SorteRegle.Reprise && !reprises.Any(p => string.Equals(p.Nom, r.Reprise, StringComparison.OrdinalIgnoreCase)))
                    refus.Add($"{r.Table} : la reprise « {r.Reprise} » de sa regle n'existe pas");
            }
            foreach (var t in catalogue.Tables.Values.Where(t => !parTable.ContainsKey(t.Nom)).OrderBy(t => t.Nom, StringComparer.Ordinal))
            {
                schema.Add($"table en plus, sans regle : {t.Nom} ({t.Lignes} lignes)");
                refus.Add($"table sans regle : {t.Nom}");
            }
            foreach (var ligne in schema) await journal.EcrireAsync("schema", ligne, ct);

            // Une reprise retrouve ce que les passages precedents ont fini (une ligne de rapport par table) et entame.
            var finies = reprise ? await TablesFiniesAsync(o.ConnexionV3, journal.Id, ct) : [];
            var entamees = reprise ? await TablesEntameesAsync(o.ConnexionV3, journal.Id, ct) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            lignes.AddRange(finies);
            var nomsFinis = finies.Select(f => f.TableV2).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var tellesQuelles = regles.Where(r => r.Sorte == SorteRegle.TelleQuelle && catalogue.Tables.ContainsKey(r.Table))
                                      .Select(r => catalogue.Tables[r.Table]).ToList();
            await using (var v3 = new SqlConnection(o.ConnexionV3))
            {
                await v3.OpenAsync(ct);
                foreach (var t in tellesQuelles.Where(t => !nomsFinis.Contains(t.Nom) && !entamees.Contains(t.Nom)))
                {
                    await using var cmd = new SqlCommand("SELECT OBJECT_ID(@n);", v3);
                    cmd.Parameters.AddWithValue("@n", CopieTelleQuelle.NomV3(t));
                    if (await cmd.ExecuteScalarAsync(ct) is not DBNull) refus.Add($"la table {CopieTelleQuelle.NomV3Texte(t)} existe deja dans la base V3");
                }
            }
            if (refus.Count > 0) return await RefuserOuSuspendreAsync(reprise, journal, refus, Rapport, chrono, ct);

            var (ordre, cycles) = OrdreEcriture(tellesQuelles);
            await journal.EcrireAsync("ordre", "Ordre d'ecriture (cles etrangeres) : " + string.Join(", ", ordre.Select(t => t.Nom)) +
                (cycles.Count > 0 ? $" ; cycles de cles, ordre par nom : {string.Join(", ", cycles)}" : ""), ct);
            if (reprise)
                await journal.EcrireAsync("reprise", $"{nomsFinis.Count} table(s) deja finie(s), non refaite(s) : {string.Join(", ", nomsFinis.Order(StringComparer.Ordinal))}" +
                                                     (entamees.Count > 0 ? $" ; entamee(s), reprise(s) au lot suivant : {string.Join(", ", entamees.Order(StringComparer.Ordinal))}" : ""), ct);

            var contexte = new ContexteReprise(instantane.Connexion, o.ConnexionV3, baseV2, journal.Id);
            var reglesReprise = regles.Where(r => r.Sorte == SorteRegle.Reprise).ToList();
            foreach (var r in reprises.Where(r => !r.ApresLesTables))
                await ReprendreAsync(r, reglesReprise, catalogue, lecture, contexte, journal, lignes, nomsFinis, ct);

            // 5. Les tables telles quelles (et provisoires), lot par lot.
            var avecRejets = new HashSet<string>(finies.Where(f => f.Rejetees > 0).Select(f => f.TableV2), StringComparer.OrdinalIgnoreCase);
            async Task ApresLot(string table, int lot, long ecrites, CancellationToken c)
            {
                o.Suivre?.Invoke(new EvenementTransfert(DateTime.Now, "lot", $"{table} : lot {lot} enregistre, {ecrites} lignes ecrites"));
                if (o.ApresLot is not null) await o.ApresLot(table, lot, ecrites, c);
            }
            await using (var v3 = new SqlConnection(o.ConnexionV3))
            {
                await v3.OpenAsync(ct);
                for (var i = 0; i < ordre.Count; i++)
                {
                    var t = ordre[i];
                    if (nomsFinis.Contains(t.Nom)) continue;
                    enCours = t;
                    await journal.EcrireAsync("table", $"{t.Nom} : copie {(parTable[t.Nom].Provisoire ? "PROVISOIRE " : "")}telle quelle ({t.Lignes} lignes au catalogue)" +
                                                       (entamees.Contains(t.Nom) ? " — reprise apres son dernier lot enregistre" : ""), ct);
                    var l = await CopieTelleQuelle.CopierAsync(lecture, v3, catalogue, t, parTable[t.Nom], i + 1, journal.Id, baseV2,
                                                               o.TailleLot, avecRejets, ct, journal.Passage, ApresLot);
                    enCours = null;
                    lignes.Add(l);
                    if (l.Rejetees > 0) avecRejets.Add(t.Nom);
                    await journal.EcrireAsync("table", $"{t.Nom} : {l.Lues} lues, {l.Ecrites} ecrites, {l.Rejetees} rejetees, " +
                                                       $"{l.LiensCasses} liens casses gardes, {l.DureeMs} ms — {l.Detail}", ct);
                }
            }

            foreach (var r in reprises.Where(r => r.ApresLesTables))
                await ReprendreAsync(r, reglesReprise, catalogue, lecture, contexte, journal, lignes, nomsFinis, ct);

            var totaux = Totaux();
            await journal.EcrireAsync("fin", $"Transfert fini en {journal.DureePrecedente + chrono.ElapsedMilliseconds} ms" +
                                             (journal.Passage > 0 ? $" ({journal.Passage} reprise(s))" : "") +
                                             $" : {totaux.Lues} lignes lues, {totaux.Ecrites} ecrites, " +
                                             $"{totaux.Rejetees} rejetees, {totaux.Liens} liens casses gardes" +
                                             (provisoires > 0 ? $", {provisoires} tables par une regle PROVISOIRE" : ""), ct);
            await journal.TerminerAsync("fini", null, chrono.ElapsedMilliseconds, totaux, baseV2, ct);
            return Rapport("fini", null);
        }
        catch (EcartTransfert e)
        {
            // ARRET PROPRE (REC-001) : un ecart ne se reprend pas ; la table entamee est annulee, l'instantane supprime.
            if (enCours is not null) await CopieTelleQuelle.AnnulerAsync(o.ConnexionV3, journal.Id, enCours);
            await ArreterAsync(journal, "Arret sur un ecart : " + e.Message, Totaux(), baseV2, chrono);
            return Rapport("arrete", "Arret sur un ecart : " + e.Message);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // INCIDENT (REC-005) : base injoignable, serveur redemarre, erreur sur une ligne... Ce qui est enregistre reste ;
            // l'instantane est garde ; relancer le meme transfert reprend au dernier lot enregistre.
            o.Journal?.LogError(e, "Transfert {Id} de {BaseV2} interrompu sur un incident", journal.Id, baseV2);
            var motif = $"Interrompu sur un incident{(enCours is null ? "" : $" (table {enCours.Nom})")} : {e.Message}";
            instantane?.Garder();
            await SuspendreAsync(journal, motif, Totaux(), chrono);
            return Rapport("interrompu", motif, await PointAsync(o.ConnexionV3, journal.Id));
        }
        catch (OperationCanceledException)
        {
            instantane?.Garder();
            await SuspendreAsync(journal, $"Interrompu (annulation demandee){(enCours is null ? "" : $", table {enCours.Nom}")}", Totaux(), chrono);
            throw;
        }
        finally
        {
            if (instantane is not null) await instantane.DisposeAsync();
        }
    }

    /// <summary>Le dernier transfert de la base V2 dans cette base V3 (un refus ne compte pas : rien n'a ete ecrit).</summary>
    private sealed record Precedent(int Id, string Etat, string? Instantane, string Debut, string? Fin, string? Motif, string? Point);

    private static async Task<Precedent?> DernierAsync(string connexionV3, string baseV2, CancellationToken ct)
    {
        await using var cn = new SqlConnection(connexionV3);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT TOP (1) Id, Etat, Instantane, CONVERT(nvarchar(19), Debut, 120), CONVERT(nvarchar(19), Fin, 120), Motif, PointDeReprise " +
            "FROM rec.Transfert WHERE BaseV2 = @b AND Etat <> 'refuse' ORDER BY Id DESC;", cn);
        cmd.Parameters.AddWithValue("@b", baseV2);
        await using var l = await cmd.ExecuteReaderAsync(ct);
        if (!await l.ReadAsync(ct)) return null;
        string? S(int i) => l.IsDBNull(i) ? null : l.GetString(i);
        return new Precedent(l.GetInt32(0), l.GetString(1), S(2), l.GetString(3), S(4), S(5), S(6));
    }

    /// <summary>Le verrou d'un transfert : verrou applicatif de SESSION sur une connexion a part, hors du pool (sa
    /// session finit avec le processus). Null s'il est deja tenu.</summary>
    private sealed class Verrou(SqlConnection cn) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var cmd = new SqlCommand("EXEC sp_releaseapplock @Resource = @r, @LockOwner = 'Session';", cn);
                cmd.Parameters.Add(new SqlParameter("@r", System.Data.SqlDbType.NVarChar, 255) { Value = Ressource });
                await cmd.ExecuteNonQueryAsync();
            }
            finally { await cn.DisposeAsync(); }
        }
        public string Ressource { get; init; } = "";
    }

    private static async Task<Verrou?> VerrouAsync(string connexionV3, string baseV2, CancellationToken ct)
    {
        var cn = new SqlConnection(new SqlConnectionStringBuilder(connexionV3) { Pooling = false }.ConnectionString);
        await cn.OpenAsync(ct);
        var ressource = "rigv3-transfert:" + baseV2;
        await using var cmd = new SqlCommand("DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; SELECT @r;", cn);
        cmd.Parameters.Add(new SqlParameter("@res", System.Data.SqlDbType.NVarChar, 255) { Value = ressource });
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) >= 0) return new Verrou(cn) { Ressource = ressource };
        await cn.DisposeAsync();
        return null;
    }

    private static async Task<List<LigneRapport>> TablesFiniesAsync(string connexionV3, int id, CancellationToken ct)
    {
        var finies = new List<LigneRapport>();
        await using var cn = new SqlConnection(connexionV3);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT TableV2, Ordre, Regle, Story, TableV3, Lues, Ecrites, Rejetees, LiensCasses, Correspondances, Octets, " +
                                             "ISNULL(DureeMs, 0), Detail FROM rec.TransfertTable WHERE IdTransfert = @id ORDER BY Fin;", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var l = await cmd.ExecuteReaderAsync(ct);
        long? N(int i) => l.IsDBNull(i) ? null : l.GetInt64(i);
        while (await l.ReadAsync(ct))
            finies.Add(new LigneRapport(l.GetString(0), l.GetInt32(1), l.GetString(2), l.GetString(3), l.IsDBNull(4) ? null : l.GetString(4),
                                        N(5), N(6), N(7), N(8), N(9), N(10), l.GetInt64(11), l.IsDBNull(12) ? null : l.GetString(12)));
        return finies;
    }

    private static async Task<HashSet<string>> TablesEntameesAsync(string connexionV3, int id, CancellationToken ct)
    {
        var entamees = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cn = new SqlConnection(connexionV3);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT DISTINCT l.TableV2 FROM rec.TransfertLot l WHERE l.IdTransfert = @id " +
                                             "AND NOT EXISTS (SELECT 1 FROM rec.TransfertTable t WHERE t.IdTransfert = l.IdTransfert AND t.TableV2 = l.TableV2);", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var l = await cmd.ExecuteReaderAsync(ct);
        while (await l.ReadAsync(ct)) entamees.Add(l.GetString(0));
        return entamees;
    }

    private static async Task<string?> PointAsync(string connexionV3, int id)
    {
        await using var cn = new SqlConnection(connexionV3);
        await cn.OpenAsync(CancellationToken.None);
        return await JournalTransfert.PointDeRepriseAsync(cn, id, CancellationToken.None);
    }

    /// <summary>Un refus au premier passage ; a une reprise, un refus (base V2 absente, regles changees) est un incident :
    /// le transfert reste interrompu, reprenable une fois la cause levee, au lieu de perdre ce qui est ecrit.</summary>
    private static async Task<RapportTransfert> RefuserOuSuspendreAsync(bool reprise, JournalTransfert journal, List<string> refus,
                                                                       Func<string, string?, string?, RapportTransfert> rapport, Stopwatch chrono, CancellationToken ct)
    {
        if (!reprise) return await RefuserAsync(journal, refus, (e, m) => rapport(e, m, null), chrono, ct);
        var motif = "Reprise impossible pour l'instant : " + string.Join(" ; ", refus);
        await SuspendreAsync(journal, motif, (0, 0, 0, 0), chrono);
        return rapport("interrompu", motif, await PointAsync(journal.ConnexionV3, journal.Id));
    }

    private static async Task SuspendreAsync(JournalTransfert journal, string motif, (long, long, long, long) totaux, Stopwatch chrono)
    {
        await journal.EcrireAsync("incident", motif, CancellationToken.None);
        await journal.InterrompreAsync(motif, chrono.ElapsedMilliseconds, totaux);
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

    /// <summary>Appelle une reprise de la phase 1, puis compte et relie ce qu'elle porte une ligne pour une ligne (CA03).
    /// A une reprise apres incident (REC-005), une reprise de la phase 1 dont toutes les tables ont leur ligne de rapport
    /// n'est pas rappelee ; sinon elle l'est (elles sont rejouables : reglages remplaces, comptes « deja faits ») et ses
    /// tables deja rapportees ne le sont pas deux fois.</summary>
    private static async Task ReprendreAsync(IRepriseTransfert r, List<RegleTable> reglesReprise, CatalogueV2 catalogue, SqlConnection lecture,
                                             ContexteReprise contexte, JournalTransfert journal, List<LigneRapport> lignes,
                                             IReadOnlySet<string> finies, CancellationToken ct)
    {
        var siennes = reglesReprise.Where(g => string.Equals(g.Reprise, r.Nom, StringComparison.OrdinalIgnoreCase) && catalogue.Tables.ContainsKey(g.Table)).ToList();
        if (siennes.Count > 0 && siennes.All(g => finies.Contains(g.Table)))
        {
            await journal.EcrireAsync("reprise", $"Reprise « {r.Nom} » ({r.Story}) : faite au passage precedent ({siennes.Count} tables rapportees), non rappelee", ct);
            return;
        }
        var debut = DateTime.Now;
        var chrono = Stopwatch.StartNew();
        await journal.EcrireAsync("reprise", $"Reprise « {r.Nom} » ({r.Story}) : appel", ct);
        var bilan = await r.ExecuterAsync(contexte, ct);
        await journal.EcrireAsync("reprise", $"Reprise « {r.Nom} » : {(bilan.DejaFaite ? "deja faite dans cette base V3, non rejouee ; " : "")}{bilan.Detail}", ct);
        if (bilan.Ecarts.Count > 0)
            throw new EcartTransfert($"reprise « {r.Nom} » ({r.Story}) : {bilan.Ecarts.Count} ecart(s) : {string.Join(" ; ", bilan.Ecarts.Take(10))}");

        var portees = bilan.Tables.ToDictionary(p => p.TableV2, StringComparer.OrdinalIgnoreCase);
        foreach (var regle in siennes.Where(g => !finies.Contains(g.Table)))
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
            $"SELECT @b, @t, x.IdV2, @t3, x.IdV3, @id FROM ({p.SelectionV2V3}) AS x (IdV2, IdV3) " +
            // Rejouable (REC-005) : un passage coupe entre ces correspondances et la ligne de rapport ne les double pas.
            "WHERE NOT EXISTS (SELECT 1 FROM rec.Correspondance c WHERE c.BaseV2 = @b AND c.TableV2 = @t AND c.IdV2 = x.IdV2);", cn) { CommandTimeout = 0 })
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
