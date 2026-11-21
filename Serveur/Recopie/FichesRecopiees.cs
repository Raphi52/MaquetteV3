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
                           IReadOnlyList<ColonneV2> propres, ColonneV2? cleV3 = null, IReadOnlyDictionary<string, ColonneV2>? colonnesV3 = null)
    {
        Regle = regle; BaseV2 = baseV2; V2 = v2; V3 = v3; Cle = cle; Colonnes = colonnes; Propres = propres;
        CleV3 = cleV3 ?? v3.Colonne(cle.Nom);
        this.colonnesV3 = colonnesV3 ?? colonnes.Concat(propres).ToDictionary(c => c.Nom, c => v3.Colonne(c.Nom), StringComparer.OrdinalIgnoreCase);
        traductions = (regle.Correspondance ?? []).Where(m => m.Traduction is not null)
                                                   .ToDictionary(m => m.V2, m => m.Traduction!, StringComparer.OrdinalIgnoreCase);
    }

    private readonly IReadOnlyDictionary<string, ColonneV2> colonnesV3;
    private readonly Dictionary<string, Traduction> traductions;

    public RegleRecopie Regle { get; }
    public string BaseV2 { get; }
    public TableV2 V2 { get; }
    public TableV2 V3 { get; }
    /// <summary>La cle primaire de la V2 (une colonne). C'est sous les NOMS DE LA V2 que la recopie lit, compare et ecrit une
    /// fiche (LigneJson) : une table V3 colonne par colonne est lue « AS » ses noms V2 (<see cref="SelectJsonV3"/>).</summary>
    public ColonneV2 Cle { get; }
    /// <summary>La cle primaire de la table V3 (la meme colonne pour une table telle quelle ; « Id » pour rec.Client).</summary>
    public ColonneV2 CleV3 { get; }
    /// <summary>Les colonnes recopiees dans les deux sens, sous leur nom et leur type V2 (<see cref="ColonneV3"/> : leur pendant).</summary>
    public IReadOnlyList<ColonneV2> Colonnes { get; }
    /// <summary>Les colonnes propres a chaque logiciel (<see cref="RegleRecopie.ColonnesPropres"/>) : presentes des deux cotes
    /// pour une table telle quelle, dans la V2 seule pour une fiche colonne par colonne (<see cref="PropresV3"/> est vide).</summary>
    public IReadOnlyList<ColonneV2> Propres { get; }
    /// <summary>Les colonnes propres que la table V3 porte aussi (une table telle quelle seulement).</summary>
    public IReadOnlyList<ColonneV2> PropresV3 => Regle.ColonneParColonne ? [] : Propres;
    /// <summary>Les liens vers une autre table partagee : colonne de cette table -> table visee (par sa cle).</summary>
    public Dictionary<string, TableRecopiee> Liens { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string NomV2 => Regle.TableV2;
    public string NomV3 => Regle.TableV3;
    /// <summary>La table V2 vue depuis la base V3, sur la meme instance (Q4) : « [RIG_X].[dbo].[CLIENT] ».</summary>
    public string NomV2Sql => $"{Sql.Q(BaseV2)}.{V2.NomSql}";
    public string NomV3Sql => V3.NomSql;
    public bool CleIdentiteV2 => V2.Colonne(Cle.Nom).Identite;
    public bool CleIdentiteV3 => CleV3.Identite;

    /// <summary>La colonne de la table V3 d'une colonne recopiee (nom V2) : elle-meme pour une table telle quelle.</summary>
    public ColonneV2 ColonneV3(ColonneV2 c) => colonnesV3[c.Nom];

    /// <summary>La traduction d'une colonne (nom V2), ou null : sa valeur passe telle quelle (ou par un lien, <see cref="Liens"/>).</summary>
    public Traduction? TraductionDe(string nomV2) => traductions.GetValueOrDefault(nomV2);

    /// <summary>Ce que le declencheur de la V3 peut lire dans inserted / deleted : ni text, ni ntext, ni image (SQL Server
    /// les refuse dans un declencheur AFTER). Une colonne de ces types est recopiee, mais sans etat « avant ».</summary>
    public IEnumerable<ColonneV2> ColonnesDeclencheur => Colonnes.Where(c => !Grand(c));

    public static bool Grand(ColonneV2 c) => c.TypeBase is "text" or "ntext" or "image";

    /// <summary>La cle d'une valeur texte : CONVERT vers son type (l'index de la cle sert).</summary>
    public string CleEgale(string alias, string parametre) => $"{alias}.{Sql.Q(Cle.Nom)} = CONVERT({Cle.TypeSql}, {parametre})";

    /// <summary>La meme chose dans la table V3, par sa cle.</summary>
    public string CleEgaleV3(string alias, string parametre) => $"{alias}.{Sql.Q(CleV3.Nom)} = CONVERT({CleV3.TypeSql}, {parametre})";

    /// <summary>« SELECT (SELECT ... FOR JSON) » : la ligne de cle <paramref name="parametre"/> en un seul texte (null si absente).</summary>
    public string SelectJson(string table, IEnumerable<ColonneV2> colonnes, string parametre, string? indications = null)
        => $"SELECT (SELECT {string.Join(", ", colonnes.Select(c => $"x.{Sql.Q(c.Nom)} AS {Sql.Q(c.Nom)}"))} FROM {table} AS x " +
           $"{indications} WHERE {CleEgale("x", parametre)} FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);";

    /// <summary>La ligne de la table V3, chaque colonne lue SOUS SON NOM V2 (« x.[Nom] AS [CLIEN_NOM] »).</summary>
    public string SelectJsonV3(IEnumerable<ColonneV2> colonnes, string parametre, string? indications = null)
        => $"SELECT (SELECT {string.Join(", ", colonnes.Select(c => $"x.{Sql.Q(ColonneV3(c).Nom)} AS {Sql.Q(c.Nom)}"))} FROM {NomV3Sql} AS x " +
           $"{indications} WHERE {CleEgaleV3("x", parametre)} FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);";

    /// <summary>La clause WITH d'un OPENJSON qui relit ces colonnes avec leur type.</summary>
    public static string OpenJson(IEnumerable<ColonneV2> colonnes)
        => "WITH (" + string.Join(", ", colonnes.Select(c => $"{Sql.Q(c.Nom)} {TypeLu(c)} '$.\"{c.Nom}\"'")) + ")";

    /// <summary>La meme, pour ecrire la table V3 : chaque colonne (nom V2 dans le JSON) avec le type de sa colonne V3 (une
    /// colonne traduite peut en avoir un autre).</summary>
    public string OpenJsonV3(IEnumerable<ColonneV2> colonnes)
        => "WITH (" + string.Join(", ", colonnes.Select(c => $"{Sql.Q(c.Nom)} {TypeLu(ColonneV3(c))} '$.\"{c.Nom}\"'")) + ")";

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
        CatalogueV2 v2, v3;
        var problemes = new List<string>();
        await using (var cn = new SqlConnection(ConnexionV2(connexionV3, baseV2)))
        {
            await cn.OpenAsync(ct);
            v2 = await CatalogueV2.LireAsync(cn, ct);
        }
        await using (var cn = new SqlConnection(connexionV3))
        {
            await cn.OpenAsync(ct);
            v3 = await CatalogueV2.LireAsync(cn, ct);
            if (regles is null)
            {
                var (enService, absentes, _) = await ReglesEnServiceAsync(cn, baseV2, ct);
                regles = enService;
                problemes.AddRange(absentes);
            }
        }
        var f = Construire(baseV2, v2, v3, regles);
        return problemes.Count == 0 ? f : new FichesRecopiees(baseV2, [.. f.Tables], [.. problemes, .. f.Problemes]);
    }

    /// <summary>
    /// Les regles avec lesquelles une base est recopiee : celles de sa MISE EN SERVICE (rec.FicheEnService), pas celles que le
    /// serveur declare aujourd'hui -- une regle ajoutee au serveur n'entre dans la recopie d'un greffe qu'a sa prochaine mise
    /// en service, qui verifie que sa table V2 est suivie et pose son declencheur. Une table mise en service telle quelle
    /// (v2.X) garde sa forme telle quelle (<see cref="RegleRecopie.EnTelleQuelle"/>) meme si sa fiche a depuis sa table.
    /// Avant toute mise en service : les regles du serveur.
    /// </summary>
    internal static async Task<(IReadOnlyList<RegleRecopie> Regles, IReadOnlyList<string> Problemes, bool EnService)> ReglesEnServiceAsync(
        SqlConnection v3, string baseV2, CancellationToken ct)
    {
        var serveur = ReglesRecopie.Decouvrir();
        var posees = new List<(string TableV2, string TableV3)>();
        await using (var cmd = new SqlCommand("IF OBJECT_ID(N'rec.FicheEnService') IS NOT NULL SELECT TableV2, TableV3 FROM rec.FicheEnService WHERE BaseV2 = @b;", v3))
        {
            cmd.Parameters.AddWithValue("@b", baseV2);
            await using var l = await cmd.ExecuteReaderAsync(ct);
            while (await l.ReadAsync(ct)) posees.Add((l.GetString(0), l.GetString(1)));
        }
        if (posees.Count == 0) return (serveur, [], false);
        var regles = new List<RegleRecopie>();
        var problemes = new List<string>();
        foreach (var (tableV2, tableV3) in posees)
        {
            var r = serveur.FirstOrDefault(x => string.Equals(x.TableV2, tableV2, StringComparison.OrdinalIgnoreCase));
            if (r is not null && string.Equals(r.TableV3, tableV3, StringComparison.OrdinalIgnoreCase)) regles.Add(r);
            else if (r is not null && string.Equals(tableV3, "v2." + tableV2, StringComparison.OrdinalIgnoreCase)) regles.Add(r.EnTelleQuelle());
            else problemes.Add($"{tableV2} : mise en service vers {tableV3}, mais le serveur n'a plus de regle pour cette table V3 " +
                               $"({(r is null ? "aucune regle" : "sa regle vise " + r.TableV3)}) : refaire la mise en service du greffe");
        }
        return (regles, problemes, true);
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
            if (r.ColonneParColonne)
            {
                if (ColonneParColonne(r, baseV2, t2, t3, problemes) is { } fiche) tables.Add(fiche);
                continue;
            }
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
        {
            // fix-ok: une colonne traduite n'est jamais un lien -- mesure par RecopieFichesCheck (REC-013 CA02 rouge, 4e essai) :
            // depuis que les comptes sont recopies (REC-017), CLIEN_ID_UTLST -> UTILISATEUR est aussi une cle vers une fiche
            // partagee ; la valeur etait traduite deux fois (par la correspondance, puis par le compte de session), et
            // l'agent V3 243 « azuredevops » devenait l'agent V2 50 « Moulinette ».
            // Une colonne qui garde le numero de la V2 dans la copie de la V3 (RegleRecopie.NumerosV2) n'est pas un lien non plus.
            foreach (var k in t.V2.Cles.Where(k => k.Colonnes.Count == 1 && t.TraductionDe(k.Colonnes[0]) is null
                                                   && !t.Regle.NumerosV2.Keys.Contains(k.Colonnes[0], StringComparer.OrdinalIgnoreCase)))
                if (tables.FirstOrDefault(p => string.Equals(p.NomV2, k.TableVisee, StringComparison.OrdinalIgnoreCase)) is { } parent
                    && string.Equals(k.ColonnesVisees[0], parent.Cle.Nom, StringComparison.OrdinalIgnoreCase)
                    && t.Colonnes.Any(c => string.Equals(c.Nom, k.Colonnes[0], StringComparison.OrdinalIgnoreCase)))
                    t.Liens[k.Colonnes[0]] = parent;
            // Les liens qu'une base V2 peut tenir sans cle etrangere, declares par la regle (CLIAD_ID_ADRBS -> ADRESSE_BASE :
            // sans cle sur RIG_DEV, avec cle sur RIG_RECETTE, ou la boucle ci-dessus pose deja le meme lien).
            foreach (var (colonne, visee) in t.Regle.LiensSansCle)
                if (tables.FirstOrDefault(p => string.Equals(p.NomV2, visee, StringComparison.OrdinalIgnoreCase)) is { } parent
                    && t.Colonnes.Any(c => string.Equals(c.Nom, colonne, StringComparison.OrdinalIgnoreCase)))
                    t.Liens[colonne] = parent;
        }
        return new FichesRecopiees(baseV2, tables, problemes);
    }

    /// <summary>
    /// Une fiche qui a sa table dans la V3 (REC-012 a REC-019) : chaque colonne de sa correspondance doit exister des deux
    /// cotes, au meme type (sauf traduite) ; la cle est celle de chaque table (une colonne, meme type). Les colonnes propres
    /// sont celles de la V2 seule (etat de transmission) : la V3 n'en a pas, elle ne les lit ni ne les ecrit.
    /// </summary>
    private static TableRecopiee? ColonneParColonne(RegleRecopie r, string baseV2, TableV2 t2, TableV2 t3, List<string> problemes)
    {
        var cleV3Nom = r.CleV3 ?? (t2.ClePrimaire.Count == 1 ? t2.ClePrimaire[0] : "");
        if (t2.ClePrimaire.Count != 1 || t3.ClePrimaire.Count != 1 || !string.Equals(t3.ClePrimaire[0], cleV3Nom, StringComparison.OrdinalIgnoreCase))
        {
            problemes.Add($"{r.TableV2} : la recopie demande une cle primaire d'une colonne de chaque cote (V2 : {string.Join("+", t2.ClePrimaire)} ; " +
                          $"{r.TableV3} : {string.Join("+", t3.ClePrimaire)}, attendue {cleV3Nom})");
            return null;
        }
        var cle = t2.Colonne(t2.ClePrimaire[0]);
        var cleV3 = t3.Colonne(t3.ClePrimaire[0]);
        if (cle.TypeSql != cleV3.TypeSql) { problemes.Add($"{r.TableV2} : la cle {cle.Nom} et {r.TableV3}.{cleV3.Nom} n'ont pas le meme type"); return null; }
        var colonnes = new List<ColonneV2>();
        var v3 = new Dictionary<string, ColonneV2>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in r.Correspondance!)
        {
            var c2 = t2.Colonnes.FirstOrDefault(c => string.Equals(c.Nom, m.V2, StringComparison.OrdinalIgnoreCase));
            var c3 = t3.Colonnes.FirstOrDefault(c => string.Equals(c.Nom, m.V3, StringComparison.OrdinalIgnoreCase));
            // Deux bases du meme logiciel n'ont pas le meme schema : une colonne absente de CETTE base V2 n'est pas recopiee.
            if (c2 is null) continue;
            if (c3 is null) { problemes.Add($"{r.TableV2}.{m.V2} : sa colonne V3 {r.TableV3}.{m.V3} n'existe pas (migration a jouer)"); continue; }
            if (m.Traduction is null && c2.TypeSql != c3.TypeSql)
            {
                problemes.Add($"{r.TableV2}.{m.V2} ({c2.TypeSql}) et {r.TableV3}.{m.V3} ({c3.TypeSql}) n'ont pas le meme type");
                continue;
            }
            if (c2.Calculee || c2.TypeBase == "timestamp" || c2.Identite || c2.TypeAssemblage || c2.TypeBase is "xml" or "sql_variant") continue;
            colonnes.Add(c2);
            v3[c2.Nom] = c3;
        }
        var propres = t2.Colonnes.Where(c => r.ColonnesPropres.Contains(c.Nom, StringComparer.OrdinalIgnoreCase)).ToList();
        foreach (var p in r.ColonnesPropres.Where(p => !propres.Any(c => string.Equals(c.Nom, p, StringComparison.OrdinalIgnoreCase))))
            problemes.Add($"{r.TableV2} : la colonne propre {p} manque dans la V2 (sa neutralisation ne serait pas faite)");
        return new TableRecopiee(r, baseV2, t2, t3, cle, colonnes, propres, cleV3, v3);
    }
}
