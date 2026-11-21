using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Recopie;

/// <summary>
/// L'ETAT FIGE DATE D'UNE BASE V2 (REC-001 CA05, T02) : un instantane de base de SQL Server
/// (CREATE DATABASE ... AS SNAPSHOT OF), pris au debut du transfert et supprime a la fin.
///
/// Pourquoi lui : la V2 ne s'arrete jamais (Q15) et RIG_DEV n'a pas l'isolement par instantane (lu le 2026-11-21 :
/// ALLOW_SNAPSHOT_ISOLATION OFF, seul READ_COMMITTED_SNAPSHOT ON, qui fige une requete et non toute une nuit de
/// lecture). L'instantane fige TOUTE la base a une date sans poser un verrou sur ses tables ni changer un de ses
/// reglages ; il est en lecture seule par nature (une ecriture y est refusee par SQL Server) ; la version du suivi
/// des changements lue dedans dit a la recopie continue (REC-003) ou reprendre. Prouve sur le SQL Server du poste
/// (2022, Linux) : un INSERT dans la base apres l'instantane n'y est pas, CHANGETABLE depuis sa version le rend.
///
/// Ce qu'il demande : le droit CREATE DATABASE sur le serveur de la V2 et la place des pages modifiees pendant la
/// nuit (fichiers .ss a cote de ceux de la base). Disponible dans toutes les editions depuis SQL Server 2016 SP1.
/// C'est le seul geste du transfert sur le serveur de la V2 ; la base V2 elle-meme n'est jamais ecrite (CA06).
/// </summary>
public sealed class Instantane : IAsyncDisposable
{
    public string Nom { get; }
    public string BaseV2 { get; }
    /// <summary>Date de l'instantane (sys.databases.create_date) : tout ce qui est ecrit apres n'est pas transfere.</summary>
    public DateTime Le { get; }
    /// <summary>CHANGE_TRACKING_CURRENT_VERSION() dans l'instantane ; null sans suivi des changements sur la base V2.</summary>
    public long? VersionSuivi { get; }
    /// <summary>Chaine de connexion a l'instantane, en intention lecture seule.</summary>
    public string Connexion { get; }

    private readonly string Master;
    private bool Supprime;

    private Instantane(string nom, string baseV2, DateTime le, long? version, string connexion, string master)
        => (Nom, BaseV2, Le, VersionSuivi, Connexion, Master) = (nom, baseV2, le, version, connexion, master);

    public static async Task<Instantane> CreerAsync(string connexionV2, CancellationToken ct)
    {
        var b = new SqlConnectionStringBuilder(connexionV2);
        var baseV2 = b.InitialCatalog;
        if (string.IsNullOrWhiteSpace(baseV2)) throw new InvalidOperationException("La chaine de la base V2 ne nomme pas sa base (Database=...).");
        var master = new SqlConnectionStringBuilder(connexionV2) { InitialCatalog = "master", ApplicationIntent = ApplicationIntent.ReadWrite }.ConnectionString;
        var nom = $"{baseV2}_rigv3_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        if (nom.Length > 128) nom = nom[^128..];

        await using var cn = new SqlConnection(master);
        await cn.OpenAsync(ct);
        var fichiers = new List<(string Logique, string Physique)>();
        await using (var cmd = new SqlCommand("SELECT name, physical_name FROM sys.master_files WHERE database_id = DB_ID(@b) AND type = 0 ORDER BY file_id;", cn))
        {
            cmd.Parameters.AddWithValue("@b", baseV2);
            await using var l = await cmd.ExecuteReaderAsync(ct);
            while (await l.ReadAsync(ct)) fichiers.Add((l.GetString(0), l.GetString(1)));
        }
        if (fichiers.Count == 0) throw new InvalidOperationException($"Base V2 « {baseV2} » introuvable sur le serveur {b.DataSource}.");

        var specs = fichiers.Select(f =>
        {
            var dossier = f.Physique[..(f.Physique.LastIndexOfAny(['/', '\\']) + 1)];
            return $"(NAME = {Sql.Q(f.Logique)}, FILENAME = {Sql.Texte($"{dossier}{nom}_{f.Logique}.ss")})";
        });
        await using (var cmd = new SqlCommand($"CREATE DATABASE {Sql.Q(nom)} ON {string.Join(", ", specs)} AS SNAPSHOT OF {Sql.Q(baseV2)};", cn) { CommandTimeout = 600 })
            await cmd.ExecuteNonQueryAsync(ct);

        DateTime le;
        await using (var cmd = new SqlCommand("SELECT create_date FROM sys.databases WHERE name = @n;", cn))
        {
            cmd.Parameters.AddWithValue("@n", nom);
            le = (DateTime)(await cmd.ExecuteScalarAsync(ct))!;
        }
        var connexion = new SqlConnectionStringBuilder(connexionV2) { InitialCatalog = nom, ApplicationIntent = ApplicationIntent.ReadOnly }.ConnectionString;
        long? version;
        await using (var lecture = new SqlConnection(connexion))
        {
            await lecture.OpenAsync(ct);
            await using var cmd = new SqlCommand("SELECT CHANGE_TRACKING_CURRENT_VERSION();", lecture);
            version = await cmd.ExecuteScalarAsync(ct) is long v ? v : null;
        }
        return new Instantane(nom, baseV2, le, version, connexion, master);
    }

    /// <summary>Supprime l'instantane (la base V2 n'en est pas touchee).</summary>
    public async ValueTask DisposeAsync()
    {
        if (Supprime) return;
        Supprime = true;
        using (var cn = new SqlConnection(Connexion)) SqlConnection.ClearPool(cn);
        await using var master = new SqlConnection(Master);
        await master.OpenAsync();
        await using var cmd = new SqlCommand($"IF DB_ID({Sql.Texte(Nom)}) IS NOT NULL DROP DATABASE {Sql.Q(Nom)};", master);
        await cmd.ExecuteNonQueryAsync();
    }
}
