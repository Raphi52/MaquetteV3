using Microsoft.Data.SqlClient;

namespace RigV3.Base;

// ============================================================
// LA BASE JETABLE D'UN BANC (lot 1 du plan de reconstruction, 2026-11-20).
//
//   await using var b = await BaseJetable.CreerAsync("SocleIdentiteCheck");
//   await Migrateur.AppliquerAsync(b.Connexion, dossierMigrations);
//   ... le banc travaille sur b.Connexion ...
//   // fin du bloc : la base est supprimee
//
// Chaque banc a SA base, nommee par lui et par l'instant : plusieurs lots qui
// lancent leurs bancs en meme temps sur le meme serveur ne se voient pas.
//
// GARDE-FOU. Les bancs ecrivent : ils ne visent que le serveur LOCAL
// (docker-compose.yml du depot, ou un SQL Server installe sur ce poste).
// Toute autre adresse -- SQL-DEV, SQL-PROD, un serveur de greffe -- est
// refusee AVANT la connexion. Ce n'est pas une politesse : la regle commune
// du plan est « jamais d'ecriture sur SQL-DEV ni SQL-PROD », et c'est ce code
// qui la tient, pas la memoire de chaque lot.
// ============================================================

public sealed class BaseJetable : IAsyncDisposable
{
    /// <summary>Variable d'environnement : chaine de connexion au serveur des bancs
    /// (sans base). Absente : <see cref="ServeurDeveloppement"/>.</summary>
    public const string VariableServeur = "RIGV3_SQL";

    /// <summary>Le SQL Server de docker-compose.yml avec ses valeurs par defaut
    /// (port 14330, mot de passe RIGV3_SQL_MDP par defaut) : les deux fichiers
    /// se changent ensemble.</summary>
    public const string ServeurDeveloppement =
        "Server=127.0.0.1,14330;User ID=sa;Password=RigV3_dev_2026!;Encrypt=True;TrustServerCertificate=True";

    /// <summary>Collation des bases V3 : celle de RIG_DEV (lue le 2026-11-20 sur SQL-DEV\DEV),
    /// pour que la recopie V2 &lt;-&gt; V3 compare les textes comme la V2.</summary>
    public const string Collation = "SQL_Latin1_General_CP1_CI_AS";

    /// <summary>Prefixe de toutes les bases jetables : on les reconnait d'un coup d'oeil.</summary>
    public const string Prefixe = "rigv3_banc_";

    /// <summary>Nom de la base creee.</summary>
    public string Nom { get; }

    /// <summary>Chaine de connexion A CETTE BASE.</summary>
    public string Connexion { get; }

    private readonly string Serveur;
    private bool Supprimee;

    private BaseJetable(string serveur, string nom)
    {
        Serveur = serveur;
        Nom = nom;
        Connexion = new SqlConnectionStringBuilder(serveur) { InitialCatalog = nom }.ConnectionString;
    }

    /// <summary>La chaine du serveur des bancs : <see cref="VariableServeur"/>, sinon
    /// <see cref="ServeurDeveloppement"/> ; refusee si elle ne vise pas ce poste.</summary>
    public static string ServeurDesBancs()
    {
        var serveur = Environment.GetEnvironmentVariable(VariableServeur);
        if (string.IsNullOrWhiteSpace(serveur)) serveur = ServeurDeveloppement;
        ExigerServeurLocal(serveur);
        return serveur;
    }

    /// <summary>Cree une base vide et neuve pour le banc <paramref name="banc"/>.</summary>
    public static async Task<BaseJetable> CreerAsync(string banc, CancellationToken ct = default)
    {
        var serveur = ServeurDesBancs();
        // Lettres et chiffres seulement : le nom entre tel quel entre crochets dans le SQL.
        var propre = new string([.. banc.Where(char.IsAsciiLetterOrDigit).Take(40)]);
        var nom = $"{Prefixe}{propre}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}";
        await using (var cn = await OuvrirMaster(serveur, ct))
        await using (var cmd = new SqlCommand($"CREATE DATABASE [{nom}] COLLATE {Collation};", cn))
            await cmd.ExecuteNonQueryAsync(ct);
        return new BaseJetable(serveur, nom);
    }

    /// <summary>La base existe-t-elle sur le serveur des bancs ? (controle des bancs)</summary>
    public static async Task<bool> ExisteAsync(string nom, CancellationToken ct = default)
    {
        await using var cn = await OuvrirMaster(ServeurDesBancs(), ct);
        await using var cmd = new SqlCommand("SELECT COUNT(*) FROM sys.databases WHERE name = @n;", cn);
        cmd.Parameters.AddWithValue("@n", nom);
        return (int)(await cmd.ExecuteScalarAsync(ct))! == 1;
    }

    /// <summary>Supprime la base, connexions ouvertes comprises.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Supprimee) return;
        Supprimee = true;
        // Les connexions du pool tiennent la base ouverte : on les lache d'abord.
        using (var cn = new SqlConnection(Connexion)) SqlConnection.ClearPool(cn);
        await using var master = await OuvrirMaster(Serveur, CancellationToken.None);
        await using var cmd = new SqlCommand(
            $"IF DB_ID(N'{Nom}') IS NOT NULL BEGIN " +
            $"ALTER DATABASE [{Nom}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Nom}]; END", master);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// La chaine vise-t-elle CE poste ? localhost, 127.0.0.1, ::1, « . », (local),
    /// (localdb) ou le nom de la machine -- avec ou sans port ni instance.
    /// </summary>
    public static bool EstServeurLocal(string chaine)
    {
        string source;
        try { source = new SqlConnectionStringBuilder(chaine).DataSource; }
        catch (ArgumentException) { return false; }
        var hote = source.Trim();
        foreach (var protocole in new[] { "tcp:", "np:", "lpc:" })
            if (hote.StartsWith(protocole, StringComparison.OrdinalIgnoreCase)) hote = hote[protocole.Length..];
        if (hote.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase)) return true;
        var coupe = hote.IndexOfAny([',', '\\']);
        if (coupe >= 0) hote = hote[..coupe];
        hote = hote.Trim();
        return hote.Length > 0 && (
            hote is "." or "127.0.0.1" or "::1" or "[::1]" ||
            hote.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            hote.Equals("(local)", StringComparison.OrdinalIgnoreCase) ||
            hote.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Leve si <paramref name="chaine"/> ne vise pas ce poste (voir <see cref="EstServeurLocal"/>).</summary>
    public static void ExigerServeurLocal(string chaine)
    {
        if (EstServeurLocal(chaine)) return;
        string source;
        try { source = new SqlConnectionStringBuilder(chaine).DataSource; }
        catch (ArgumentException) { source = "(chaine illisible)"; }
        throw new InvalidOperationException(
            $"Serveur refuse : « {source} ». Un banc ou l'outil ne creent une base que sur le serveur LOCAL " +
            "(docker compose up -d, a la racine du depot) ; jamais sur SQL-DEV, SQL-PROD ni un serveur de greffe.");
    }

    /// <summary>
    /// Ouvre master. « docker compose up -d » rend la main avant que SQL Server
    /// accepte les connexions (une vingtaine de secondes au premier demarrage) :
    /// on l'attend jusqu'a 90 s, puis on dit quoi faire.
    /// </summary>
    private static async Task<SqlConnection> OuvrirMaster(string serveur, CancellationToken ct)
    {
        var chaine = new SqlConnectionStringBuilder(serveur) { InitialCatalog = "master", ConnectTimeout = 5 }.ConnectionString;
        var limite = DateTime.UtcNow.AddSeconds(90);
        while (true)
        {
            var cn = new SqlConnection(chaine);
            try
            {
                await cn.OpenAsync(ct);
                return cn;
            }
            catch (SqlException e)
            {
                await cn.DisposeAsync();
                if (DateTime.UtcNow >= limite)
                    throw new InvalidOperationException(
                        $"SQL Server des bancs injoignable ({new SqlConnectionStringBuilder(serveur).DataSource}) depuis 90 s : " +
                        $"lancer « docker compose up -d » a la racine du depot (ou poser {VariableServeur}). Derniere erreur : {e.Message}", e);
                await Task.Delay(2000, ct);
            }
        }
    }
}
