using Microsoft.Data.SqlClient;

namespace RigV3.Base;

// ============================================================
// LE JOURNAL DES LIVRAISONS de chaque base V3 (SOC-009 T01, lot 12 du plan, 2026-11-21).
//
// La version qu'une base porte est la derniere livraison REUSSIE inscrite ici, par
// l'outil de livraison (Outils/Livraison). Comme le journal des migrations
// (rigv3.Migration, Migrateur.cs), c'est une table technique de l'OUTIL, dans le
// schema rigv3, creee par lui : elle existe avant la premiere migration du paquet,
// et une livraison en echec s'y inscrit APRES le retour de la base a sa version
// precedente (le retour efface tout ce qui a ete ecrit pendant la tentative).
//
//   rigv3.Livraison    une ligne par tentative : version, vague, paquet, debut, fin,
//                      resultat (livree, echec, ecart), detail, operateur, poste ;
//   rigv3.SchemaLivre  le schema de la version livree (BaseGreffe.SchemaAsync), une
//                      ligne par objet, remplace a chaque livraison reussie : le
//                      controle d'ecart AVANT la livraison suivante s'y compare (CA03).
//
// La partie serveur lit la version ici (ModuleVersions) ; l'outil de livraison l'ecrit.
// ============================================================

/// <summary>Une tentative de livraison sur une base.</summary>
public sealed record LivraisonInscrite(int Id, string Version, string Vague, string Paquet, DateTime Debut, DateTime Fin,
                                       string Resultat, string? Detail, string Operateur, string Poste);

public static class JournalLivraisons
{
    public const string Table = "rigv3.Livraison";
    public const string TableSchema = "rigv3.SchemaLivre";

    /// <summary>Les valeurs de <see cref="LivraisonInscrite.Resultat"/>.</summary>
    public const string Livree = "livree", Echec = "echec", Ecart = "ecart";

    /// <summary>Cree les deux tables si elles manquent (sans rien toucher sinon). Le paquet de livraison
    /// les cree aussi sur sa base de reference : elles font partie du schema livre.</summary>
    public static async Task CreerAsync(SqlConnection cn, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand($"""
            IF SCHEMA_ID(N'rigv3') IS NULL EXEC(N'CREATE SCHEMA rigv3');
            IF OBJECT_ID(N'{Table}', N'U') IS NULL
                CREATE TABLE {Table} (
                    Id         int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Livraison PRIMARY KEY,
                    Version    varchar(20)    NOT NULL,
                    Vague      varchar(20)    NOT NULL,
                    Paquet     char(64)       NOT NULL,   -- empreinte SHA-256 du paquet livre
                    Debut      datetime2(3)   NOT NULL,   -- UTC
                    Fin        datetime2(3)   NOT NULL CONSTRAINT DF_Livraison_Fin DEFAULT (SYSUTCDATETIME()),
                    Resultat   varchar(10)    NOT NULL CONSTRAINT CK_Livraison_Resultat CHECK (Resultat IN ('livree', 'echec', 'ecart')),
                    Detail     nvarchar(4000) NULL,
                    Operateur  nvarchar(128)  NOT NULL CONSTRAINT DF_Livraison_Operateur DEFAULT (SUSER_SNAME()),
                    Poste      nvarchar(128)  NOT NULL CONSTRAINT DF_Livraison_Poste DEFAULT (HOST_NAME()));
            IF OBJECT_ID(N'{TableSchema}', N'U') IS NULL
                CREATE TABLE {TableSchema} (
                    Rang   int           NOT NULL CONSTRAINT PK_SchemaLivre PRIMARY KEY,
                    Ligne  nvarchar(max) NOT NULL);
            """, cn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>La version que la base porte (derniere livraison reussie) ; null si aucune livraison n'y est
    /// inscrite (table absente : base de developpement, ou creee hors de l'outil de livraison).</summary>
    public static async Task<string?> VersionAsync(SqlConnection cn, CancellationToken ct = default)
        => (await DerniereReussieAsync(cn, null, ct))?.Version;

    /// <summary>La derniere livraison reussie ; null si aucune.</summary>
    public static async Task<LivraisonInscrite?> DerniereReussieAsync(SqlConnection cn, SqlTransaction? tx, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand(
            $"IF OBJECT_ID(N'{Table}', N'U') IS NOT NULL " +
            $"SELECT TOP (1) Id, Version, Vague, Paquet, Debut, Fin, Resultat, Detail, Operateur, Poste FROM {Table} " +
            "WHERE Resultat = 'livree' ORDER BY Id DESC;", cn, tx);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Lire(r) : null;
    }

    /// <summary>Toutes les tentatives, la plus ancienne d'abord.</summary>
    public static async Task<IReadOnlyList<LivraisonInscrite>> ListerAsync(SqlConnection cn, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand(
            $"IF OBJECT_ID(N'{Table}', N'U') IS NOT NULL " +
            $"SELECT Id, Version, Vague, Paquet, Debut, Fin, Resultat, Detail, Operateur, Poste FROM {Table} ORDER BY Id;", cn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var liste = new List<LivraisonInscrite>();
        while (await r.ReadAsync(ct)) liste.Add(Lire(r));
        return liste;
    }

    /// <summary>Le schema de la version livree, tel qu'inscrit a la derniere livraison reussie ; vide si aucune.</summary>
    public static async Task<IReadOnlyList<string>> SchemaLivreAsync(SqlConnection cn, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand(
            $"IF OBJECT_ID(N'{TableSchema}', N'U') IS NOT NULL SELECT Ligne FROM {TableSchema} ORDER BY Rang;", cn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lignes = new List<string>();
        while (await r.ReadAsync(ct)) lignes.Add(r.GetString(0));
        return lignes;
    }

    /// <summary>Inscrit une tentative. Pour une livraison reussie, <paramref name="schema"/> remplace le schema
    /// livre, dans la transaction <paramref name="tx"/> (version et schema ensemble, ou ni l'un ni l'autre).</summary>
    public static async Task InscrireAsync(SqlConnection cn, SqlTransaction? tx, string version, string vague, string paquet,
                                           DateTime debutUtc, string resultat, string? detail,
                                           IReadOnlyList<string>? schema = null, CancellationToken ct = default)
    {
        await using (var cmd = new SqlCommand(
            $"INSERT INTO {Table} (Version, Vague, Paquet, Debut, Resultat, Detail) VALUES (@v, @vague, @p, @d, @r, @detail);", cn, tx))
        {
            cmd.Parameters.AddWithValue("@v", version);
            cmd.Parameters.AddWithValue("@vague", vague);
            cmd.Parameters.AddWithValue("@p", paquet);
            cmd.Parameters.AddWithValue("@d", debutUtc);
            cmd.Parameters.AddWithValue("@r", resultat);
            cmd.Parameters.AddWithValue("@detail", detail is null ? DBNull.Value : detail.Length > 4000 ? detail[..4000] : detail);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        if (schema is null) return;
        await using (var vider = new SqlCommand($"DELETE FROM {TableSchema};", cn, tx))
            await vider.ExecuteNonQueryAsync(ct);
        for (var i = 0; i < schema.Count; i++)
        {
            await using var ins = new SqlCommand($"INSERT INTO {TableSchema} (Rang, Ligne) VALUES (@r, @l);", cn, tx);
            ins.Parameters.AddWithValue("@r", i);
            ins.Parameters.Add(new SqlParameter("@l", System.Data.SqlDbType.NVarChar, -1) { Value = schema[i] });
            await ins.ExecuteNonQueryAsync(ct);
        }
    }

    private static LivraisonInscrite Lire(SqlDataReader r)
        => new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3).Trim(), r.GetDateTime(4), r.GetDateTime(5),
               r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.GetString(8), r.GetString(9));
}
