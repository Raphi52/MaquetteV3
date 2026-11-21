using System.Data;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Numerotation;

namespace RigV3.Serveur.Numerotation.Series;

/// <summary>
/// La liste des numeros manquants du registre (NUM-013 T04), lue dans la vue num.NumeroManquant
/// (Base\migrations\NUM-005_reprise.sql) : chaque numero declare manquant, avec son dernier motif, sauf s'il a ete
/// rendu depuis (NUM-004). Lecture seule : declarer un manquant reste num.DeclarerManquant.
/// </summary>
public static class NumerosManquants
{
    /// <summary>Les numeros manquants de la base <paramref name="cn"/>, par serie, annee et chrono ; filtres facultatifs.</summary>
    public static async Task<NumeroManquant[]> ListerAsync(SqlConnection cn, string? serie, int? annee, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT Serie, Annee, Jour, Chrono, Numero, Motif, Logiciel, DeclareLe
            FROM num.NumeroManquant
            WHERE (@serie IS NULL OR Serie = @serie) AND (@annee IS NULL OR Annee = @annee)
            ORDER BY Serie, Annee, Chrono, Numero;
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@serie", SqlDbType.VarChar, 5) { Value = (object?)serie ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@annee", SqlDbType.Int) { Value = (object?)annee ?? DBNull.Value });
        var lus = new List<NumeroManquant>();
        await using var lecteur = await cmd.ExecuteReaderAsync(ct);
        while (await lecteur.ReadAsync(ct))
            lus.Add(new NumeroManquant(
                lecteur.GetString(0).Trim(),
                Convert.ToInt32(lecteur.GetValue(1)),
                lecteur.IsDBNull(2) ? 0 : Convert.ToInt32(lecteur.GetValue(2)),
                lecteur.IsDBNull(3) ? 0 : Convert.ToInt32(lecteur.GetValue(3)),
                lecteur.GetString(4),
                lecteur.IsDBNull(5) ? null : lecteur.GetString(5),
                lecteur.GetString(6).Trim(),
                Convert.ToDateTime(lecteur.GetValue(7))));
        return [.. lus];
    }
}
