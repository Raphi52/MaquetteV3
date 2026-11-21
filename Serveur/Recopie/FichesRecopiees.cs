using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Recopie;

// ============================================================
// LES FICHES PARTAGEES D'UN GREFFE TELLES QUE LA RECOPIE LES VOIT DANS SES DEUX BASES (REC-002 T02, REC-003 T02).
//
// Les regles (ReglesRecopie.cs) disent quelles tables sont partagees ; le CATALOGUE REEL des deux bases (CatalogueV2,
// lu pour la base V2 ET pour la base V3) dit leurs colonnes, leur cle et leurs liens : deux bases du meme logiciel n'ont
// pas le meme schema (recopie.md, « Les choix et leur raison »). Une colonne est recopiee si elle existe des deux cotes,
// n'est ni la cle, ni propre a un logiciel, ni calculee, ni un horodatage de ligne, ni une identite.
//
// LA LIGNE EN JSON. La recopie compare et ecrit une fiche par son texte JSON de SQL Server (FOR JSON, puis OPENJSON
// pour l'ecrire) : memes types des deux cotes, donc meme texte pour la meme valeur. Mesure sur le SQL Server du poste
// (2026-11-21) : datetime, smalldatetime, datetime2, money, decimal, float, bit, varbinary, uniqueidentifier, text,
// char et nvarchar reviennent a l'identique apres FOR JSON -> OPENJSON -> FOR JSON (0 ligne differente par EXCEPT).
// ============================================================

/// <summary>Une table partagee, dans la base V2 et dans la base V3 du greffe.</summary>
public sealed class TableRecopiee
{
    internal TableRecopiee(RegleRecopie regle, string baseV2, TableV2 v2, TableV2 v3, ColonneV2 cle, IReadOnlyList<ColonneV2> colonnes,
                           IReadOnlyList<ColonneV2> propres)
    {
        Regle = regle; BaseV2 = baseV2; V2 = v2; V3 = v3; Cle = cle; Colonnes = colonnes; Propres = propres;
    }

    public RegleRecopie Regle { get; }
    public string BaseV2 { get; }
    public TableV2 V2 { get; }
    public TableV2 V3 { get; }
    /// <summary>La cle primaire (une colonne, meme nom des deux cotes).</summary>
    public ColonneV2 Cle { get; }
    /// <summary>Les colonnes recopiees dans les deux sens (types de la V2, les memes dans la V3).</summary>
    public IReadOnlyList<ColonneV2> Colonnes { get; }
    /// <summary>Les colonnes propres a chaque logiciel (<see cref="RegleRecopie.ColonnesPropres"/>) presentes des deux cotes.</summary>
    public IReadOnlyList<ColonneV2> Propres { get; }
    /// <summary>Les liens vers une autre table partagee : colonne de cette table -> table visee (par sa cle).</summary>
    public Dictionary<string, TableRecopiee> Liens { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string NomV2 => Regle.TableV2;
    public string NomV3 => Regle.TableV3;
    /// <summary>La table V2 vue depuis la base V3, sur la meme instance (Q4) : « [RIG_X].[dbo].[CLIENT] ».</summary>
    public string NomV2Sql => $"{Sql.Q(BaseV2)}.{V2.NomSql}";
    public string NomV3Sql => V3.NomSql;
    public bool CleIdentiteV2 => V2.Colonne(Cle.Nom).Identite;
    public bool CleIdentiteV3 => V3.Colonne(Cle.Nom).Identite;

    /// <summary>Ce que le declencheur de la V3 peut lire dans inserted / deleted : ni text, ni ntext, ni image (SQL Server
    /// les refuse dans un declencheur AFTER). Une colonne de ces types est recopiee, mais sans etat « avant ».</summary>
    public IEnumerable<ColonneV2> ColonnesDeclencheur => Colonnes.Where(c => !Grand(c));

    public static bool Grand(ColonneV2 c) => c.TypeBase is "text" or "ntext" or "image";

    /// <summary>La cle d'une valeur texte : CONVERT vers son type (l'index de la cle sert).</summary>
    public string CleEgale(string alias, string parametre) => $"{alias}.{Sql.Q(Cle.Nom)} = CONVERT({Cle.TypeSql}, {parametre})";

    /// <summary>« SELECT (SELECT ... FOR JSON) » : la ligne de cle <paramref name="parametre"/> en un seul texte (null si absente).</summary>
    public string SelectJson(string table, IEnumerable<ColonneV2> colonnes, string parametre, string? indications = null)
        => $"SELECT (SELECT {string.Join(", ", colonnes.Select(c => $"x.{Sql.Q(c.Nom)} AS {Sql.Q(c.Nom)}"))} FROM {table} AS x " +
           $"{indications} WHERE {CleEgale("x", parametre)} FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);";

    /// <summary>La clause WITH d'un OPENJSON qui relit ces colonnes avec leur type.</summary>
    public static string OpenJson(IEnumerable<ColonneV2> colonnes)
        => "WITH (" + string.Join(", ", colonnes.Select(c => $"{Sql.Q(c.Nom)} {TypeLu(c)} '$.\"{c.Nom}\"'")) + ")";

    private static string TypeLu(ColonneV2 c) => c.TypeBase switch
    {
        "text" => "varchar(max)",
        "ntext" => "nvarchar(max)",
        "image" => "varbinary(max)",
        _ => c.TypeSql!,
    };

    /// <summary>La valeur d'une cle en texte (celle de rec.Correspondance) dans une ligne JSON : 123 -> « 123 », "AB" -> « AB ».</summary>
    public static string? TexteDe(string? brut)
        => brut is null || brut == "null" ? null : brut.StartsWith('"') ? JsonSerializer.Deserialize<string>(brut) : brut;

    /// <summary>L'inverse de <see cref="TexteDe"/> pour une colonne de ce type.</summary>
    public static string Brut(string texte, ColonneV2 c)
        => c.TypeBase is "int" or "bigint" or "smallint" or "tinyint" or "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real"
            ? texte : JsonSerializer.Serialize(texte);
}

/// <summary>Une ligne lue en JSON : colonne -> texte JSON brut de sa valeur (« 12 », « "DUPONT" », « null »).</summary>
public sealed class LigneJson : Dictionary<string, string>
{
    public LigneJson() : base(StringComparer.OrdinalIgnoreCase) { }

    public static LigneJson? Lire(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        using var doc = JsonDocument.Parse(json);
        var l = new LigneJson();
        foreach (var p in doc.RootElement.EnumerateObject()) l[p.Name] = p.Value.GetRawText();
        return l;
    }

    public string Ecrire()
    {
        var sb = new StringBuilder("{");
        foreach (var (k, v) in this)
        {
            if (sb.Length > 1) sb.Append(',');
            sb.Append(JsonSerializer.Serialize(k)).Append(':').Append(v);
        }
        return sb.Append('}').ToString();
    }

    public LigneJson Copie()
    {
        var c = new LigneJson();
        foreach (var (k, v) in this) c[k] = v;
        return c;
    }

    /// <summary>Les colonnes dont la valeur differe entre les deux lignes (seules les colonnes presentes des deux cotes).</summary>
    public static List<string> Differences(LigneJson a, LigneJson b, IEnumerable<string> colonnes)
        => colonnes.Where(c => a.TryGetValue(c, out var x) && b.TryGetValue(c, out var y) && x != y).ToList();

    /// <summary>La valeur pour l'agent : texte sans guillemets, null pour un vide.</summary>
    public static string? Lisible(string? brut)
        => brut is null || brut == "null" ? null : brut.StartsWith('"') ? JsonSerializer.Deserialize<string>(brut) : brut;
}

/// <summary>Les fiches partagees d'un greffe, lues dans le catalogue de ses deux bases.</summary>
public sealed class FichesRecopiees
{
    private FichesRecopiees(string baseV2, List<TableRecopiee> tables, List<string> problemes)
    {
        BaseV2 = baseV2;
        Tables = tables;
        ParTableV2 = tables.ToDictionary(t => t.NomV2, StringComparer.OrdinalIgnoreCase);
        ParTableV3 = tables.ToDictionary(t => t.NomV3, StringComparer.OrdinalIgnoreCase);
        Problemes = problemes;
    }

    public string BaseV2 { get; }
    public IReadOnlyList<TableRecopiee> Tables { get; }
    public IReadOnlyDictionary<string, TableRecopiee> ParTableV2 { get; }
    public IReadOnlyDictionary<string, TableRecopiee> ParTableV3 { get; }
    /// <summary>Ce qui empeche une table d'etre recopiee (elle est alors hors de la recopie) ; la mise en service refuse
    /// tant qu'il en reste.</summary>
    public IReadOnlyList<string> Problemes { get; }

    /// <summary>La chaine de la base V2, sur le serveur de la base V3 (Q4 : meme instance).</summary>
    public static string ConnexionV2(string connexionV3, string baseV2)
        => new SqlConnectionStringBuilder(connexionV3) { InitialCatalog = baseV2 }.ConnectionString;

    public static async Task<FichesRecopiees> ChargerAsync(string connexionV3, string baseV2, IReadOnlyList<RegleRecopie>? regles, CancellationToken ct)
    {
        regles ??= ReglesRecopie.Decouvrir();
        CatalogueV2 v2, v3;
        await using (var cn = new SqlConnection(ConnexionV2(connexionV3, baseV2)))
        {
            await cn.OpenAsync(ct);
            v2 = await CatalogueV2.LireAsync(cn, ct);
        }
        await using (var cn = new SqlConnection(connexionV3))
        {
            await cn.OpenAsync(ct);
            v3 = await CatalogueV2.LireAsync(cn, ct);
        }
        return Construire(baseV2, v2, v3, regles);
    }

    /// <summary>Les fiches partagees vues dans deux catalogues deja lus : ceux des bases vivantes (<see cref="ChargerAsync"/>),
    /// ou ceux des etats figes du controle de nuit (ComparaisonBases), qui compare ainsi exactement ce que la recopie recopie.</summary>
    internal static FichesRecopiees Construire(string baseV2, CatalogueV2 v2, CatalogueV2 v3, IReadOnlyList<RegleRecopie> regles)
    {
        var problemes = new List<string>();
        var tables = new List<TableRecopiee>();
        foreach (var r in regles)
        {
            if (!v2.Tables.TryGetValue(r.TableV2, out var t2)) { problemes.Add($"{r.TableV2} : absente de la base V2 {baseV2}"); continue; }
            if (!v3.Tables.TryGetValue(r.TableV3, out var t3)) { problemes.Add($"{r.TableV2} : sa table V3 {r.TableV3} n'existe pas (transfert non fait ?)"); continue; }
            if (t2.ClePrimaire.Count != 1 || t3.ClePrimaire.Count != 1 || !string.Equals(t2.ClePrimaire[0], t3.ClePrimaire[0], StringComparison.OrdinalIgnoreCase))
            {
                problemes.Add($"{r.TableV2} : la recopie demande une cle primaire d'une colonne, la meme des deux cotes " +
                              $"(V2 : {string.Join("+", t2.ClePrimaire)} ; V3 : {string.Join("+", t3.ClePrimaire)})");
                continue;
            }
            var cle = t2.Colonne(t2.ClePrimaire[0]);
            if (cle.TypeSql != t3.Colonne(cle.Nom).TypeSql) { problemes.Add($"{r.TableV2} : la cle {cle.Nom} n'a pas le meme type des deux cotes"); continue; }
            var propres = new HashSet<string>(r.ColonnesPropres, StringComparer.OrdinalIgnoreCase);
            bool DesDeuxCotes(ColonneV2 c) => t3.Colonnes.Any(x => string.Equals(x.Nom, c.Nom, StringComparison.OrdinalIgnoreCase) && x.TypeSql == c.TypeSql);
            var colonnes = t2.Colonnes.Where(c => !string.Equals(c.Nom, cle.Nom, StringComparison.OrdinalIgnoreCase) && !propres.Contains(c.Nom)
                                                  && !c.Calculee && c.TypeBase != "timestamp" && !c.Identite && !c.TypeAssemblage
                                                  && c.TypeBase is not ("xml" or "sql_variant") && DesDeuxCotes(c)).ToList();
            var propresPresentes = t2.Colonnes.Where(c => propres.Contains(c.Nom) && DesDeuxCotes(c)).ToList();
            foreach (var p in r.ColonnesPropres.Where(p => !propresPresentes.Any(c => string.Equals(c.Nom, p, StringComparison.OrdinalIgnoreCase))))
                problemes.Add($"{r.TableV2} : la colonne propre {p} manque d'un cote (sa neutralisation ne serait pas faite)");
            tables.Add(new TableRecopiee(r, baseV2, t2, t3, cle, colonnes, propresPresentes));
        }
        foreach (var t in tables)
            foreach (var k in t.V2.Cles.Where(k => k.Colonnes.Count == 1))
                if (tables.FirstOrDefault(p => string.Equals(p.NomV2, k.TableVisee, StringComparison.OrdinalIgnoreCase)) is { } parent
                    && string.Equals(k.ColonnesVisees[0], parent.Cle.Nom, StringComparison.OrdinalIgnoreCase)
                    && t.Colonnes.Any(c => string.Equals(c.Nom, k.Colonnes[0], StringComparison.OrdinalIgnoreCase)))
                    t.Liens[k.Colonnes[0]] = parent;
        return new FichesRecopiees(baseV2, tables, problemes);
    }
}
