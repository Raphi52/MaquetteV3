using System.Globalization;

namespace RigV3.Outils.Anonymisation;

// ============================================================
// LE SCHEMA DE LA BASE SOURCE, lu dans ses vues systeme (sys.*), et sa reecriture en DDL pour
// la copie : tables (colonnes, types, classements, identites, valeurs par defaut, colonnes
// calculees), index et cles primaires, cles etrangeres, contraintes, vues, fonctions,
// procedures et declencheurs. La copie a la FORME de la source (SOC-021 « garde la forme »).
// ============================================================

public sealed record ColonneSql(
    string Nom, string Type, int Longueur, int Precision, int Echelle, bool AccepteNull, bool Identite,
    bool Calculee, string? Collation, string? Calcul, bool Persistee, string? NomDefaut, string? Defaut,
    decimal? GraineIdentite, decimal? PasIdentite);

public sealed class TableSql(int id, string schema, string nom)
{
    public int Id { get; } = id;
    public string Schema { get; } = schema;
    public string Nom { get; } = nom;
    public List<ColonneSql> Colonnes { get; } = [];
    /// <summary>Derniere valeur d'identite donnee par la source (sys.identity_columns.last_value).</summary>
    public decimal? DerniereIdentite { get; set; }
    public string Complet => $"{Ddl.Q(Schema)}.{Ddl.Q(Nom)}";
    public string Libelle => $"{Schema}.{Nom}";
}

public sealed record IndexSql(int Table, string Nom, int Type, bool Unique, bool Primaire, bool Contrainte, string? Filtre,
                              List<(string Colonne, bool Descendant, bool Inclus)> Colonnes);

public sealed record CleEtrangere(string Nom, int Table, int Reference, bool NonVerifiee, bool Desactivee,
                                  int Suppression, int MiseAJour, List<(string Colonne, string Referencee)> Colonnes);

public sealed record ContrainteVerif(int Table, string Nom, string Definition, bool NonVerifiee, bool Desactivee);

public sealed record ModuleSql(string Schema, string Nom, string Type, string Definition, bool GuillemetsIdentifiants, bool AnsiNulls)
{
    public string Libelle => $"{Schema}.{Nom}";
}

public sealed class SchemaSource
{
    public List<TableSql> Tables { get; } = [];
    public List<IndexSql> Index { get; } = [];
    public List<CleEtrangere> Cles { get; } = [];
    public List<ContrainteVerif> Contraintes { get; } = [];
    public List<ModuleSql> Modules { get; } = [];
    /// <summary>Index que la copie ne sait pas refaire (columnstore, XML, spatial) : listes au rapport.</summary>
    public List<string> IndexIgnores { get; } = [];
    public string Collation { get; private set; } = "";

    public static async Task<SchemaSource> LireAsync(PorteSource source, CancellationToken ct)
    {
        var s = new SchemaSource
        {
            Collation = (string?)await source.ValeurAsync("SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation'))", ct) ?? "",
        };
        var parId = new Dictionary<int, TableSql>();
        foreach (var l in await source.LignesAsync(
                     "SELECT t.object_id, SCHEMA_NAME(t.schema_id), t.name, CONVERT(decimal(38,0), ic.last_value) " +
                     "FROM sys.tables t LEFT JOIN sys.identity_columns ic ON ic.object_id = t.object_id " +
                     "WHERE t.is_ms_shipped = 0 ORDER BY SCHEMA_NAME(t.schema_id), t.name", ct))
        {
            var t = new TableSql((int)l[0]!, (string)l[1]!, (string)l[2]!) { DerniereIdentite = (decimal?)l[3] };
            s.Tables.Add(t);
            parId[t.Id] = t;
        }

        foreach (var l in await source.LignesAsync(
                     "SELECT c.object_id, c.name, " +
                     "CASE WHEN ty.is_user_defined = 1 AND ty.is_assembly_type = 0 THEN TYPE_NAME(c.system_type_id) ELSE ty.name END, " +
                     "CONVERT(int, c.max_length), CONVERT(int, c.precision), CONVERT(int, c.scale), c.is_nullable, c.is_identity, c.is_computed, " +
                     "c.collation_name, cc.definition, cc.is_persisted, dc.name, dc.definition, " +
                     "CONVERT(decimal(38,0), ic.seed_value), CONVERT(decimal(38,0), ic.increment_value) " +
                     "FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id " +
                     "JOIN sys.tables t ON t.object_id = c.object_id " +
                     "LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id " +
                     "LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id " +
                     "LEFT JOIN sys.identity_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id " +
                     "WHERE t.is_ms_shipped = 0 ORDER BY c.object_id, c.column_id", ct))
        {
            if (!parId.TryGetValue((int)l[0]!, out var t)) continue;
            t.Colonnes.Add(new ColonneSql((string)l[1]!, (string)l[2]!, (int)l[3]!, (int)l[4]!, (int)l[5]!, (bool)l[6]!, (bool)l[7]!,
                (bool)l[8]!, (string?)l[9], (string?)l[10], l[11] is true, (string?)l[12], (string?)l[13], (decimal?)l[14], (decimal?)l[15]));
        }

        IndexSql? courant = null;
        foreach (var l in await source.LignesAsync(
                     "SELECT i.object_id, i.name, CONVERT(int, i.type), i.is_unique, i.is_primary_key, i.is_unique_constraint, i.filter_definition, " +
                     "COL_NAME(ic.object_id, ic.column_id), ic.is_descending_key, ic.is_included_column, i.index_id " +
                     "FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id " +
                     "JOIN sys.tables t ON t.object_id = i.object_id " +
                     "WHERE t.is_ms_shipped = 0 AND i.is_hypothetical = 0 AND i.type > 0 " +
                     "ORDER BY i.object_id, i.index_id, ic.is_included_column, ic.key_ordinal, ic.index_column_id", ct))
        {
            var table = (int)l[0]!;
            var nom = (string)l[1]!;
            var type = (int)l[2]!;
            if (!parId.TryGetValue(table, out var t)) continue;
            if (type is not (1 or 2))
            {
                var libelle = $"{t.Libelle}.{nom} (type {type})";
                if (!s.IndexIgnores.Contains(libelle)) s.IndexIgnores.Add(libelle);
                continue;
            }
            if (courant is null || courant.Table != table || courant.Nom != nom)
            {
                courant = new IndexSql(table, nom, type, (bool)l[3]!, (bool)l[4]!, (bool)l[5]!, (string?)l[6], []);
                s.Index.Add(courant);
            }
            var inclus = (bool)l[9]!;
            // Une colonne de cle de la cle groupee apparait aussi « incluse » dans un index non groupe : on ne garde que les vraies.
            if (inclus && courant.Colonnes.Any(c => c.Colonne == (string)l[7]!)) continue;
            courant.Colonnes.Add(((string)l[7]!, (bool)l[8]!, inclus));
        }

        CleEtrangere? cle = null;
        foreach (var l in await source.LignesAsync(
                     "SELECT fk.name, fk.parent_object_id, fk.referenced_object_id, fk.is_not_trusted, fk.is_disabled, " +
                     "CONVERT(int, fk.delete_referential_action), CONVERT(int, fk.update_referential_action), " +
                     "COL_NAME(fkc.parent_object_id, fkc.parent_column_id), COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) " +
                     "FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id " +
                     "ORDER BY fk.object_id, fkc.constraint_column_id", ct))
        {
            var nom = (string)l[0]!;
            if (cle is null || cle.Nom != nom || cle.Table != (int)l[1]!)
            {
                cle = new CleEtrangere(nom, (int)l[1]!, (int)l[2]!, (bool)l[3]!, (bool)l[4]!, (int)l[5]!, (int)l[6]!, []);
                s.Cles.Add(cle);
            }
            cle.Colonnes.Add(((string)l[7]!, (string)l[8]!));
        }

        foreach (var l in await source.LignesAsync(
                     "SELECT cc.parent_object_id, cc.name, cc.definition, cc.is_not_trusted, cc.is_disabled FROM sys.check_constraints cc " +
                     "JOIN sys.tables t ON t.object_id = cc.parent_object_id WHERE t.is_ms_shipped = 0", ct))
            s.Contraintes.Add(new ContrainteVerif((int)l[0]!, (string)l[1]!, (string)l[2]!, (bool)l[3]!, (bool)l[4]!));

        foreach (var l in await source.LignesAsync(
                     "SELECT SCHEMA_NAME(o.schema_id), o.name, RTRIM(o.type), m.definition, m.uses_quoted_identifier, m.uses_ansi_nulls " +
                     "FROM sys.sql_modules m JOIN sys.objects o ON o.object_id = m.object_id " +
                     "WHERE o.is_ms_shipped = 0 AND o.type IN ('V', 'P', 'FN', 'IF', 'TF', 'TR') AND m.definition IS NOT NULL " +
                     "ORDER BY o.object_id", ct))
            s.Modules.Add(new ModuleSql((string)l[0]!, (string)l[1]!, (string)l[2]!, (string)l[3]!, (bool)l[4]!, (bool)l[5]!));
        return s;
    }
}

/// <summary>La reecriture en DDL (SQL Server 2017 et plus, comme la cible des greffes).</summary>
public static class Ddl
{
    /// <summary>Un nom entre crochets, crochet fermant double (QUOTENAME).</summary>
    public static string Q(string nom) => "[" + nom.Replace("]", "]]") + "]";

    public static string Type(ColonneSql c) => c.Type switch
    {
        "varchar" or "char" or "varbinary" or "binary" => $"{c.Type}({(c.Longueur < 0 ? "max" : c.Longueur.ToString(CultureInfo.InvariantCulture))})",
        "nvarchar" or "nchar" => $"{c.Type}({(c.Longueur < 0 ? "max" : (c.Longueur / 2).ToString(CultureInfo.InvariantCulture))})",
        "decimal" or "numeric" => $"{c.Type}({c.Precision},{c.Echelle})",
        "datetime2" or "time" or "datetimeoffset" => $"{c.Type}({c.Echelle})",
        "float" => $"float({c.Precision})",
        // Une colonne rowversion ne s'ecrit pas : la copie garde sa valeur en binary(8) (au rapport).
        "timestamp" or "rowversion" => "binary(8)",
        _ => c.Type,
    };

    public static string Colonne(ColonneSql c)
    {
        if (c.Calculee) return $"{Q(c.Nom)} AS {c.Calcul}{(c.Persistee ? " PERSISTED" : "")}";
        var s = $"{Q(c.Nom)} {Type(c)}";
        // Le classement ne s'ecrit que sur un texte : RIG_DEV porte un collation_name sur des tinyint, datetime
        // et bit (TYPE_BIEN.R_TYPEBIEN_REC_NIV…, lu le 2026-11-21), que CREATE TABLE refuse (« invalid for COLLATE »).
        if (c.Collation is not null && CatalogueRegles.EstTexte(c.Type)) s += $" COLLATE {c.Collation}";
        if (c.Identite)
            s += $" IDENTITY({(c.GraineIdentite ?? 1).ToString(CultureInfo.InvariantCulture)},{(c.PasIdentite ?? 1).ToString(CultureInfo.InvariantCulture)})";
        s += c.AccepteNull ? " NULL" : " NOT NULL";
        if (c.Defaut is not null) s += $" CONSTRAINT {Q(c.NomDefaut!)} DEFAULT {c.Defaut}";
        return s;
    }

    public static string CreerTable(TableSql t)
        => $"CREATE TABLE {t.Complet} ({string.Join(", ", t.Colonnes.Select(Colonne))});";

    public static string CreerIndex(TableSql t, IndexSql i)
    {
        var cles = string.Join(", ", i.Colonnes.Where(c => !c.Inclus).Select(c => Q(c.Colonne) + (c.Descendant ? " DESC" : " ASC")));
        var genre = i.Type == 1 ? "CLUSTERED" : "NONCLUSTERED";
        if (i.Primaire) return $"ALTER TABLE {t.Complet} ADD CONSTRAINT {Q(i.Nom)} PRIMARY KEY {genre} ({cles});";
        if (i.Contrainte) return $"ALTER TABLE {t.Complet} ADD CONSTRAINT {Q(i.Nom)} UNIQUE {genre} ({cles});";
        var inclus = i.Colonnes.Where(c => c.Inclus).Select(c => Q(c.Colonne)).ToList();
        return $"CREATE {(i.Unique ? "UNIQUE " : "")}{genre} INDEX {Q(i.Nom)} ON {t.Complet} ({cles})" +
               (inclus.Count > 0 ? $" INCLUDE ({string.Join(", ", inclus)})" : "") +
               (i.Filtre is null ? "" : $" WHERE {i.Filtre}") + ";";
    }

    private static string Action(int a) => a switch { 1 => "CASCADE", 2 => "SET NULL", 3 => "SET DEFAULT", _ => "NO ACTION" };

    public static string CreerCle(TableSql t, TableSql r, CleEtrangere k, bool verifier)
        => $"ALTER TABLE {t.Complet} WITH {(verifier ? "CHECK" : "NOCHECK")} ADD CONSTRAINT {Q(k.Nom)} FOREIGN KEY " +
           $"({string.Join(", ", k.Colonnes.Select(c => Q(c.Colonne)))}) REFERENCES {r.Complet} " +
           $"({string.Join(", ", k.Colonnes.Select(c => Q(c.Referencee)))}) ON DELETE {Action(k.Suppression)} ON UPDATE {Action(k.MiseAJour)};";

    public static string CreerContrainte(TableSql t, ContrainteVerif c, bool verifier)
        => $"ALTER TABLE {t.Complet} WITH {(verifier ? "CHECK" : "NOCHECK")} ADD CONSTRAINT {Q(c.Nom)} CHECK {c.Definition};";
}
