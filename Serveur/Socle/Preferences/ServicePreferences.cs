using System.Data;
using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Socle.Preferences;

/// <summary>
/// Les preferences d'un agent dans la base V3 de son greffe (SOC-013 T02, CA01 et CA05) : lire et remplacer.
/// La valeur est le JSON que l'ecran a ecrit ; le serveur ne l'interprete pas (une preference nouvelle de l'ecran
/// n'a besoin ni de migration ni de nouvelle version du serveur). Tout se fait sur la connexion que la route tient
/// ouverte sur la base DU greffe de l'adresse : aucune autre base n'est lue a sa place.
/// </summary>
public static class ServicePreferences
{
    /// <summary>Plus long qu'un nom de preference ne peut l'etre (colonne soc.Preference.Nom).</summary>
    public const int LongueurNom = 60;

    /// <summary>Au-dela, une valeur est refusee : un reglage d'accueil n'a pas a peser plus (l'accueil.json entier
    /// d'un poste de developpement pese ~10 Ko). Sans plafond, un ecran qui boucle remplirait la base.</summary>
    public const int TailleMaxValeur = 256 * 1024;

    /// <summary>Les preferences du compte <paramref name="idCompte"/>, et le serveur RDS qui en a ecrit la derniere.</summary>
    public static async Task<(Dictionary<string, string> Valeurs, string? Serveur)> LireAsync(SqlConnection cn, int idCompte, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            "SELECT Nom, Valeur, ModifieSur FROM soc.Preference WHERE IdCompte = @id ORDER BY ModifieLe, Nom;", cn);
        cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = idCompte });
        var valeurs = new Dictionary<string, string>(StringComparer.Ordinal);
        string? serveur = null;
        await using var lecteur = await cmd.ExecuteReaderAsync(ct);
        while (await lecteur.ReadAsync(ct))
        {
            valeurs[lecteur.GetString(0)] = lecteur.GetString(1);
            if (!lecteur.IsDBNull(2)) serveur = lecteur.GetString(2);   // tri par date : le dernier ecrit gagne
        }
        return (valeurs, serveur);
    }

    /// <summary>
    /// Le motif d'un refus de <paramref name="valeurs"/>, ou null si elles s'ecrivent : nom vide ou trop long,
    /// valeur absente ou trop lourde. Rien n'est ecrit quand une seule est refusee.
    /// </summary>
    public static string? Refus(IReadOnlyDictionary<string, string>? valeurs)
    {
        if (valeurs is null) return "Aucune preference envoyee.";
        foreach (var (nom, valeur) in valeurs)
        {
            if (string.IsNullOrWhiteSpace(nom) || nom.Length > LongueurNom)
                return $"Nom de preference refuse : « {nom} » (1 a {LongueurNom} caracteres).";
            if (valeur is null) return $"La preference « {nom} » n'a pas de valeur.";
            if (valeur.Length > TailleMaxValeur)
                return $"La preference « {nom} » pese {valeur.Length} caracteres : au-dela de {TailleMaxValeur}, elle est refusee.";
        }
        return null;
    }

    /// <summary>
    /// Remplace, pour le compte <paramref name="idCompte"/>, les preferences envoyees (les autres ne bougent pas),
    /// en une transaction : toutes ou aucune. <paramref name="serveur"/> : le serveur RDS d'ou elles viennent.
    /// </summary>
    public static async Task<int> EcrireAsync(SqlConnection cn, int idCompte, IReadOnlyDictionary<string, string> valeurs,
                                              string? serveur, CancellationToken ct)
    {
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        var changees = 0;
        foreach (var (nom, valeur) in valeurs)
        {
            // Une valeur identique n'est pas reecrite : la date et le serveur gardent leur sens (« ecrite la, ce jour-la »).
            // Comparaison en binaire : la collation de la base ignore la casse, « Clair » vaudrait « clair ».
            await using var cmd = new SqlCommand("""
                UPDATE soc.Preference SET Valeur = @valeur, ModifieLe = SYSDATETIME(), ModifieSur = @serveur
                WHERE IdCompte = @id AND Nom = @nom AND CAST(Valeur AS varbinary(max)) <> CAST(@valeur AS varbinary(max));
                DECLARE @n int = @@ROWCOUNT;
                IF @n = 0 AND NOT EXISTS (SELECT 1 FROM soc.Preference WHERE IdCompte = @id AND Nom = @nom)
                BEGIN
                    INSERT INTO soc.Preference (IdCompte, Nom, Valeur, ModifieSur) VALUES (@id, @nom, @valeur, @serveur);
                    SET @n = 1;
                END
                SELECT @n;
                """, cn, tx);
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = idCompte });
            cmd.Parameters.Add(new SqlParameter("@nom", SqlDbType.VarChar, LongueurNom) { Value = nom });
            cmd.Parameters.Add(new SqlParameter("@valeur", SqlDbType.NVarChar, -1) { Value = valeur });
            cmd.Parameters.Add(new SqlParameter("@serveur", SqlDbType.VarChar, 64) { Value = (object?)Borne(serveur, 64) ?? DBNull.Value });
            changees += Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        }
        await tx.CommitAsync(ct);
        return changees;
    }

    internal static string? Borne(string? texte, int taille)
        => string.IsNullOrWhiteSpace(texte) ? null : texte.Trim().Length <= taille ? texte.Trim() : texte.Trim()[..taille];
}
