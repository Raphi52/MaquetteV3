using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Socle.Greffes;

/// <summary>
/// La SEULE porte de la V3 vers une base V2 pour la reprise : elle ne laisse passer
/// qu'une lecture (un SELECT seul, sans INTO, sans point-virgule). La reprise lit la
/// V2 sans jamais l'ecrire (regle commune du plan : RIG_DEV et les bases des greffes
/// se lisent, elles ne s'ecrivent pas) ; ce garde-fou le tient dans le code, pas dans
/// la memoire de chacun.
/// </summary>
public static partial class LectureV2
{
    [GeneratedRegex(@"\b(INTO|INSERT|UPDATE|DELETE|MERGE|EXEC|EXECUTE|DROP|ALTER|CREATE|TRUNCATE|GRANT|DENY|REVOKE)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MotQuiEcrit();

    /// <summary>Leve si <paramref name="sql"/> n'est pas une lecture pure.</summary>
    public static void ExigerLecture(string sql)
    {
        var t = sql.Trim();
        if (!t.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) || t.Contains(';') || MotQuiEcrit().IsMatch(t))
            throw new InvalidOperationException(
                $"Lecture V2 refusee : seule une requete SELECT seule est permise sur une base V2 (recu : « {Couper(t)} »).");
    }

    /// <summary>Execute une lecture sur la base V2 et rend son lecteur.</summary>
    public static async Task<SqlDataReader> LireAsync(SqlConnection v2, string sql, CancellationToken ct,
                                                      params SqlParameter[] parametres)
    {
        ExigerLecture(sql);
        var cmd = new SqlCommand(sql, v2) { CommandTimeout = 120 };
        cmd.Parameters.AddRange(parametres);
        return await cmd.ExecuteReaderAsync(ct);
    }

    /// <summary>Une valeur seule (SELECT COUNT(*)...).</summary>
    public static async Task<object?> ValeurAsync(SqlConnection v2, string sql, CancellationToken ct)
    {
        ExigerLecture(sql);
        await using var cmd = new SqlCommand(sql, v2) { CommandTimeout = 120 };
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is DBNull ? null : v;
    }

    private static string Couper(string s) => s.Length <= 80 ? s : s[..80] + "…";
}
