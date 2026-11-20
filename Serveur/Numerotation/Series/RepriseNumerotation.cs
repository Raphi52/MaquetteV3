using System.Data;
using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Numerotation.Series;

/// <summary>Un ecart du rapprochement d'une reprise (num.RepriseEcart) : ce que dit la V2, ce que dit la V3.</summary>
public sealed record EcartReprise(string? Type, int? Annee, int? Jour, string Quoi, string? V2, string? V3);

/// <summary>Le rapport d'un passage (num.Reprise, NUM-005 T03).</summary>
/// <param name="Code">0 validee ; -1 ecart (annulee) ; -10 deja reprise ; -11 base V2 introuvable ; -99 erreur.</param>
/// <param name="DureeMs">Du blocage de la table V2 a la fin du rapprochement (NUM-005 CA01 : moins d'une minute).</param>
public sealed record RapportReprise(int Code, int? IdReprise, string? Message, int? DureeMs, int? LignesV2, int? Compteurs,
                                    int? Rendus, int? Journalisees, int? Archivees, int? Manquants, int? CodesLiaison,
                                    int? OrdresFiche, IReadOnlyList<EcartReprise> Ecarts);

/// <summary>
/// Le passage d'une base (NUM-005) : num.Reprendre recopie les compteurs de la V2 dans la V3 sous blocage
/// et rapproche ; num.ReleverHeritage releve ensuite, en lecture, les valeurs heritees a signaler.
/// Les deux bases sont sur la meme instance (Q4) : <paramref name="baseV2"/> est le nom de la base V2.
/// Appele dans la transaction du passage (remplacement des trois programmes, lot 7), le blocage dure
/// jusqu'a son commit. Mode d'emploi : docs/reconstruction/numerotation-series.md.
/// </summary>
public static class RepriseNumerotation
{
    /// <summary>Le prefixe provisoire de la serie des factures de la V3 (NUM-020 CA02, choix du referent Facturation).</summary>
    public const string PrefixeFacturesV3 = "V3-";

    public static async Task<RapportReprise> ReprendreAsync(SqlConnection cn, SqlTransaction? tx, string baseV2,
                                                            string prefixeFacturesV3 = PrefixeFacturesV3,
                                                            CancellationToken ct = default)
    {
        int code, id;
        await using (var cmd = new SqlCommand("num.Reprendre", cn, tx) { CommandType = CommandType.StoredProcedure, CommandTimeout = 600 })
        {
            cmd.Parameters.Add("@BaseV2", SqlDbType.NVarChar, 128).Value = baseV2;
            cmd.Parameters.Add("@PrefixeFacturesV3", SqlDbType.VarChar, 16).Value = prefixeFacturesV3;
            var idReprise = cmd.Parameters.Add("@IdReprise", SqlDbType.Int);
            idReprise.Direction = ParameterDirection.Output;
            var retour = cmd.Parameters.Add("@RETURN_VALUE", SqlDbType.Int);
            retour.Direction = ParameterDirection.ReturnValue;
            await cmd.ExecuteNonQueryAsync(ct);
            code = (int)retour.Value;
            id = idReprise.Value is int i ? i : 0;
        }
        return await LireRapportAsync(cn, tx, id, code, ct);
    }

    /// <summary>num.ReleverHeritage : les signalements au referent et les valeurs heritees ; rend leur nombre.</summary>
    public static async Task<int> ReleverHeritageAsync(SqlConnection cn, SqlTransaction? tx, string baseV2, int? idReprise,
                                                       CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand("num.ReleverHeritage", cn, tx) { CommandType = CommandType.StoredProcedure, CommandTimeout = 600 };
        cmd.Parameters.Add("@BaseV2", SqlDbType.NVarChar, 128).Value = baseV2;
        cmd.Parameters.Add("@IdReprise", SqlDbType.Int).Value = (object?)idReprise ?? DBNull.Value;
        var signales = cmd.Parameters.Add("@Signales", SqlDbType.Int);
        signales.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync(ct);
        return (int)signales.Value;
    }

    private static async Task<RapportReprise> LireRapportAsync(SqlConnection cn, SqlTransaction? tx, int id, int code, CancellationToken ct)
    {
        var ecarts = new List<EcartReprise>();
        await using (var cmd = new SqlCommand(
            "SELECT Type, Annee, Jour, Quoi, V2, V3 FROM num.RepriseEcart WHERE IdReprise = @id;", cn, tx))
        {
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = id;
            await using var l = await cmd.ExecuteReaderAsync(ct);
            while (await l.ReadAsync(ct))
                ecarts.Add(new EcartReprise(Texte(l, 0), Entier(l, 1), Entier(l, 2), l.GetString(3), Texte(l, 4), Texte(l, 5)));
        }
        await using (var cmd = new SqlCommand(
            "SELECT Message, DureeMs, LignesV2, Compteurs, Rendus, Journalisees, Archivees, Manquants, CodesLiaison, OrdresFiche " +
            "FROM num.Reprise WHERE Id = @id;", cn, tx))
        {
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = id;
            await using var l = await cmd.ExecuteReaderAsync(ct);
            if (!await l.ReadAsync(ct)) return new RapportReprise(code, null, null, null, null, null, null, null, null, null, null, null, ecarts);
            return new RapportReprise(code, id, Texte(l, 0), Entier(l, 1), Entier(l, 2), Entier(l, 3), Entier(l, 4), Entier(l, 5),
                                      Entier(l, 6), Entier(l, 7), Entier(l, 8), Entier(l, 9), ecarts);
        }
    }

    private static string? Texte(SqlDataReader l, int i) => l.IsDBNull(i) ? null : l.GetString(i);
    private static int? Entier(SqlDataReader l, int i) => l.IsDBNull(i) ? null : Convert.ToInt32(l.GetValue(i));
}
