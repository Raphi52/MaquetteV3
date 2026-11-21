using System.Data;
using Microsoft.Data.SqlClient;
using RigV3.Serveur.Socle.Comptes;

namespace RigV3.Serveur.Recopie;

/// <summary>Une ligne imprimee par le transfert (et gardee dans rec.TransfertEvenement).</summary>
public sealed record EvenementTransfert(DateTime Le, string Genre, string Message);

/// <summary>
/// LE JOURNAL D'UN TRANSFERT, dans la base V3 (rec.Transfert, rec.TransfertEvenement). Chaque ecriture ouvre sa
/// connexion : le journal reste ecrit meme quand la table en cours est annulee (arret propre).
/// </summary>
internal sealed class JournalTransfert
{
    private readonly string Connexion;
    private readonly Action<EvenementTransfert>? Suivre;
    public int Id { get; }

    private JournalTransfert(string connexion, int id, Action<EvenementTransfert>? suivre) => (Connexion, Id, Suivre) = (connexion, id, suivre);

    public static async Task<JournalTransfert> OuvrirAsync(string connexionV3, string baseV2, string serveurV2,
                                                           Action<EvenementTransfert>? suivre, CancellationToken ct)
    {
        await using var cn = new SqlConnection(connexionV3);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("INSERT INTO rec.Transfert (BaseV2, ServeurV2) OUTPUT INSERTED.Id VALUES (@b, @s);", cn);
        cmd.Parameters.Add(new SqlParameter("@b", SqlDbType.NVarChar, 128) { Value = baseV2 });
        cmd.Parameters.Add(new SqlParameter("@s", SqlDbType.NVarChar, 128) { Value = serveurV2 });
        return new JournalTransfert(connexionV3, (int)(await cmd.ExecuteScalarAsync(ct))!, suivre);
    }

    /// <summary>Ecrit une ligne au journal et la montre a qui suit le transfert.</summary>
    public async Task EcrireAsync(string genre, string message, CancellationToken ct)
    {
        Suivre?.Invoke(new EvenementTransfert(DateTime.Now, genre, message));
        await using var cn = new SqlConnection(Connexion);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("INSERT INTO rec.TransfertEvenement (IdTransfert, Genre, Message) VALUES (@id, @g, @m);", cn);
        cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = Id });
        cmd.Parameters.Add(new SqlParameter("@g", SqlDbType.VarChar, 12) { Value = genre });
        cmd.Parameters.Add(new SqlParameter("@m", SqlDbType.NVarChar, -1) { Value = message });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task InstantaneAsync(Instantane i, CancellationToken ct)
    {
        await using var cn = new SqlConnection(Connexion);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("UPDATE rec.Transfert SET Instantane = @n, InstantaneLe = @le, VersionSuivi = @v WHERE Id = @id;", cn);
        cmd.Parameters.Add(new SqlParameter("@n", SqlDbType.NVarChar, 128) { Value = i.Nom });
        cmd.Parameters.Add(new SqlParameter("@le", SqlDbType.DateTime2) { Value = i.Le });
        cmd.Parameters.Add(new SqlParameter("@v", SqlDbType.BigInt) { Value = (object?)i.VersionSuivi ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = Id });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Ecrit la ligne de rapport d'une table portee par une reprise.</summary>
    public async Task TableAsync(LigneRapport l, DateTime debut, CancellationToken ct)
    {
        await using var cn = new SqlConnection(Connexion);
        await cn.OpenAsync(ct);
        await InsererTableAsync(cn, null, Id, l, debut, ct);
    }

    internal static async Task InsererTableAsync(SqlConnection cn, SqlTransaction? tx, int id, LigneRapport l, DateTime debut, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            INSERT INTO rec.TransfertTable (IdTransfert, TableV2, Ordre, Regle, Story, TableV3, Lues, Ecrites, Rejetees, LiensCasses,
                                            Correspondances, Octets, Debut, Fin, DureeMs, Detail)
            VALUES (@id, @t, @o, @r, @s, @t3, @lu, @e, @rj, @lc, @c, @oc, @d, SYSDATETIME(), @ms, @det);
            """, cn, tx);
        static object V(long? v) => (object?)v ?? DBNull.Value;
        cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
        cmd.Parameters.Add(new SqlParameter("@t", SqlDbType.NVarChar, 128) { Value = l.TableV2 });
        cmd.Parameters.Add(new SqlParameter("@o", SqlDbType.Int) { Value = l.Ordre });
        cmd.Parameters.Add(new SqlParameter("@r", SqlDbType.VarChar, 20) { Value = l.Regle });
        cmd.Parameters.Add(new SqlParameter("@s", SqlDbType.VarChar, 20) { Value = l.Story });
        cmd.Parameters.Add(new SqlParameter("@t3", SqlDbType.NVarChar, 260) { Value = (object?)l.TableV3 ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@lu", SqlDbType.BigInt) { Value = V(l.Lues) });
        cmd.Parameters.Add(new SqlParameter("@e", SqlDbType.BigInt) { Value = V(l.Ecrites) });
        cmd.Parameters.Add(new SqlParameter("@rj", SqlDbType.BigInt) { Value = V(l.Rejetees) });
        cmd.Parameters.Add(new SqlParameter("@lc", SqlDbType.BigInt) { Value = V(l.LiensCasses) });
        cmd.Parameters.Add(new SqlParameter("@c", SqlDbType.BigInt) { Value = V(l.Correspondances) });
        cmd.Parameters.Add(new SqlParameter("@oc", SqlDbType.BigInt) { Value = V(l.Octets) });
        cmd.Parameters.Add(new SqlParameter("@d", SqlDbType.DateTime2) { Value = debut });
        cmd.Parameters.Add(new SqlParameter("@ms", SqlDbType.BigInt) { Value = l.DureeMs });
        cmd.Parameters.Add(new SqlParameter("@det", SqlDbType.NVarChar, -1) { Value = (object?)l.Detail ?? DBNull.Value });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Clot le transfert. Fini : l'evenement 210 de la V2 (« reprise de donnees d'autres logiciels »), un seul
    /// pour toute la base comme pour les droits (SOC-005 CA03), dans la meme transaction que l'etat.</summary>
    public async Task TerminerAsync(string etat, string? motif, long dureeMs, (long Lues, long Ecrites, long Rejetees, long LiensCasses) totaux,
                                    string baseV2, CancellationToken ct)
    {
        await using var cn = new SqlConnection(Connexion);
        await cn.OpenAsync(CancellationToken.None);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(CancellationToken.None);
        if (etat == "fini")
            await JournalGestes.EcrireEvenementAsync(cn, tx, RepriseDroits.CodeEvenementReprise,
                "Reprise de données d'autres logiciels suite à changement de systèmes",
                $"Transfert initial de la base V2 {baseV2} (transfert {Id}) : {totaux.Lues} lignes lues, {totaux.Ecrites} écrites, " +
                $"{totaux.Rejetees} rejetées, {totaux.LiensCasses} liens cassés gardés", null, CancellationToken.None);
        await using (var cmd = new SqlCommand("""
            UPDATE rec.Transfert SET Etat = @e, Motif = @m, Fin = SYSDATETIME(), DureeMs = @ms,
                   Lues = @lu, Ecrites = @ec, Rejetees = @rj, LiensCasses = @lc
            WHERE Id = @id;
            """, cn, tx))
        {
            cmd.Parameters.Add(new SqlParameter("@e", SqlDbType.VarChar, 10) { Value = etat });
            cmd.Parameters.Add(new SqlParameter("@m", SqlDbType.NVarChar, -1) { Value = (object?)motif ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@ms", SqlDbType.BigInt) { Value = dureeMs });
            cmd.Parameters.Add(new SqlParameter("@lu", SqlDbType.BigInt) { Value = totaux.Lues });
            cmd.Parameters.Add(new SqlParameter("@ec", SqlDbType.BigInt) { Value = totaux.Ecrites });
            cmd.Parameters.Add(new SqlParameter("@rj", SqlDbType.BigInt) { Value = totaux.Rejetees });
            cmd.Parameters.Add(new SqlParameter("@lc", SqlDbType.BigInt) { Value = totaux.LiensCasses });
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = Id });
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);
        }
        await tx.CommitAsync(CancellationToken.None);
    }
}
