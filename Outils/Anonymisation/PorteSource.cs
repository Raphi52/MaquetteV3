using Microsoft.Data.SqlClient;
using RigV3.Base;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Outils.Anonymisation;

/// <summary>Un refus de l'outil (garde-fou) : message pour l'exploitant, code de sortie 1, rien d'ecrit.</summary>
public sealed class RefusAnonymisation(string message) : Exception(message);

/// <summary>
/// LA SOURCE, EN LECTURE SEULE (SOC-021 CA07, T05).
///  - Hors de ce poste, la base ne s'ouvre qu'avec l'accord donne POUR ELLE (--accord NOM_DE_LA_BASE).
///  - La connexion est ouverte en ApplicationIntent=ReadOnly.
///  - Chaque requete passe par <see cref="LectureV2.ExigerLecture"/> (la porte de la reprise : un SELECT
///    seul, sans INTO, sans point-virgule) ; le nombre de requetes est note au rapport.
/// </summary>
public sealed class PorteSource : IAsyncDisposable
{
    private readonly SqlConnection _cn;

    public string Serveur { get; }
    public string Base { get; }
    /// <summary>Requetes jouees sur la source, toutes passees par le garde-fou.</summary>
    public int Requetes { get; private set; }

    private PorteSource(SqlConnection cn, string serveur, string nomBase)
    {
        _cn = cn;
        Serveur = serveur;
        Base = nomBase;
    }

    /// <summary>
    /// Verifie la chaine (base nommee, accord hors poste) SANS se connecter : un refus ne touche
    /// jamais le serveur. Rend la chaine forcee en lecture seule.
    /// </summary>
    public static SqlConnectionStringBuilder Verifier(string chaine, string? accord)
    {
        SqlConnectionStringBuilder b;
        try { b = new SqlConnectionStringBuilder(chaine); }
        catch (ArgumentException e) { throw new RefusAnonymisation($"Chaine de la source illisible : {e.Message}"); }
        if (string.IsNullOrWhiteSpace(b.InitialCatalog))
            throw new RefusAnonymisation("La chaine de la source doit nommer sa base (Database=…).");
        if (!EstSurCePoste(chaine) && !string.Equals(accord?.Trim(), b.InitialCatalog, StringComparison.OrdinalIgnoreCase))
            throw new RefusAnonymisation(
                $"Source « {b.DataSource} / {b.InitialCatalog} » hors de ce poste : elle ne se lit qu'avec l'accord donne pour " +
                $"CETTE base, --accord {b.InitialCatalog} (SOC-021 CA07)" +
                (string.IsNullOrWhiteSpace(accord) ? "." : $" ; l'accord donne vise « {accord} »."));
        b.ApplicationIntent = ApplicationIntent.ReadOnly;
        return b;
    }

    /// <summary>La source est-elle sur ce poste (le SQL Server de docker-compose.yml) ?</summary>
    public static bool EstSurCePoste(string chaine) => BaseJetable.EstServeurLocal(chaine);

    public static async Task<PorteSource> OuvrirAsync(string chaine, string? accord, CancellationToken ct)
    {
        var b = Verifier(chaine, accord);
        var cn = new SqlConnection(b.ConnectionString);
        await cn.OpenAsync(ct);
        return new PorteSource(cn, b.DataSource, b.InitialCatalog);
    }

    /// <summary>Un lecteur sur une requete de lecture (refusee si elle n'est pas un SELECT seul).</summary>
    public async Task<SqlDataReader> LireAsync(string sql, CancellationToken ct, params SqlParameter[] parametres)
    {
        LectureV2.ExigerLecture(sql);
        Requetes++;
        // Pas de delai : une table de production se lit en continu, parfois longtemps.
        var cmd = new SqlCommand(sql, _cn) { CommandTimeout = 0 };
        cmd.Parameters.AddRange(parametres);
        return await cmd.ExecuteReaderAsync(ct);
    }

    /// <summary>Toutes les lignes d'une lecture (petits resultats : schema, comptes).</summary>
    public async Task<List<object?[]>> LignesAsync(string sql, CancellationToken ct, params SqlParameter[] parametres)
    {
        var lignes = new List<object?[]>();
        await using var l = await LireAsync(sql, ct, parametres);
        while (await l.ReadAsync(ct))
        {
            var v = new object?[l.FieldCount];
            l.GetValues(v!);
            for (var i = 0; i < v.Length; i++) if (v[i] is DBNull) v[i] = null;
            lignes.Add(v);
        }
        return lignes;
    }

    public async Task<object?> ValeurAsync(string sql, CancellationToken ct, params SqlParameter[] parametres)
    {
        var l = await LignesAsync(sql, ct, parametres);
        return l.Count == 0 ? null : l[0][0];
    }

    /// <summary>
    /// Les ecritures recensees par le serveur sur la base source (sys.dm_db_index_usage_stats,
    /// user_updates) : lues avant et apres la copie (SOC-021 T05). Null si le compte n'a pas le droit
    /// VIEW SERVER STATE -- le rapport le dit au lieu d'inventer un zero.
    /// </summary>
    public async Task<long?> EcrituresServeurAsync(CancellationToken ct)
    {
        try
        {
            return Convert.ToInt64(await ValeurAsync(
                "SELECT ISNULL(SUM(s.user_updates), 0) FROM sys.dm_db_index_usage_stats s WHERE s.database_id = DB_ID()", ct));
        }
        catch (SqlException) { return null; }
    }

    public ValueTask DisposeAsync() => _cn.DisposeAsync();
}
