using System.Data;
using Microsoft.Data.SqlClient;
using RigV3.Serveur.Socle.Greffes;

namespace RecopieTransfertCheck;

/// <summary>
/// LA BASE V2 JETABLE DU BANC, construite a partir d'un EXTRAIT de RIG_DEV (SQL-DEV\DEV, lu en lecture seule par la
/// porte de la V2 : un SELECT seul, LectureV2.ExigerLecture).
///
///  - les tables (colonnes, types, classements, identites, valeurs par defaut, cle primaire) sont relues dans
///    INFORMATION_SCHEMA de RIG_DEV : un DDL ecrit ici, independant de celui du moteur (CatalogueV2), pour que le banc
///    compare deux lectures et non une seule avec elle-meme ;
///  - les petites tables (reprises de la phase 1, UTILISATEUR, CATEGORIE) sont copiees entieres ; les grandes, par un
///    extrait : les affaires les plus recentes et leurs demandes, des arrivees, adresses et clients recents, puis TOUTE
///    ligne qu'un lien de l'extrait vise (fermeture sur les cles etrangeres, jusqu'a ce que rien ne manque) ;
///  - les cles etrangeres entre ces tables sont reposees AVEC verification : une fermeture incomplete arrete le banc ;
///  - les declencheurs de RIG_DEV sur ces tables sont recrees puis desactives : ils sont dans le catalogue (le moteur doit
///    les relever) sans que les injections du banc ne les fassent jouer.
/// Rien n'est ecrit dans RIG_DEV ; la base jetable est sur le SQL Server du poste et supprimee a la fin.
/// </summary>
public static class ExtraitRigDev
{
    public sealed record Bilan(IReadOnlyDictionary<string, long> Lignes, int Cles, int Declencheurs, IReadOnlyList<string> DeclencheursNonRecrees);

    /// <summary>
    /// L'extrait d'un banc complete d'un petit extrait de chaque table des regles du serveur qu'il ne tire pas deja : un banc qui
    /// transfere avec les regles du serveur (l'outil, le passage) doit les avoir toutes dans sa base V2, sinon le transfert la
    /// refuse (« table attendue absente », REC-001 CA01). Depuis le lot 10, ces regles portent aussi les fiches de REC-012 a
    /// REC-019 (FichesV3.cs) ; une story qui en ajoute d'autres n'a rien a changer aux bancs.
    /// </summary>
    public static Dictionary<string, string> Completer(IReadOnlyDictionary<string, string> extraits, IEnumerable<string> entieres,
                                                       IEnumerable<string> tablesDesRegles, int lignes = 20)
    {
        var complet = new Dictionary<string, string>(extraits, StringComparer.OrdinalIgnoreCase);
        var deja = new HashSet<string>(entieres, StringComparer.OrdinalIgnoreCase);
        foreach (var t in tablesDesRegles.Where(t => !deja.Contains(t) && !complet.ContainsKey(t)))
            complet[t] = $"SELECT TOP ({lignes}) * FROM dbo.[{t}]";
        return complet;
    }

    public static async Task<Bilan> ConstruireAsync(string rigDev, string v2, IReadOnlyList<string> entieres,
                                                    IReadOnlyDictionary<string, string> extraits, Action<string> info)
    {
        // Une table demandee deux fois (entiere et en extrait, ou deux fois entiere : CATEGORIE est lue par une reprise depuis le
        // lot 10) n'est creee qu'une fois ; entiere l'emporte.
        entieres = [.. entieres.Distinct(StringComparer.OrdinalIgnoreCase)];
        extraits = extraits.Where(e => !entieres.Contains(e.Key, StringComparer.OrdinalIgnoreCase)).ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);
        var tables = entieres.Concat(extraits.Keys).ToList();
        var dans = string.Join(", ", tables.Select(t => $"'{t}'"));
        var cles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // 1. Les tables.
        foreach (var t in tables)
        {
            var cols = await LireAsync(rigDev,
                "SELECT c.COLUMN_NAME, c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.NUMERIC_PRECISION, c.NUMERIC_SCALE, c.DATETIME_PRECISION, " +
                $"c.IS_NULLABLE, c.COLUMN_DEFAULT, c.COLLATION_NAME, COLUMNPROPERTY(OBJECT_ID('dbo.{t}'), c.COLUMN_NAME, 'IsIdentity'), " +
                $"IDENT_SEED('dbo.{t}'), IDENT_INCR('dbo.{t}') FROM INFORMATION_SCHEMA.COLUMNS c WHERE c.TABLE_SCHEMA = 'dbo' AND c.TABLE_NAME = '{t}' " +
                "ORDER BY c.ORDINAL_POSITION");
            if (cols.Rows.Count == 0) throw new InvalidOperationException($"RIG_DEV : table dbo.{t} introuvable.");
            var pk = await LireAsync(rigDev,
                "SELECT k.COLUMN_NAME FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE k " +
                "ON k.CONSTRAINT_NAME = tc.CONSTRAINT_NAME AND k.TABLE_SCHEMA = tc.TABLE_SCHEMA AND k.TABLE_NAME = tc.TABLE_NAME " +
                $"WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY' AND tc.TABLE_SCHEMA = 'dbo' AND tc.TABLE_NAME = '{t}' ORDER BY k.ORDINAL_POSITION");
            cles[t] = [.. pk.Rows.Cast<DataRow>().Select(r => (string)r[0])];
            var ddl = new List<string>();
            foreach (DataRow r in cols.Rows)
            {
                var type = (string)r[1];
                var taille = r[2] is DBNull ? "" : Convert.ToInt32(r[2]) < 0 ? "(max)" : $"({r[2]})";
                var sqlType = type switch
                {
                    "varchar" or "nvarchar" or "char" or "nchar" or "varbinary" or "binary" => type + taille,
                    "decimal" or "numeric" => $"{type}({r[3]},{r[4]})",
                    "datetime2" or "time" or "datetimeoffset" => $"{type}({r[5]})",
                    _ => type,
                };
                // RIG_DEV garde un classement sur des colonnes bit et datetime (ARRIVEE, INSCRIPTION...) : texte seulement.
                // fix-ok: erreur 447 « Expression type bit is invalid for COLLATE clause » (INFORMATION_SCHEMA.COLUMNS de RIG_DEV, 15 colonnes).
                var collation = r[8] is string c && type is "varchar" or "char" or "nvarchar" or "nchar" or "text" or "ntext" ? $" COLLATE {c}" : "";
                var identite = r[9] is int i && i == 1 ? $" IDENTITY({r[10]},{r[11]})" : "";
                var defaut = r[7] is string d ? $" DEFAULT {d}" : "";
                ddl.Add($"[{r[0]}] {sqlType}{collation}{identite}{((string)r[6] == "YES" ? " NULL" : " NOT NULL")}{defaut}");
            }
            if (cles[t].Count > 0) ddl.Add($"CONSTRAINT [PK_{t}] PRIMARY KEY ({string.Join(", ", cles[t].Select(c => $"[{c}]"))})");
            await ExecuterAsync(v2, $"CREATE TABLE dbo.[{t}] ({string.Join(", ", ddl)});");
        }

        // 2. Les donnees : tables entieres, puis l'extrait, puis la fermeture sur les liens.
        foreach (var t in entieres) await CopierAsync(rigDev, v2, t, $"SELECT * FROM dbo.[{t}]");
        foreach (var (t, select) in extraits) await CopierAsync(rigDev, v2, t, select);

        var liens = await LireAsync(rigDev,
            "SELECT f.name, OBJECT_NAME(f.parent_object_id), COL_NAME(fc.parent_object_id, fc.parent_column_id), OBJECT_NAME(f.referenced_object_id), " +
            "COL_NAME(fc.referenced_object_id, fc.referenced_column_id), (SELECT COUNT(*) FROM sys.foreign_key_columns x WHERE x.constraint_object_id = f.object_id) " +
            "FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id = f.object_id " +
            $"WHERE OBJECT_NAME(f.parent_object_id) IN ({dans}) AND OBJECT_NAME(f.referenced_object_id) IN ({dans}) ORDER BY f.name");
        var simples = liens.Rows.Cast<DataRow>().Where(r => Convert.ToInt32(r[5]) == 1)
            .Select(r => (Nom: (string)r[0], Enfant: (string)r[1], Colonne: (string)r[2], Parent: (string)r[3], Visee: (string)r[4])).ToList();
        if (simples.Count != liens.Rows.Count) throw new InvalidOperationException("RIG_DEV : une cle etrangere a plusieurs colonnes entre les tables de l'extrait (non prevu par le banc).");
        for (var tour = 1; ; tour++)
        {
            var ajoutees = 0;
            foreach (var k in simples.Where(k => extraits.ContainsKey(k.Parent)))
            {
                var manquants = await LireAsync(v2,
                    $"SELECT DISTINCT CONVERT(varchar(40), c.[{k.Colonne}]) FROM dbo.[{k.Enfant}] c WHERE c.[{k.Colonne}] IS NOT NULL " +
                    $"AND NOT EXISTS (SELECT 1 FROM dbo.[{k.Parent}] p WHERE p.[{k.Visee}] = c.[{k.Colonne}])");
                var ids = manquants.Rows.Cast<DataRow>().Select(r => (string)r[0]).ToList();
                foreach (var paquet in ids.Chunk(500))
                    ajoutees += await CopierAsync(rigDev, v2, k.Parent, $"SELECT * FROM dbo.[{k.Parent}] WHERE [{k.Visee}] IN ({string.Join(", ", paquet)})");
            }
            info($"fermeture sur les liens, tour {tour} : {ajoutees} lignes ajoutees");
            if (ajoutees == 0) break;
            if (tour > 30) throw new InvalidOperationException("fermeture de l'extrait : plus de 30 tours.");
        }

        // 3. Les cles etrangeres, verifiees ; 4. les declencheurs, recrees puis desactives.
        // Une cle peut viser une colonne unique et non la cle primaire (HUROL_LOGIN_UTLST -> UTILISATEUR.UTLST_LOGIN) :
        // la colonne visee recoit son index unique, que RIG_DEV a forcement pour porter la cle.
        // fix-ok: « There are no primary or candidate keys in the referenced table 'dbo.UTILISATEUR' » (banc, 2e essai).
        foreach (var (parent, visee) in simples.Where(k => !cles[k.Parent].SequenceEqual([k.Visee], StringComparer.OrdinalIgnoreCase))
                                               .Select(k => (k.Parent, k.Visee)).Distinct())
            await ExecuterAsync(v2, $"CREATE UNIQUE INDEX [UX_{parent}_{visee}] ON dbo.[{parent}] ([{visee}]);");
        foreach (var k in simples)
            await ExecuterAsync(v2, $"ALTER TABLE dbo.[{k.Enfant}] WITH CHECK ADD CONSTRAINT [{k.Nom}] FOREIGN KEY ([{k.Colonne}]) REFERENCES dbo.[{k.Parent}] ([{k.Visee}]);");
        var declencheurs = await LireAsync(rigDev,
            "SELECT g.name, OBJECT_NAME(g.parent_id), m.definition, m.uses_quoted_identifier, m.uses_ansi_nulls FROM sys.triggers g " +
            $"JOIN sys.sql_modules m ON m.object_id = g.object_id WHERE OBJECT_NAME(g.parent_id) IN ({dans}) ORDER BY g.name");
        var echoues = new List<string>();
        var recrees = 0;
        foreach (DataRow r in declencheurs.Rows)
        {
            // Une base qui ne laisse pas lire la definition de ses declencheurs (RIG_RECETTE pour le compte du poste, lu le
            // 2026-11-21 : T_DEMANDE_VALIDATION) : le declencheur n'est pas recree, il est nomme.
            if (r[2] is DBNull) { echoues.Add($"{r[0]} ({r[1]}) : definition illisible dans la base source"); continue; }
            try
            {
                await using var cn = new SqlConnection(v2);
                await cn.OpenAsync();
                await using (var o = new SqlCommand($"SET QUOTED_IDENTIFIER {((bool)r[3] ? "ON" : "OFF")}; SET ANSI_NULLS {((bool)r[4] ? "ON" : "OFF")};", cn))
                    await o.ExecuteNonQueryAsync();
                await using (var c = new SqlCommand((string)r[2], cn)) await c.ExecuteNonQueryAsync();
                await using (var d = new SqlCommand($"DISABLE TRIGGER [{r[0]}] ON dbo.[{r[1]}];", cn)) await d.ExecuteNonQueryAsync();
                recrees++;
            }
            catch (SqlException e) { echoues.Add($"{r[0]} ({r[1]}) : {e.Message}"); }
        }

        var lignes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tables) lignes[t] = Convert.ToInt64((await LireAsync(v2, $"SELECT COUNT_BIG(*) FROM dbo.[{t}]")).Rows[0][0]);
        return new Bilan(lignes, simples.Count, recrees, echoues);
    }

    /// <summary>Une lecture de RIG_DEV : la porte de la V2 la refuse si ce n'est pas un SELECT seul.</summary>
    private static async Task<DataTable> LireAsync(string chaine, string sql)
    {
        if (chaine.Contains("SQL-DEV", StringComparison.OrdinalIgnoreCase) || chaine.Contains("RIG_DEV", StringComparison.OrdinalIgnoreCase))
            LectureV2.ExigerLecture(sql);
        await using var cn = new SqlConnection(chaine);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 600 };
        var t = new DataTable();
        await using var l = await cmd.ExecuteReaderAsync();
        t.Load(l);
        return t;
    }

    private static async Task<int> CopierAsync(string rigDev, string v2, string table, string select)
    {
        LectureV2.ExigerLecture(select);
        await using var src = new SqlConnection(rigDev);
        await src.OpenAsync();
        await using var cmd = new SqlCommand(select, src) { CommandTimeout = 600 };
        await using var lecteur = await cmd.ExecuteReaderAsync();
        using var copie = new SqlBulkCopy(v2, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls)
        { DestinationTableName = $"dbo.[{table}]", BulkCopyTimeout = 600, BatchSize = 20_000 };
        for (var i = 0; i < lecteur.FieldCount; i++) copie.ColumnMappings.Add(lecteur.GetName(i), lecteur.GetName(i));
        await copie.WriteToServerAsync(lecteur);
        return copie.RowsCopied;
    }

    private static async Task ExecuterAsync(string chaine, string sql)
    {
        await using var cn = new SqlConnection(chaine);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 600 };
        await cmd.ExecuteNonQueryAsync();
    }
}
