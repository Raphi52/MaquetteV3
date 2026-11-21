using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using RigV3.Base;

namespace RigV3.Outils.Anonymisation;

/// <summary>Le resultat d'un controle de copie. Aucune valeur des bases : des noms et des nombres.</summary>
public sealed class ResultatControle
{
    /// <summary>Colonnes personnelles controlees (designees par le catalogue dans le schema de la source).</summary>
    public int ColonnesControlees { get; set; }
    /// <summary>Valeurs reelles tirees de la source (empreintes) : valeurs entieres et mots, XML compris.</summary>
    public int EchantillonValeurs { get; set; }
    public int EchantillonMots { get; set; }
    /// <summary>Ou une valeur reelle est restee a sa place dans la copie (CA06) : « table.colonne (n) ».</summary>
    public List<string> Fuites { get; } = [];
    /// <summary>Tables a colonnes personnelles sans cle primaire d'une colonne : non tirees (au rapport).</summary>
    public List<string> TablesSansCle { get; } = [];
    /// <summary>Tables dont le nombre de lignes differe (CA03).</summary>
    public List<string> VolumesDifferents { get; } = [];
    public int TablesComparees { get; set; }
    /// <summary>Lignes tirees au hasard dont une colonne GARDEE a change (CA03).</summary>
    public List<string> NumerosChanges { get; } = [];
    public int LignesComparees { get; set; }
    /// <summary>Colonnes a vider qui ne le sont pas (CA04).</summary>
    public List<string> NonVidees { get; } = [];
    public bool Marquee { get; set; }
    public bool ChainesInvalidees { get; set; }
    /// <summary>Ce qui est sorti de la source : des nombres, des cles primaires, des noms d'attributs et des
    /// empreintes de 32 octets -- jamais une valeur.</summary>
    public HashSet<string> GenresLusSource { get; } = [];
    public bool Vert => Fuites.Count == 0 && VolumesDifferents.Count == 0 && NumerosChanges.Count == 0 && NonVidees.Count == 0 && Marquee;
}

/// <summary>
/// LE CONTROLE AUTOMATIQUE D'UNE COPIE (SOC-021 T04, CA06).
///
/// Il tire au hasard des lignes de la SOURCE qui portent des donnees personnelles, et cherche leurs valeurs
/// reelles A LEUR PLACE dans la copie (meme cle primaire, meme colonne ; dans un XML, meme attribut) :
/// valeur entiere, puis chaque mot de 5 caracteres et plus. Une valeur restee en clair y est forcement ;
/// une coincidence ne peut pas y tomber (aucun mot n'est sa propre image). Chercher « partout » ne vaut
/// rien : mesure du 2026-11-21 sur RIG_DEV, une fausse date de naissance tombe sur la vraie d'un autre, et un
/// mot de banque ou de commune GARDE exprès partage un mot avec un nom de client -- 6 faux positifs.
///
/// La comparaison se fait sur EMPREINTES SALEES calculees PAR CHAQUE SERVEUR avec la meme expression :
/// la source rend HASHBYTES('SHA2_256', sel + valeur), jamais la valeur ; le sel est tire au hasard puis
/// oublie. Les colonnes personnelles sont celles que le CATALOGUE designe dans le schema de la source, pas
/// la liste que la copie tient d'elle-meme : une colonne oubliee par la copie est controlee quand meme.
///
/// Il verifie aussi : memes volumes par table, colonnes gardees identiques sur des lignes tirees au hasard,
/// colonnes « a vider » vides, marque presente.
/// </summary>
public static class ControleCopie
{
    /// <summary>Les mots plus courts peuvent etre l'image d'un autre mot de la meme valeur : on cherche les
    /// valeurs entieres (toutes) et les mots de 5 caracteres et plus.</summary>
    public const int MotMin = 5;
    private static readonly char[] Separateurs = [' ', '-', '\'', '.', '@', ',', ';', '/', '(', ')', '"', ':', '<', '>', '_', '\\', '\t', '\r', '\n', '&', '+', '!', '?', '*', '='];

    /// <summary>Au-dela, le tirage lit des PAGES au hasard (TABLESAMPLE) au lieu de trier toute la table.</summary>
    public const long GrandeTable = 50_000;

    /// <summary>Une valeur ou un mot sans lettre ni chiffre (« - », « ##### ») reste tel quel par construction :
    /// il n'entre pas dans l'echantillon, sinon il passerait pour une valeur restee en clair.</summary>
    private const string AlphaNum = "k.v LIKE N'%[0-9A-Za-z]%'";

    public static async Task<ResultatControle> ControlerAsync(string chaineSource, string chaineCopie, string? accord,
        int echantillon = 200, CancellationToken ct = default, TextWriter? journal = null,
        IEnumerable<KeyValuePair<string, Traitement>>? remplacements = null)
    {
        BaseJetable.ExigerServeurLocal(chaineCopie);
        journal ??= TextWriter.Null;
        var chrono = System.Diagnostics.Stopwatch.StartNew();
        void Etape(string quoi) => journal.WriteLine($"  [{chrono.Elapsed.TotalSeconds,6:0.0} s] {quoi}");
        var r = new ResultatControle();
        await using var src = await PorteSource.OuvrirAsync(chaineSource, accord, ct);
        await using var copie = new SqlConnection(chaineCopie);
        await copie.OpenAsync(ct);

        // ---- la marque
        r.Marquee = Convert.ToInt32(await Scalaire(copie, $"SELECT COUNT(*) FROM sys.tables WHERE name = 'Copie' AND SCHEMA_NAME(schema_id) = '{CopieAnonymisee.SchemaMarque}'", ct)) == 1;
        if (!r.Marquee) return r;
        r.ChainesInvalidees = Convert.ToBoolean(await Scalaire(copie, $"SELECT ChainesInvalidees FROM {CopieAnonymisee.SchemaMarque}.Copie", ct));

        var tablesCopie = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new SqlCommand($"SELECT SCHEMA_NAME(schema_id) + '.' + name FROM sys.tables WHERE is_ms_shipped = 0 AND SCHEMA_NAME(schema_id) <> '{CopieAnonymisee.SchemaMarque}'", copie))
        await using (var l = await cmd.ExecuteReaderAsync(ct))
            while (await l.ReadAsync(ct)) tablesCopie.Add(l.GetString(0));

        // ---- les colonnes personnelles : le catalogue sur le schema de la SOURCE (voir l'en-tete)
        var schema = await SchemaSource.LireAsync(src, ct);
        var catalogue = new CatalogueRegles(remplacements);
        catalogue.AlignerSurLesCles(schema);
        var tables = schema.Tables.Where(t => tablesCopie.Contains(t.Libelle)).ToList();
        var regles = tables.ToDictionary(t => t, t => t.Colonnes.Where(c => !c.Calculee)
            .Select(c => (Colonne: c, Regle: catalogue.Pour(t.Nom, c.Nom, c.Type))).Where(x => x.Regle.Anonymise).ToList());
        r.ColonnesControlees = regles.Values.Sum(v => v.Count);

        // ---- volumes (CA03)
        var volumes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tables)
        {
            var nSource = Convert.ToInt64(await src.ValeurAsync($"SELECT COUNT_BIG(*) FROM {t.Complet} WITH (NOLOCK)", ct));
            r.GenresLusSource.Add("nombre");
            volumes[t.Libelle] = nSource;
            var nCopie = Convert.ToInt64(await Scalaire(copie, $"SELECT COUNT_BIG(*) FROM {t.Complet}", ct));
            r.TablesComparees++;
            if (nSource != nCopie) r.VolumesDifferents.Add($"{t.Libelle} ({nSource} dans la source, {nCopie} dans la copie)");
        }
        Etape($"volumes : {r.TablesComparees} tables");

        // ---- colonnes a vider (CA04)
        foreach (var (t, cols) in regles)
            foreach (var (c, _) in cols.Where(x => x.Regle.Traitement == Traitement.Vider))
            {
                var n = Convert.ToInt64(await Scalaire(copie, $"SELECT COUNT_BIG(*) FROM {t.Complet} WHERE {Ddl.Q(c.Nom)} IS NOT NULL AND DATALENGTH({Ddl.Q(c.Nom)}) > 0", ct));
                if (n > 0) r.NonVidees.Add($"{t.Libelle}.{c.Nom} ({n})");
            }

        // La source d'un tirage : toute la table si elle est petite, sinon des pages au hasard (~50 n lignes).
        // fix-ok: mesure du 2026-11-21 sur RIG_DEV -- trier chaque grosse table (ORDER BY NEWID()) et calculer
        // l'empreinte de chaque ligne AVANT le tirage : plus de 10 min ; ici 203 s pour les 860 tables.
        string Tirage(TableSql t)
        {
            if (!volumes.TryGetValue(t.Libelle, out var n) || n <= GrandeTable) return $"{t.Complet} WITH (NOLOCK)";
            var pourcent = Math.Min(100.0, Math.Max(0.001, 100.0 * 50 * echantillon / n));
            return $"{t.Complet} TABLESAMPLE SYSTEM ({pourcent.ToString("0.###", CultureInfo.InvariantCulture)} PERCENT) WITH (NOLOCK)";
        }
        var sel = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        SqlParameter PSel() => new("@sel", System.Data.SqlDbType.NVarChar, 64) { Value = sel };
        SqlParameter PN() => new("@n", System.Data.SqlDbType.Int) { Value = echantillon };
        SqlParameter PCles(IEnumerable<string> cles) => new("@cles", System.Data.SqlDbType.NVarChar, -1) { Value = string.Join('\u001f', cles) };
        string Decoupe(string expr) => Separateurs.Aggregate(expr, (e, c) => $"REPLACE({e}, NCHAR({(int)c}), N' ')");
        var mots = $"CROSS APPLY (SELECT 1 AS g, u.v AS v UNION ALL SELECT 2, m.value FROM STRING_SPLIT(u.d, N' ') m WHERE LEN(m.value) >= {MotMin}) k";

        // ---- valeurs reelles restees a leur place (CA06)
        foreach (var (t, cols) in regles.Where(x => x.Value.Any(c => c.Regle.Traitement != Traitement.Vider)))
        {
            var cle = schema.Index.FirstOrDefault(i => i.Table == t.Id && i.Primaire)?.Colonnes.Where(c => !c.Inclus).ToList();
            if (cle is not { Count: 1 } || cols.Any(c => c.Colonne.Nom == cle[0].Colonne))
            {
                r.TablesSansCle.Add(t.Libelle);
                continue;
            }
            var k = Ddl.Q(cle[0].Colonne);
            var fuites = new Dictionary<string, int>();

            // Colonnes (hors XML) : valeur entiere normalisee comme la base la compare, et ses mots.
            var simples = cols.Where(c => c.Regle.Traitement is not (Traitement.Vider or Traitement.Xml)).ToList();
            if (simples.Count > 0)
            {
                var exprs = simples.Select(c => CatalogueRegles.EstDate(c.Colonne.Type)
                    ? ($"CONVERT(nvarchar(30), {Ddl.Q(c.Colonne.Nom)}, 126)", "CONVERT(nvarchar(4000), NULL)")
                    : ($"UPPER(LTRIM(RTRIM(CONVERT(nvarchar(4000), {Ddl.Q(c.Colonne.Nom)}))))", Decoupe($"UPPER(CONVERT(nvarchar(4000), {Ddl.Q(c.Colonne.Nom)}))"))).ToList();
                var champs = string.Join(", ", exprs.Select((e, i) => $"{e.Item1} AS v{i}, {e.Item2} AS d{i}"));
                var valeurs = string.Join(", ", exprs.Select((_, i) => $"({i}, e.v{i}, e.d{i})"));
                var remplie = string.Join(" OR ", simples.Select(c => $"{Ddl.Q(c.Colonne.Nom)} IS NOT NULL"));
                var suite = $") e CROSS APPLY (VALUES {valeurs}) AS u(c, v, d) {mots} WHERE u.v IS NOT NULL AND {AlphaNum}";
                var source = await EmpreintesSource(src, r,
                    $"SELECT CONVERT(nvarchar(400), e.k), CONVERT(nvarchar(128), u.c), k.g, HASHBYTES('SHA2_256', @sel + k.v) FROM (SELECT TOP (@n) {k} AS k, {champs} FROM {Tirage(t)} WHERE {remplie} ORDER BY NEWID(){suite}",
                    ct, PSel(), PN());
                var dansCopie = await EmpreintesCopie(copie,
                    $"SELECT CONVERT(nvarchar(400), e.k), CONVERT(nvarchar(128), u.c), k.g, HASHBYTES('SHA2_256', @sel + k.v) FROM (SELECT {k} AS k, {champs} FROM {t.Complet} " +
                    $"WHERE CONVERT(nvarchar(400), {k}) IN (SELECT value FROM STRING_SPLIT(@cles, NCHAR(31))){suite}",
                    ct, PSel(), PCles(source.Select(s => s.Cle).Distinct()));
                Compter(source, dansCopie, (rang, _) => $"{t.Libelle}.{simples[int.Parse(rang, CultureInfo.InvariantCulture)].Colonne.Nom}", r, fuites);
            }

            // XML : chaque attribut et chaque texte, avec son nom ; seuls comptent ceux que la regle de leur
            // colonne (dans la table que nomme la ligne, AUDIT_TABLE) declare personnels.
            foreach (var (c, regle) in cols.Where(c => c.Regle.Traitement == Traitement.Xml && c.Colonne.Type == "xml"))
            {
                var table = regle.ColonneTable is null ? "CONVERT(nvarchar(128), NULL)" : $"CONVERT(nvarchar(128), {Ddl.Q(regle.ColonneTable)})";
                var suite = ") e CROSS APPLY (SELECT z.a.value('local-name(.)', 'nvarchar(128)') AS n, z.a.value('.', 'nvarchar(4000)') AS v0 FROM e.x.nodes('//@*') z(a) " +
                            "UNION ALL SELECT z.t.value('local-name(..)', 'nvarchar(128)'), z.t.value('.', 'nvarchar(4000)') FROM e.x.nodes('//text()') z(t)) a " +
                            $"CROSS APPLY (SELECT UPPER(LTRIM(RTRIM(a.v0))) AS v, {Decoupe("UPPER(a.v0)")} AS d) u {mots} WHERE {AlphaNum}";
                var source = await EmpreintesSource(src, r,
                    $"SELECT CONVERT(nvarchar(400), e.k), ISNULL(e.t, N'') + N'|' + a.n, k.g, HASHBYTES('SHA2_256', @sel + k.v) FROM (SELECT TOP (@n) {k} AS k, {table} AS t, {Ddl.Q(c.Nom)} AS x " +
                    $"FROM {Tirage(t)} WHERE {Ddl.Q(c.Nom)} IS NOT NULL ORDER BY NEWID(){suite}",
                    ct, PSel(), PN());
                var dansCopie = await EmpreintesCopie(copie,
                    $"SELECT CONVERT(nvarchar(400), e.k), ISNULL(e.t, N'') + N'|' + a.n, k.g, HASHBYTES('SHA2_256', @sel + k.v) FROM (SELECT {k} AS k, {table} AS t, {Ddl.Q(c.Nom)} AS x " +
                    $"FROM {t.Complet} WHERE CONVERT(nvarchar(400), {k}) IN (SELECT value FROM STRING_SPLIT(@cles, NCHAR(31))){suite}",
                    ct, PSel(), PCles(source.Select(s => s.Cle).Distinct()));
                Compter(source, dansCopie, (rang, _) =>
                {
                    var barre = rang.IndexOf('|');
                    var tableXml = barre > 0 ? rang[..barre].Trim() : null;
                    return catalogue.PourXml(tableXml, rang[(barre + 1)..]).Anonymise ? $"{t.Libelle}.{c.Nom}" : null;
                }, r, fuites);
            }
            foreach (var (ou, n) in fuites) r.Fuites.Add($"{ou} ({n})");
            Etape($"{t.Libelle} : {cols.Count} colonne(s) personnelle(s) tirees");
        }

        // ---- colonnes gardees identiques, sur des lignes tirees au hasard (CA03)
        foreach (var t in tables)
        {
            var cle = schema.Index.FirstOrDefault(i => i.Table == t.Id && i.Primaire)?.Colonnes.Where(c => !c.Inclus).ToList();
            if (cle is not { Count: 1 }) continue;
            var personnelles = regles[t].Select(x => x.Colonne.Nom).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (personnelles.Contains(cle[0].Colonne)) continue;
            var gardees = t.Colonnes.Where(c => !c.Calculee && !personnelles.Contains(c.Nom)
                                                && c.Type is not ("xml" or "text" or "ntext" or "image" or "sql_variant" or "geography" or "geometry" or "hierarchyid" or "timestamp" or "rowversion")).ToList();
            if (gardees.Count == 0) continue;
            var k = Ddl.Q(cle[0].Colonne);
            var ligne = "CONCAT(" + string.Join(", NCHAR(31), ", gardees.Select(g => $"ISNULL({Texte(g.Nom, g.Type)}, NCHAR(30))")) + ", N'')";
            var source = await EmpreintesSource(src, r,
                $"SELECT CONVERT(nvarchar(400), x.{k}), N'', 0, HASHBYTES('SHA2_256', @sel + {ligne}) FROM {t.Complet} x WITH (NOLOCK) " +
                $"WHERE x.{k} IN (SELECT TOP (@n) {k} FROM {Tirage(t)} ORDER BY NEWID())", ct, PSel(), PN());
            if (source.Count == 0) continue;
            var dansCopie = await EmpreintesCopie(copie,
                $"SELECT CONVERT(nvarchar(400), {k}), N'', 0, HASHBYTES('SHA2_256', @sel + {ligne}) FROM {t.Complet} " +
                $"WHERE CONVERT(nvarchar(400), {k}) IN (SELECT value FROM STRING_SPLIT(@cles, NCHAR(31)))", ct, PSel(), PCles(source.Select(s => s.Cle)));
            var differentes = source.Count(s => !dansCopie.Contains(s));
            r.LignesComparees += source.Count;
            if (differentes > 0) r.NumerosChanges.Add($"{t.Libelle} ({differentes} lignes sur {source.Count})");
        }
        Etape($"colonnes gardees : {r.LignesComparees} lignes comparees");
        return r;
    }

    /// <summary>(cle primaire, rang de la colonne ou nom de l'attribut, genre, empreinte).</summary>
    private readonly record struct Empreinte(string Cle, string Rang, int Genre, UInt128 Valeur);

    /// <summary>Les empreintes rendues par la source : elle ne rend QUE des cles, des rangs ou noms
    /// d'attributs et des empreintes de 32 octets -- tout autre contenu est compte « AUTRE » et ecarte.</summary>
    private static async Task<List<Empreinte>> EmpreintesSource(PorteSource src, ResultatControle r, string sql, CancellationToken ct, params SqlParameter[] p)
    {
        var liste = new List<Empreinte>();
        await using var l = await src.LireAsync(sql, ct, p);
        while (await l.ReadAsync(ct))
        {
            if (l.IsDBNull(3)) continue;
            if (l.GetValue(3) is not byte[] { Length: 32 } h || l.GetValue(2) is not int g)
            {
                r.GenresLusSource.Add("AUTRE");
                continue;
            }
            r.GenresLusSource.Add("cle primaire");
            r.GenresLusSource.Add("empreinte");
            if (g == 1) r.EchantillonValeurs++;
            if (g == 2) r.EchantillonMots++;
            liste.Add(new Empreinte(l.GetString(0), l.GetString(1), g, Cle(h)));
        }
        return liste;
    }

    private static async Task<HashSet<Empreinte>> EmpreintesCopie(SqlConnection copie, string sql, CancellationToken ct, params SqlParameter[] p)
    {
        var ensemble = new HashSet<Empreinte>();
        await using var cmd = new SqlCommand(sql, copie) { CommandTimeout = 0 };
        cmd.Parameters.AddRange(p);
        await using var l = await cmd.ExecuteReaderAsync(ct);
        while (await l.ReadAsync(ct))
            if (!l.IsDBNull(3)) ensemble.Add(new Empreinte(l.GetString(0), l.GetString(1), l.GetInt32(2), Cle((byte[])l[3])));
        return ensemble;
    }

    /// <summary>Une empreinte de la source retrouvee A LA MEME PLACE dans la copie = une valeur restee en clair.
    /// <paramref name="ou"/> nomme la colonne (null : valeur gardee exprès, ne compte pas).</summary>
    private static void Compter(List<Empreinte> source, HashSet<Empreinte> copie, Func<string, int, string?> ou, ResultatControle r, Dictionary<string, int> fuites)
    {
        foreach (var e in source.Where(copie.Contains))
            if (ou(e.Rang, e.Genre) is { } nom) fuites[nom] = fuites.GetValueOrDefault(nom) + 1;
    }

    private static UInt128 Cle(byte[] h) => new(BitConverter.ToUInt64(h, 0), BitConverter.ToUInt64(h, 8));

    private static async Task<object?> Scalaire(SqlConnection cn, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 0 };
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is DBNull ? null : v;
    }

    /// <summary>Une colonne en texte, de la meme facon des deux cotes (dates au format ODBC, binaires en hexadecimal).</summary>
    private static string Texte(string colonne, string type) => type switch
    {
        "datetime" or "datetime2" or "smalldatetime" or "date" or "time" or "datetimeoffset" => $"CONVERT(nvarchar(40), {Ddl.Q(colonne)}, 121)",
        "binary" or "varbinary" => $"CONVERT(nvarchar(max), {Ddl.Q(colonne)}, 2)",
        "float" or "real" => $"CONVERT(nvarchar(60), {Ddl.Q(colonne)}, 3)",
        "money" or "smallmoney" => $"CONVERT(nvarchar(40), {Ddl.Q(colonne)}, 2)",
        _ => $"CONVERT(nvarchar(max), {Ddl.Q(colonne)})",
    };
}
