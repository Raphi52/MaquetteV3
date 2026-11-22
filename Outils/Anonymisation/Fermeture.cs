using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;

namespace RigV3.Outils.Anonymisation;

/// <summary>
/// LA FERMETURE D'UNE COPIE REDUITE SUR SES CLES ETRANGERES (lot 18 du plan, REC-008 sur RIG_DEV entier).
///
/// Une copie reduite (--max-lignes N) prend les N premieres lignes de chaque table : une ligne y vise souvent une ligne
/// parente restee dans la source (au lot 8, un role de HABILIT_ROLE_USER_PROCESSUS visait une fonction absente, et la
/// reprise des droits s'arretait sur FK_RoleFonction_Fonction). La fermeture ajoute ces parents, de proche en proche :
///  - pendant la copie, chaque ligne ecrite est NOTEE avec ses valeurs SOURCE (avant anonymisation) : les colonnes qui
///    l'identifient (cle primaire, colonnes visees par une cle etrangere) et celles qui visent un parent ;
///  - puis, tour par tour, pour chaque cle etrangere entre tables copiees : les valeurs visees par les enfants copies et
///    absentes du parent copie sont relues dans la source (SELECT seul, par paquets) et la ligne parente est copiee --
///    anonymisee comme les autres ; une ligne deja copiee n'est jamais reecrite (cle primaire ou colonnes visees) ;
///  - jusqu'a un tour sans ajout. La fin est garantie : chaque tour ajoute une ligne de la source ou demande une valeur
///    jamais demandee, et les deux sont en nombre fini. Une valeur absente de la source elle-meme (cle non verifiee dans
///    la source) est demandee une fois, puis laissee : la cle reste posee sans verification, comme dans la source.
/// Les valeurs se comparent comme la base les compare (blancs finaux ignores ; majuscules et accents selon le classement
/// de la colonne visee) : le parent « ABC » sert l'enfant « abc » sous un classement CI, comme la cle elle-meme.
/// Une cle qui porte une colonne calculee (non copiee telle quelle) reste hors fermeture : posee sans verification, au rapport.
/// </summary>
internal sealed class FermetureSurLesCles
{
    /// <summary>Une cle etrangere entre deux tables copiees, en rangs des colonnes copiees de chaque table.</summary>
    internal sealed class Lien(CleEtrangere cle, TableSql enfant, TableSql parent, int[] colonnesEnfant, int[] colonnesParent, ColonneSql[] visees)
    {
        public CleEtrangere Cle { get; } = cle;
        public TableSql Enfant { get; } = enfant;
        public TableSql Parent { get; } = parent;
        public int[] ColonnesEnfant { get; } = colonnesEnfant;
        public int[] ColonnesParent { get; } = colonnesParent;
        /// <summary>Les colonnes visees, dans le parent : leur type et leur classement disent comment comparer.</summary>
        public ColonneSql[] Visees { get; } = visees;
        public string Identifiant => string.Join('|', Visees.Select(c => c.Nom));
        /// <summary>Valeurs visees par les enfants copies (cle normalisee -> valeurs source).</summary>
        public Dictionary<string, object[]> Attendues { get; } = new(StringComparer.Ordinal);
        /// <summary>Valeurs deja demandees a la source (presentes ou non) : jamais redemandees.</summary>
        public HashSet<string> Demandees { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>Ce qui identifie une ligne d'une table : la cle primaire et chaque jeu de colonnes visees.</summary>
    private sealed record Identite(string Nom, int[] Rangs, ColonneSql[] Colonnes, HashSet<string> Presentes);

    private readonly Dictionary<int, List<ColonneSql>> _copiees;
    private readonly Dictionary<int, List<Identite>> _identites = [];
    private readonly Dictionary<int, List<Lien>> _parEnfant = [];

    public List<Lien> Liens { get; } = [];
    /// <summary>Cles laissees hors fermeture (colonne calculee) : posees sans verification.</summary>
    public HashSet<string> HorsFermeture { get; } = new(StringComparer.Ordinal);

    /// <param name="colonnesCopiees">Par table copiee : ses colonnes copiees, dans l'ordre de la copie (non calculees).</param>
    public FermetureSurLesCles(SchemaSource schema, Dictionary<int, List<ColonneSql>> colonnesCopiees)
    {
        _copiees = colonnesCopiees;
        int[]? Rangs(int table, IEnumerable<string> noms)
        {
            var cols = _copiees[table];
            var rangs = noms.Select(n => cols.FindIndex(c => string.Equals(c.Nom, n, StringComparison.OrdinalIgnoreCase))).ToArray();
            return rangs.Any(r => r < 0) ? null : rangs;
        }
        foreach (var t in _copiees.Keys)
        {
            _identites[t] = [];
            _parEnfant[t] = [];
            var pk = schema.Index.FirstOrDefault(i => i.Table == t && i.Primaire)?.Colonnes.Where(c => !c.Inclus).Select(c => c.Colonne).ToList();
            if (pk is { Count: > 0 } && Rangs(t, pk) is { } r) AjouterIdentite(t, r);
        }
        foreach (var k in schema.Cles.Where(k => _copiees.ContainsKey(k.Table) && _copiees.ContainsKey(k.Reference)))
        {
            var enfant = Rangs(k.Table, k.Colonnes.Select(c => c.Colonne));
            var parent = Rangs(k.Reference, k.Colonnes.Select(c => c.Referencee));
            var tEnfant = schema.Tables.First(t => t.Id == k.Table);
            if (enfant is null || parent is null)
            {
                HorsFermeture.Add($"{tEnfant.Libelle}.{k.Nom}");
                continue;
            }
            var lien = new Lien(k, tEnfant, schema.Tables.First(t => t.Id == k.Reference), enfant, parent,
                                [.. parent.Select(i => _copiees[k.Reference][i])]);
            Liens.Add(lien);
            _parEnfant[k.Table].Add(lien);
            AjouterIdentite(k.Reference, parent);
        }
    }

    private void AjouterIdentite(int table, int[] rangs)
    {
        var cols = rangs.Select(i => _copiees[table][i]).ToArray();
        var nom = string.Join('|', cols.Select(c => c.Nom));
        if (_identites[table].All(i => !string.Equals(i.Nom, nom, StringComparison.OrdinalIgnoreCase)))
            _identites[table].Add(new Identite(nom, rangs, cols, new HashSet<string>(StringComparer.Ordinal)));
    }

    /// <summary>La ligne (valeurs SOURCE) est-elle deja dans la copie, par une des colonnes qui l'identifient ?</summary>
    public bool DejaCopiee(int table, object[] valeurs)
        => _identites[table].Any(i => Normaliser(valeurs, i.Rangs, i.Colonnes) is { } k && i.Presentes.Contains(k));

    /// <summary>Note une ligne ecrite dans la copie, par ses valeurs SOURCE.</summary>
    public void Noter(int table, object[] valeurs)
    {
        foreach (var i in _identites[table])
            if (Normaliser(valeurs, i.Rangs, i.Colonnes) is { } k) i.Presentes.Add(k);
        foreach (var l in _parEnfant[table])
            // Une cle dont une colonne est NULL n'est pas verifiee par SQL Server : elle ne demande pas de parent.
            if (Normaliser(valeurs, l.ColonnesEnfant, l.Visees) is { } k && !l.Attendues.ContainsKey(k))
                l.Attendues[k] = [.. l.ColonnesEnfant.Select(c => valeurs[c])];
    }

    /// <summary>Les valeurs visees d'un lien absentes du parent copie et jamais demandees : marquees demandees.</summary>
    public List<object[]> AManquer(Lien l)
    {
        var presentes = _identites[l.Parent.Id].First(i => string.Equals(i.Nom, l.Identifiant, StringComparison.OrdinalIgnoreCase)).Presentes;
        var manquantes = new List<object[]>();
        foreach (var (k, v) in l.Attendues)
            if (!presentes.Contains(k) && l.Demandees.Add(k)) manquantes.Add(v);
        return manquantes;
    }

    /// <summary>
    /// Les lectures qui ramenent les lignes parentes de <paramref name="manquantes"/> : un SELECT seul par paquet (au plus
    /// 2 000 parametres, la limite du serveur est 2 100), parametres au type de la colonne visee (l'index de la source sert).
    /// </summary>
    public static IEnumerable<(string Sql, SqlParameter[] Parametres)> Lectures(Lien l, List<ColonneSql> colonnesParent, List<object[]> manquantes)
    {
        var select = $"SELECT {string.Join(", ", colonnesParent.Select(c => Ddl.Q(c.Nom)))} FROM {l.Parent.Complet} WITH (NOLOCK) WHERE ";
        var parPaquet = Math.Max(1, 2000 / l.Visees.Length);
        foreach (var paquet in manquantes.Chunk(parPaquet))
        {
            var p = new List<SqlParameter>();
            string Param(int colonne, object valeur)
            {
                var nom = $"@p{p.Count}";
                p.Add(Parametre(nom, l.Visees[colonne], valeur));
                return nom;
            }
            var filtre = l.Visees.Length == 1
                ? $"{Ddl.Q(l.Visees[0].Nom)} IN ({string.Join(", ", paquet.Select(v => Param(0, v[0])))})"
                : string.Join(" OR ", paquet.Select(v => "(" + string.Join(" AND ", l.Visees.Select((c, i) => $"{Ddl.Q(c.Nom)} = {Param(i, v[i])}")) + ")"));
            yield return (select + filtre, [.. p]);
        }
    }

    private static SqlParameter Parametre(string nom, ColonneSql c, object valeur) => c.Type switch
    {
        "varchar" or "char" => new SqlParameter(nom, SqlDbType.VarChar, c.Longueur) { Value = valeur },
        "nvarchar" or "nchar" => new SqlParameter(nom, SqlDbType.NVarChar, c.Longueur < 0 ? -1 : c.Longueur / 2) { Value = valeur },
        _ => new SqlParameter(nom, valeur),
    };

    /// <summary>
    /// La valeur comparable d'un jeu de colonnes, comme SQL Server compare : blancs finaux ignores, majuscules (CI) et accents
    /// (AI) selon le classement de la colonne. Null si une colonne est NULL (la cle n'est alors pas verifiee).
    /// </summary>
    internal static string? Normaliser(object[] valeurs, int[] rangs, ColonneSql[] colonnes)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < rangs.Length; i++)
        {
            var v = valeurs[rangs[i]];
            if (v is DBNull or null) return null;
            if (i > 0) sb.Append('\u001f');
            sb.Append(v switch
            {
                string s => Texte(s, colonnes[i].Collation),
                byte[] b => Convert.ToHexString(b),
                decimal d => (d / 1.000000000000000000000000000000000m).ToString(CultureInfo.InvariantCulture),
                DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
                _ => Convert.ToString(v, CultureInfo.InvariantCulture),
            });
        }
        return sb.ToString();
    }

    private static string Texte(string s, string? collation)
    {
        s = s.TrimEnd(' ');
        if (collation is null) return s;
        if (collation.Contains("_AI", StringComparison.OrdinalIgnoreCase))
            s = new string([.. s.Normalize(NormalizationForm.FormD).Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)]);
        return collation.Contains("_CI", StringComparison.OrdinalIgnoreCase) ? s.ToUpperInvariant() : s;
    }
}
