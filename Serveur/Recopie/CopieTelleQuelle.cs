using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Recopie;

/// <summary>Un ecart constate pendant le transfert : la table en cours est annulee et le transfert s'arrete.</summary>
public sealed class EcartTransfert(string message) : Exception(message);

/// <summary>
/// LA COPIE D'UNE TABLE « TELLE QUELLE » (REC-001 T02, CA03, CA04), dans UNE transaction de la base V3 :
///  1. la table v2.NOM, aux colonnes du catalogue REEL de la base (memes types, classements, identite) et a sa cle
///     primaire ; ni ses cles etrangeres (un lien casse herite y est garde tel quel), ni ses declencheurs (le
///     comportement de la V2 se refait dans les stories de domaine) ;
///  2. les liens casses, lus dans l'instantane : chaque cle etrangere de la V2, desactivee ou non, est verifiee ligne a
///     ligne ; la regle de la table dit marquer (ligne ecrite, lien dans rec.LienCasse) ou rejeter (rec.Rejet) ;
///  3. la copie par lots (SqlBulkCopy), valeurs et identifiants gardes, depuis l'instantane ;
///  4. la correspondance : chaque ligne ecrite, son identifiant V2 et son identifiant V3 (le meme ici) ;
///  5. les lignes dont le lien vise une ligne rejetee par le transfert : marquees « rejetee par le transfert » ;
///  6. le controle de fin : lues = ecrites + rejetees, une correspondance par ligne ecrite, et la meme empreinte
///     (CHECKSUM_AGG de BINARY_CHECKSUM des colonnes) des deux cotes. Un ecart annule la table et arrete tout.
/// </summary>
public static class CopieTelleQuelle
{
    public static string NomV3(TableV2 t) => $"v2.{Sql.Q(t.Nom)}";

    /// <summary>Le nom de la table V3 tel qu'il s'ecrit dans le journal et la correspondance (« v2.AFFAIRE »).</summary>
    public static string NomV3Texte(TableV2 t) => $"v2.{t.Nom}";

    /// <summary>Ce qui empeche de copier la table telle quelle (verifie avant toute ecriture), ou null.</summary>
    public static string? Empechement(TableV2 t)
    {
        if (t.ClePrimaire.Count == 0)
            return $"{t.Nom} n'a pas de cle primaire : sa regle doit dire comment identifier une ligne (rec.Correspondance)";
        var non = t.Colonnes.Where(c => c.TypeSql is null).Select(c => $"{c.Nom} ({c.TypeNom})").ToList();
        return non.Count == 0 ? null : $"{t.Nom} a des colonnes d'un type que la copie telle quelle ne porte pas : {string.Join(", ", non)}";
    }

    public static string Ddl(TableV2 t)
    {
        var colonnes = t.Colonnes.Select(c =>
            $"{Sql.Q(c.Nom)} {c.TypeSql}" +
            (c.Collation is { } col && c.PorteClassement ? $" COLLATE {col}" : "") +
            (c.Identite ? $" IDENTITY({c.Graine ?? 1}, {c.Pas ?? 1})" : "") +
            (c.AccepteNull ? " NULL" : " NOT NULL"));
        var pk = $"PK_v2_{t.Nom.Replace('.', '_')}";
        if (pk.Length > 128) pk = pk[..128];
        return $"CREATE TABLE {NomV3(t)} ({string.Join(", ", colonnes)}, CONSTRAINT {Sql.Q(pk)} PRIMARY KEY CLUSTERED " +
               $"({string.Join(", ", t.ClePrimaire.Select(Sql.Q))}));";
    }

    /// <summary>« La ligne c a un lien casse par cette cle » : toutes ses colonnes renseignees, aucune ligne visee.</summary>
    private static string Casse(CleEtrangereV2 k, TableV2 visee)
        => "(" + string.Join(" AND ", k.Colonnes.Select(c => $"c.{Sql.Q(c)} IS NOT NULL")) +
           $" AND NOT EXISTS (SELECT 1 FROM {visee.NomSql} AS p WHERE " +
           string.Join(" AND ", k.Colonnes.Zip(k.ColonnesVisees, (c, v) => $"p.{Sql.Q(v)} = c.{Sql.Q(c)}")) + "))";

    private static string Valeur(TableV2 t, CleEtrangereV2 k) => Sql.Identifiant("c", k.Colonnes.Select(t.Colonne));

    public static async Task<LigneRapport> CopierAsync(SqlConnection instantane, SqlConnection v3, CatalogueV2 catalogue, TableV2 t,
                                                       RegleTable regle, int ordre, int idTransfert, string baseV2, int tailleLot,
                                                       IReadOnlySet<string> tablesAvecRejets, CancellationToken ct)
    {
        var chrono = Stopwatch.StartNew();
        var debut = DateTime.Now;
        var cle = t.ClePrimaire.Select(t.Colonne).ToList();
        var identifiantV2 = Sql.Identifiant("c", cle);
        var rejeter = regle.SiLienCasse == SiLienCasse.Rejeter;

        // 2. Les liens casses de la V2, avant d'ecrire quoi que ce soit (lecture de l'instantane).
        var casses = new List<(string IdV2, CleEtrangereV2 Cle, string Valeur)>();
        foreach (var k in t.Cles)
        {
            var visee = catalogue.Tables[k.TableVisee];
            var sql = $"SELECT {identifiantV2}, {Valeur(t, k)} FROM {t.NomSql} AS c WHERE {Casse(k, visee)}";
            await using var cmd = Lecture(instantane, sql);
            await using var l = await cmd.ExecuteReaderAsync(ct);
            while (await l.ReadAsync(ct)) casses.Add((l.GetString(0), k, l.GetString(1)));
        }
        var filtre = rejeter && t.Cles.Count > 0
            ? " WHERE NOT (" + string.Join(" OR ", t.Cles.Select(k => Casse(k, catalogue.Tables[k.TableVisee]))) + ")"
            : "";
        var rejetes = rejeter ? casses.GroupBy(c => c.IdV2, StringComparer.Ordinal).ToList() : [];

        await using var tx = (SqlTransaction)await v3.BeginTransactionAsync(ct);
        // 1. La table.
        await ExecuterAsync(v3, tx, Ddl(t), ct);

        // 3. La copie, par lots, depuis l'instantane.
        var colonnes = string.Join(", ", t.Colonnes.Select(c => $"c.{Sql.Q(c.Nom)}"));
        var lots = 0;
        await using (var cmd = Lecture(instantane, $"SELECT {colonnes} FROM {t.NomSql} AS c{filtre} ORDER BY {string.Join(", ", cle.Select(c => $"c.{Sql.Q(c.Nom)}"))}"))
        await using (var lecteur = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct))
        {
            using var copie = new SqlBulkCopy(v3, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.TableLock, tx)
            {
                DestinationTableName = NomV3(t),
                BatchSize = tailleLot,
                NotifyAfter = tailleLot,
                BulkCopyTimeout = 0,
                EnableStreaming = true,
            };
            foreach (var c in t.Colonnes) copie.ColumnMappings.Add(c.Nom, c.Nom);
            foreach (var c in cle) copie.ColumnOrderHints.Add(c.Nom, SortOrder.Ascending);
            copie.SqlRowsCopied += (_, _) => lots++;
            await copie.WriteToServerAsync(lecteur, ct);
        }

        // 4. La correspondance de chaque ligne ecrite (meme identifiant des deux cotes).
        await ExecuterAsync(v3, tx,
            $"INSERT INTO rec.Correspondance (BaseV2, TableV2, IdV2, TableV3, IdV3, IdTransfert) " +
            $"SELECT {Sql.Texte(baseV2)}, {Sql.Texte(t.Nom)}, {identifiantV2}, {Sql.Texte(NomV3Texte(t))}, {identifiantV2}, {idTransfert} FROM {NomV3(t)} AS c;", ct);

        // 2 (suite). Rejets et liens marques.
        if (rejeter)
        {
            var table = new DataTable();
            foreach (var (nom, type) in new[] { ("IdTransfert", typeof(int)), ("BaseV2", typeof(string)), ("TableV2", typeof(string)), ("IdV2", typeof(string)), ("Motif", typeof(string)), ("Regle", typeof(string)) })
                table.Columns.Add(nom, type);
            foreach (var g in rejetes)
                table.Rows.Add(idTransfert, baseV2, t.Nom, g.Key, Borne("lien casse : " + string.Join(" ; ",
                    g.Select(c => $"{c.Cle.Nom} ({string.Join(", ", c.Cle.Colonnes)} = {c.Valeur}) vise une ligne absente de {c.Cle.TableVisee}")), 1000),
                    "lien casse : rejeter");
            await InsererAsync(v3, tx, "rec.Rejet", table, ct);
        }
        else
        {
            var table = TableLiens();
            foreach (var c in casses)
                table.Rows.Add(idTransfert, baseV2, t.Nom, c.IdV2, c.Cle.Nom, string.Join(", ", c.Cle.Colonnes), Borne(c.Valeur, 400), c.Cle.TableVisee, "absente de la V2");
            await InsererAsync(v3, tx, "rec.LienCasse", table, ct);
        }

        // 5. Les lignes ecrites dont le lien vise une ligne que le transfert a rejetee : dans une table deja copiee, ou dans
        // celle-ci (une demande dont la demande mere est rejetee) ; ses rejets sont deja dans rec.Rejet, meme transaction.
        // fix-ok: le lien d'une table sur elle-meme (DMND_ID_DMND_MERE) n'etait pas vu, la table n'entrant dans
        // tablesAvecRejets qu'apres sa copie (relecture du cas « demande mere rejetee » de REC-001 CA04).
        foreach (var k in t.Cles.Where(k => tablesAvecRejets.Contains(k.TableVisee) ||
                                            rejetes.Count > 0 && string.Equals(k.TableVisee, t.Nom, StringComparison.OrdinalIgnoreCase)))
        {
            var visee = catalogue.Tables[k.TableVisee];
            if (!k.ColonnesVisees.SequenceEqual(visee.ClePrimaire, StringComparer.OrdinalIgnoreCase)) continue;
            await ExecuterAsync(v3, tx,
                "INSERT INTO rec.LienCasse (IdTransfert, BaseV2, TableV2, IdV2, Cle, Colonnes, Valeur, TableVisee, Cause) " +
                $"SELECT {idTransfert}, {Sql.Texte(baseV2)}, {Sql.Texte(t.Nom)}, {identifiantV2}, {Sql.Texte(k.Nom)}, " +
                $"{Sql.Texte(string.Join(", ", k.Colonnes))}, {Valeur(t, k)}, {Sql.Texte(k.TableVisee)}, 'rejetee par le transfert' " +
                $"FROM {NomV3(t)} AS c WHERE EXISTS (SELECT 1 FROM rec.Rejet r WHERE r.IdTransfert = {idTransfert} " +
                $"AND r.BaseV2 = {Sql.Texte(baseV2)} AND r.TableV2 = {Sql.Texte(k.TableVisee)} AND r.IdV2 = {Valeur(t, k)});", ct);
        }

        // 6. Le controle de fin de table.
        var lues = Convert.ToInt64(await ScalaireLectureAsync(instantane, $"SELECT COUNT_BIG(*) FROM {t.NomSql} AS c", ct));
        var ecrites = Convert.ToInt64(await ScalaireAsync(v3, tx, $"SELECT COUNT_BIG(*) FROM {NomV3(t)};", ct));
        long rejetees = rejetes.Count;
        var correspondances = Convert.ToInt64(await ScalaireAsync(v3, tx,
            $"SELECT COUNT_BIG(*) FROM rec.Correspondance WHERE BaseV2 = {Sql.Texte(baseV2)} AND TableV2 = {Sql.Texte(t.Nom)} AND IdTransfert = {idTransfert};", ct));
        var liens = Convert.ToInt64(await ScalaireAsync(v3, tx,
            $"SELECT COUNT_BIG(*) FROM rec.LienCasse WHERE IdTransfert = {idTransfert} AND TableV2 = {Sql.Texte(t.Nom)};", ct));
        if (lues != ecrites + rejetees)
            throw new EcartTransfert($"{t.Nom} : {lues} lignes lues dans la V2, {ecrites} ecrites et {rejetees} rejetees (lues = ecrites + rejetees attendu)");
        if (correspondances != ecrites)
            throw new EcartTransfert($"{t.Nom} : {ecrites} lignes ecrites mais {correspondances} correspondances");
        var empreinte = t.Colonnes.Where(c => !c.HorsEmpreinte).ToList();
        if (empreinte.Count > 0)
        {
            string Liste(bool cote2) => string.Join(", ", empreinte.Select(c => c.TypeBase == "timestamp" && cote2
                ? $"CONVERT(binary(8), c.{Sql.Q(c.Nom)})" : $"c.{Sql.Q(c.Nom)}"));
            var e2 = await ScalaireLectureAsync(instantane, $"SELECT CHECKSUM_AGG(BINARY_CHECKSUM({Liste(true)})) FROM {t.NomSql} AS c{filtre}", ct);
            var e3 = await ScalaireAsync(v3, tx, $"SELECT CHECKSUM_AGG(BINARY_CHECKSUM({Liste(false)})) FROM {NomV3(t)} AS c;", ct);
            if (!Equals(e2, e3))
                throw new EcartTransfert($"{t.Nom} : les valeurs ecrites ne sont pas celles de la V2 (empreinte V2 {e2 ?? "vide"}, V3 {e3 ?? "vide"})");
        }
        var octets = Convert.ToInt64(await ScalaireAsync(v3, tx,
            "SELECT ISNULL(SUM(a.total_pages), 0) * 8192 FROM sys.partitions p JOIN sys.allocation_units a ON a.container_id = p.partition_id " +
            $"WHERE p.object_id = OBJECT_ID({Sql.Texte(NomV3(t))});", ct));

        var cassesHerites = rejeter ? 0 : casses.Count;
        var detail = $"{t.Cles.Count} cles etrangeres verifiees ({t.Cles.Count(k => k.Desactivee || k.NonVerifiee)} desactivees ou non verifiees dans la V2), " +
                     $"{cassesHerites} liens casses herites marques, {liens - cassesHerites} liens vers une ligne rejetee, " +
                     $"{t.Declencheurs.Count} declencheurs releves (non recopies), {lots} lots de {tailleLot}";
        var ligne = new LigneRapport(t.Nom, ordre, "telle quelle", regle.Story, NomV3Texte(t), lues, ecrites, rejetees, liens,
                                     correspondances, octets, chrono.ElapsedMilliseconds, detail);
        await JournalTransfert.InsererTableAsync(v3, tx, idTransfert, ligne, debut, ct);
        await tx.CommitAsync(ct);
        return ligne with { DureeMs = chrono.ElapsedMilliseconds };
    }

    internal static DataTable TableLiens()
    {
        var table = new DataTable();
        foreach (var (nom, type) in new[] { ("IdTransfert", typeof(int)), ("BaseV2", typeof(string)), ("TableV2", typeof(string)), ("IdV2", typeof(string)),
                                            ("Cle", typeof(string)), ("Colonnes", typeof(string)), ("Valeur", typeof(string)), ("TableVisee", typeof(string)), ("Cause", typeof(string)) })
            table.Columns.Add(nom, type);
        return table;
    }

    private static string Borne(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

    /// <summary>Une lecture de l'instantane : par la porte de la V2 (un SELECT seul), sans delai (une table de la V2
    /// peut prendre des minutes : DEMANDE fait 5 millions de lignes dans RIG_DEV).</summary>
    private static SqlCommand Lecture(SqlConnection instantane, string sql)
    {
        LectureV2.ExigerLecture(sql);
        return new SqlCommand(sql, instantane) { CommandTimeout = 0 };
    }

    private static async Task<object?> ScalaireLectureAsync(SqlConnection instantane, string sql, CancellationToken ct)
    {
        await using var cmd = Lecture(instantane, sql);
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is DBNull ? null : v;
    }

    private static async Task ExecuterAsync(SqlConnection cn, SqlTransaction tx, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, cn, tx) { CommandTimeout = 0 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<object?> ScalaireAsync(SqlConnection cn, SqlTransaction tx, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, cn, tx) { CommandTimeout = 0 };
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is DBNull ? null : v;
    }

    private static async Task InsererAsync(SqlConnection cn, SqlTransaction tx, string destination, DataTable lignes, CancellationToken ct)
    {
        if (lignes.Rows.Count == 0) return;
        using var copie = new SqlBulkCopy(cn, SqlBulkCopyOptions.Default, tx) { DestinationTableName = destination, BulkCopyTimeout = 0 };
        foreach (DataColumn c in lignes.Columns) copie.ColumnMappings.Add(c.ColumnName, c.ColumnName);
        await copie.WriteToServerAsync(lignes, ct);
    }
}
