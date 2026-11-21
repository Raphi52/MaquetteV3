using Microsoft.Data.SqlClient;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Recopie;

// ============================================================
// LE SCHEMA REEL D'UNE BASE V2, lu dans son catalogue (REC-001 T02, CA01).
//
// Deux bases du meme logiciel n'ont pas le meme schema (RIG_DEV : 861 tables, 789 cles ; RIG_RECETTE : 838 et
// 777), et le projet de base de donnees de la V2 ne dit pas ce qui est deploye (AFFAIRE.sql n'a pas la colonne
// client que RIG_DEV porte avec sa cle) : le transfert se regle sur le catalogue de CHAQUE base, jamais sur le
// projet. Tout passe par LectureV2 (un SELECT seul).
// ============================================================

public sealed record ColonneV2(string Nom, string TypeNom, string TypeBase, short Longueur, byte Precision, byte Echelle,
                               bool AccepteNull, bool Identite, decimal? Graine, decimal? Pas, string? Collation,
                               bool Calculee, bool TypeAssemblage)
{
    /// <summary>Le type a ecrire dans la V3 pour garder la valeur telle quelle ; null si la copie ne sait pas le porter
    /// (types d'assemblage : geography, geometry, hierarchyid, types CLR).</summary>
    public string? TypeSql => TypeAssemblage ? null : TypeBase switch
    {
        "varchar" or "char" or "varbinary" or "binary" => $"{TypeBase}({(Longueur < 0 ? "max" : Longueur.ToString())})",
        "nvarchar" or "nchar" => $"{TypeBase}({(Longueur < 0 ? "max" : (Longueur / 2).ToString())})",
        "decimal" or "numeric" => $"{TypeBase}({Precision},{Echelle})",
        "datetime2" or "time" or "datetimeoffset" => $"{TypeBase}({Echelle})",
        "float" => $"float({Precision})",
        // Un horodatage de ligne n'a de sens que dans la V2 ; sa valeur est gardee, en binaire de 8 octets.
        "timestamp" => "binary(8)",
        _ => TypeBase,
    };

    /// <summary>Types que BINARY_CHECKSUM ne sait pas comparer : hors de l'empreinte de controle (comptes seuls).</summary>
    public bool HorsEmpreinte => TypeBase is "text" or "ntext" or "image" or "xml" or "sql_variant";

    /// <summary>Seul un type texte porte un classement. Le catalogue en donne aussi a d'autres : RIG_DEV en garde un sur
    /// 15 colonnes bit et datetime de ARRIVEE, INSCRIPTION, SERVICE et ACTION_RCS (lu le 2026-11-21), qu'un COLLATE refuse.
    /// fix-ok: erreur 447 « Expression type bit is invalid for COLLATE clause » sur ARRIVEE.ARRV_PAIEMENT_JOINT (banc, 1er essai).</summary>
    public bool PorteClassement => TypeBase is "varchar" or "char" or "nvarchar" or "nchar" or "text" or "ntext";
}

public sealed record CleEtrangereV2(string Nom, string Table, string TableVisee, IReadOnlyList<string> Colonnes,
                                    IReadOnlyList<string> ColonnesVisees, bool Desactivee, bool NonVerifiee);

public sealed record DeclencheurV2(string Nom, string Table, bool Desactive);

public sealed class TableV2(string schema, string nom)
{
    public string Schema { get; } = schema;
    public string NomSeul { get; } = nom;
    /// <summary>Le nom des regles : nom seul pour dbo, « schema.NOM » sinon.</summary>
    public string Nom => Schema == "dbo" ? NomSeul : $"{Schema}.{NomSeul}";
    public string NomSql => $"{Sql.Q(Schema)}.{Sql.Q(NomSeul)}";
    public List<ColonneV2> Colonnes { get; } = [];
    public List<string> ClePrimaire { get; } = [];
    public List<CleEtrangereV2> Cles { get; } = [];
    public List<DeclencheurV2> Declencheurs { get; } = [];
    public long Lignes { get; set; }

    public ColonneV2 Colonne(string nom) => Colonnes.First(c => string.Equals(c.Nom, nom, StringComparison.OrdinalIgnoreCase));
}

public sealed class CatalogueV2
{
    /// <summary>Tables des outils de diagramme de SQL Server, creees par l'outil et non par RIG : hors du transfert,
    /// comme hors du critere D2 du carnet (inventaire-d2.json, rubrique hors_d2, decision du chef).</summary>
    public static readonly IReadOnlySet<string> TablesDeDiagramme = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sysdiagrams", "dtproperties" };

    public Dictionary<string, TableV2> Tables { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int NombreCles => Tables.Values.Sum(t => t.Cles.Count);
    public int NombreDeclencheurs => Tables.Values.Sum(t => t.Declencheurs.Count);

    public static async Task<CatalogueV2> LireAsync(SqlConnection v2, CancellationToken ct)
    {
        var c = new CatalogueV2();
        TableV2 Table(string schema, string nom)
        {
            var cle = schema == "dbo" ? nom : $"{schema}.{nom}";
            if (!c.Tables.TryGetValue(cle, out var t)) c.Tables[cle] = t = new TableV2(schema, nom);
            return t;
        }
        bool Retenue(string schema, string nom) => !(schema == "dbo" && TablesDeDiagramme.Contains(nom));

        await using (var l = await LectureV2.LireAsync(v2, """
            SELECT s.name, t.name, c.name, ty.name, TYPE_NAME(ty.system_type_id), c.max_length, c.precision, c.scale,
                   c.is_nullable, c.is_identity, CONVERT(decimal(38, 0), ic.seed_value), CONVERT(decimal(38, 0), ic.increment_value),
                   c.collation_name, c.is_computed, ty.is_assembly_type
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.columns c ON c.object_id = t.object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            LEFT JOIN sys.identity_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE t.is_ms_shipped = 0
            ORDER BY s.name, t.name, c.column_id
            """, ct))
            while (await l.ReadAsync(ct))
            {
                if (!Retenue(l.GetString(0), l.GetString(1))) continue;
                Table(l.GetString(0), l.GetString(1)).Colonnes.Add(new ColonneV2(
                    l.GetString(2), l.GetString(3), l.IsDBNull(4) ? l.GetString(3) : l.GetString(4), l.GetInt16(5), l.GetByte(6), l.GetByte(7),
                    l.GetBoolean(8), l.GetBoolean(9), l.IsDBNull(10) ? null : l.GetDecimal(10), l.IsDBNull(11) ? null : l.GetDecimal(11),
                    l.IsDBNull(12) ? null : l.GetString(12), l.GetBoolean(13), l.GetBoolean(14)));
            }

        await using (var l = await LectureV2.LireAsync(v2, """
            SELECT s.name, t.name, c.name
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.is_primary_key = 1 AND t.is_ms_shipped = 0
            ORDER BY s.name, t.name, ic.key_ordinal
            """, ct))
            while (await l.ReadAsync(ct))
                if (Retenue(l.GetString(0), l.GetString(1))) Table(l.GetString(0), l.GetString(1)).ClePrimaire.Add(l.GetString(2));

        var cles = new Dictionary<string, (string Schema, string Table, string SchemaVise, string TableVisee, List<string> Cols, List<string> Vises, bool Desactivee, bool NonVerifiee)>(StringComparer.Ordinal);
        await using (var l = await LectureV2.LireAsync(v2, """
            SELECT f.name, sp.name, tp.name, sr.name, tr.name, cp.name, cr.name, f.is_disabled, f.is_not_trusted, SCHEMA_NAME(f.schema_id)
            FROM sys.foreign_keys f
            JOIN sys.foreign_key_columns fc ON fc.constraint_object_id = f.object_id
            JOIN sys.tables tp ON tp.object_id = f.parent_object_id
            JOIN sys.schemas sp ON sp.schema_id = tp.schema_id
            JOIN sys.tables tr ON tr.object_id = f.referenced_object_id
            JOIN sys.schemas sr ON sr.schema_id = tr.schema_id
            JOIN sys.columns cp ON cp.object_id = fc.parent_object_id AND cp.column_id = fc.parent_column_id
            JOIN sys.columns cr ON cr.object_id = fc.referenced_object_id AND cr.column_id = fc.referenced_column_id
            WHERE tp.is_ms_shipped = 0
            ORDER BY f.object_id, fc.constraint_column_id
            """, ct))
            while (await l.ReadAsync(ct))
            {
                var nom = l.GetString(9) + "." + l.GetString(0);
                if (!cles.TryGetValue(nom, out var k))
                    cles[nom] = k = (l.GetString(1), l.GetString(2), l.GetString(3), l.GetString(4), [], [], l.GetBoolean(7), l.GetBoolean(8));
                k.Cols.Add(l.GetString(5));
                k.Vises.Add(l.GetString(6));
            }
        foreach (var (nom, k) in cles)
        {
            if (!Retenue(k.Schema, k.Table) || !Retenue(k.SchemaVise, k.TableVisee)) continue;
            var t = Table(k.Schema, k.Table);
            var visee = Table(k.SchemaVise, k.TableVisee);
            t.Cles.Add(new CleEtrangereV2(nom[(nom.IndexOf('.') + 1)..], t.Nom, visee.Nom, k.Cols, k.Vises, k.Desactivee, k.NonVerifiee));
        }

        await using (var l = await LectureV2.LireAsync(v2, """
            SELECT s.name, t.name, g.name, g.is_disabled
            FROM sys.triggers g
            JOIN sys.tables t ON t.object_id = g.parent_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0
            ORDER BY s.name, t.name, g.name
            """, ct))
            while (await l.ReadAsync(ct))
                if (Retenue(l.GetString(0), l.GetString(1)))
                    Table(l.GetString(0), l.GetString(1)).Declencheurs.Add(new DeclencheurV2(l.GetString(2), l.GetString(1), l.GetBoolean(3)));

        await using (var l = await LectureV2.LireAsync(v2, """
            SELECT s.name, t.name, SUM(p.rows)
            FROM sys.partitions p
            JOIN sys.tables t ON t.object_id = p.object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE p.index_id IN (0, 1) AND t.is_ms_shipped = 0
            GROUP BY s.name, t.name
            """, ct))
            while (await l.ReadAsync(ct))
                if (Retenue(l.GetString(0), l.GetString(1))) Table(l.GetString(0), l.GetString(1)).Lignes = l.GetInt64(2);
        return c;
    }
}

/// <summary>Ecriture des noms et des expressions SQL du transfert.</summary>
internal static class Sql
{
    public static string Q(string nom) => "[" + nom.Replace("]", "]]") + "]";

    public static string Texte(string s) => "N'" + s.Replace("'", "''") + "'";

    /// <summary>Une colonne en texte, pour l'identifiant d'une ligne : date en ISO 8601, binaire en hexadecimal,
    /// flottant a 17 chiffres ; le reste par la conversion par defaut.</summary>
    public static string EnTexte(string alias, ColonneV2 c) => c.TypeBase switch
    {
        "date" or "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" or "time" => $"CONVERT(nvarchar(200), {alias}.{Q(c.Nom)}, 126)",
        "binary" or "varbinary" or "timestamp" => $"CONVERT(nvarchar(200), CONVERT(varbinary(100), {alias}.{Q(c.Nom)}), 1)",
        "float" or "real" => $"CONVERT(nvarchar(200), {alias}.{Q(c.Nom)}, 3)",
        _ => $"CONVERT(nvarchar(200), {alias}.{Q(c.Nom)})",
    };

    /// <summary>L'identifiant d'une ligne en texte : ses colonnes jointes par « | ».</summary>
    public static string Identifiant(string alias, IEnumerable<ColonneV2> colonnes)
        => string.Join(" + N'|' + ", colonnes.Select(c => EnTexte(alias, c)));

    /// <summary>
    /// « La ligne vient apres cette cle » dans l'ordre de la cle primaire (lecture par lots, REC-005 T03) :
    /// (k1 &gt; @k0) OR (k1 = @k0 AND k2 &gt; @k1)... Les parametres ont EXACTEMENT le type de leur colonne (<see cref="Parametre"/>) :
    /// un varchar compare a un nvarchar est converti, et sous un classement SQL_ le tri d'un texte non Unicode n'est pas
    /// celui du meme texte en Unicode -- la lecture sauterait ou relirait des lignes.
    /// </summary>
    public static string Apres(string alias, IReadOnlyList<ColonneV2> cle, string prefixe)
        => "(" + string.Join(" OR ", cle.Select((c, i) =>
               "(" + string.Join(" AND ", cle.Take(i).Select((p, j) => $"{alias}.{Q(p.Nom)} = @{prefixe}{j}")
                                           .Append($"{alias}.{Q(c.Nom)} > @{prefixe}{i}")) + ")")) + ")";

    /// <summary>Le type de parametre d'une colonne de cle, ou null s'il n'est pas porte par la lecture par lots.</summary>
    public static SqlParameter? Parametre(ColonneV2 c, string nom, object valeur)
    {
        System.Data.SqlDbType? type = c.TypeBase switch
        {
            "int" => System.Data.SqlDbType.Int, "bigint" => System.Data.SqlDbType.BigInt, "smallint" => System.Data.SqlDbType.SmallInt,
            "tinyint" => System.Data.SqlDbType.TinyInt, "bit" => System.Data.SqlDbType.Bit, "uniqueidentifier" => System.Data.SqlDbType.UniqueIdentifier,
            "varchar" => System.Data.SqlDbType.VarChar, "char" => System.Data.SqlDbType.Char, "nvarchar" => System.Data.SqlDbType.NVarChar,
            "nchar" => System.Data.SqlDbType.NChar, "datetime" => System.Data.SqlDbType.DateTime, "smalldatetime" => System.Data.SqlDbType.SmallDateTime,
            "datetime2" => System.Data.SqlDbType.DateTime2, "date" => System.Data.SqlDbType.Date, "decimal" or "numeric" => System.Data.SqlDbType.Decimal,
            "binary" => System.Data.SqlDbType.Binary, "varbinary" => System.Data.SqlDbType.VarBinary,
            _ => null,
        };
        if (type is null) return null;
        var p = new SqlParameter(nom, type.Value) { Value = valeur };
        switch (c.TypeBase)
        {
            case "varchar" or "char" or "binary" or "varbinary": p.Size = c.Longueur; break;
            case "nvarchar" or "nchar": p.Size = c.Longueur < 0 ? -1 : c.Longueur / 2; break;
            case "decimal" or "numeric": p.Precision = c.Precision; p.Scale = c.Echelle; break;
            case "datetime2": p.Scale = c.Echelle; break;
        }
        return p;
    }
}
