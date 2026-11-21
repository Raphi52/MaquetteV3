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
    public string ConnexionV3 => Connexion;
    private readonly Action<EvenementTransfert>? Suivre;
    public int Id { get; }
    /// <summary>0 : le premier passage ; n : la n-ieme reprise (REC-005).</summary>
    public int Passage { get; private init; }
    /// <summary>La duree des passages precedents : la duree du transfert est leur somme et celle de ce passage.</summary>
    public long DureePrecedente { get; private init; }

    private JournalTransfert(string connexion, int id, Action<EvenementTransfert>? suivre) => (Connexion, Id, Suivre) = (connexion, id, suivre);

    /// <summary>
    /// Reprend le journal d'un transfert interrompu (REC-005 CA04) : meme identifiant (la correspondance, les rejets et
    /// les liens casses deja ecrits lui appartiennent), une reprise de plus, l'etat repasse « en cours ».
    /// </summary>
    public static async Task<JournalTransfert> ReprendreAsync(string connexionV3, int id, Action<EvenementTransfert>? suivre, CancellationToken ct)
    {
        await using var cn = new SqlConnection(connexionV3);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            UPDATE rec.Transfert SET Etat = 'en cours', Reprises = Reprises + 1, Motif = NULL, Fin = NULL
            OUTPUT INSERTED.Reprises, ISNULL(DELETED.DureeMs, 0)
            WHERE Id = @id;
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
        await using var l = await cmd.ExecuteReaderAsync(ct);
        await l.ReadAsync(ct);
        return new JournalTransfert(connexionV3, id, suivre) { Passage = l.GetInt32(0), DureePrecedente = l.GetInt64(1) };
    }

    /// <summary>
    /// Le point de reprise d'un transfert, en clair (rapport du matin, REC-005 CA04) : la table en cours et son dernier lot
    /// enregistre, les tables finies ; null si aucune ligne n'est encore ecrite.
    /// </summary>
    public static async Task<string?> PointDeRepriseAsync(SqlConnection cn, int id, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT (SELECT COUNT(*) FROM rec.TransfertTable WHERE IdTransfert = @id),
                   (SELECT TOP (1) CONCAT(l.TableV2, N' : lot ', l.Lot, N' enregistre (', l.Lignes, N' lignes ecrites, derniere cle ',
                                          ISNULL(l.DerniereCle, N'aucune'), N', le ', CONVERT(nvarchar(19), l.Le, 120), N')')
                    FROM rec.TransfertLot l
                    WHERE l.IdTransfert = @id AND NOT EXISTS (SELECT 1 FROM rec.TransfertTable t WHERE t.IdTransfert = l.IdTransfert AND t.TableV2 = l.TableV2)
                    ORDER BY l.Le DESC, l.Lot DESC),
                   (SELECT TOP (1) TableV2 FROM rec.TransfertTable WHERE IdTransfert = @id ORDER BY Fin DESC);
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
        await using var l = await cmd.ExecuteReaderAsync(ct);
        await l.ReadAsync(ct);
        var finies = l.GetInt32(0);
        var enCours = l.IsDBNull(1) ? null : l.GetString(1);
        var derniere = l.IsDBNull(2) ? null : l.GetString(2);
        if (finies == 0 && enCours is null) return null;
        return (enCours is null ? "aucune table entamee" : "table en cours " + enCours) +
               $" ; {finies} table(s) finie(s){(derniere is null ? "" : $", la derniere : {derniere}")}";
    }

    /// <summary>Clot le passage sur un incident technique (REC-005) : etat « interrompu », motif et point de reprise.</summary>
    public async Task InterrompreAsync(string motif, long dureeMs, (long Lues, long Ecrites, long Rejetees, long LiensCasses) totaux)
    {
        await using var cn = new SqlConnection(Connexion);
        await cn.OpenAsync(CancellationToken.None);
        var point = await PointDeRepriseAsync(cn, Id, CancellationToken.None);
        await using var cmd = new SqlCommand("""
            UPDATE rec.Transfert SET Etat = 'interrompu', Motif = @m, PointDeReprise = @p, Fin = SYSDATETIME(), DureeMs = @ms,
                   Lues = @lu, Ecrites = @ec, Rejetees = @rj, LiensCasses = @lc
            WHERE Id = @id;
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@m", SqlDbType.NVarChar, -1) { Value = motif });
        cmd.Parameters.Add(new SqlParameter("@p", SqlDbType.NVarChar, 1000) { Value = (object?)point ?? "aucune ligne ecrite" });
        cmd.Parameters.Add(new SqlParameter("@ms", SqlDbType.BigInt) { Value = DureePrecedente + dureeMs });
        cmd.Parameters.Add(new SqlParameter("@lu", SqlDbType.BigInt) { Value = totaux.Lues });
        cmd.Parameters.Add(new SqlParameter("@ec", SqlDbType.BigInt) { Value = totaux.Ecrites });
        cmd.Parameters.Add(new SqlParameter("@rj", SqlDbType.BigInt) { Value = totaux.Rejetees });
        cmd.Parameters.Add(new SqlParameter("@lc", SqlDbType.BigInt) { Value = totaux.LiensCasses });
        cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = Id });
        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    /// <summary>Le nombre de tables transferees par une regle provisoire (REC-008), ecrit au journal du transfert.</summary>
    public async Task ProvisoiresAsync(int nombre, CancellationToken ct)
    {
        await using var cn = new SqlConnection(Connexion);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("UPDATE rec.Transfert SET Provisoires = @n WHERE Id = @id;", cn);
        cmd.Parameters.Add(new SqlParameter("@n", SqlDbType.Int) { Value = nombre });
        cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = Id });
        await cmd.ExecuteNonQueryAsync(ct);
    }

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
            UPDATE rec.Transfert SET Etat = @e, Motif = @m, Fin = SYSDATETIME(), DureeMs = @ms, PointDeReprise = NULL,
                   Lues = @lu, Ecrites = @ec, Rejetees = @rj, LiensCasses = @lc
            WHERE Id = @id;
            """, cn, tx))
        {
            cmd.Parameters.Add(new SqlParameter("@e", SqlDbType.VarChar, 10) { Value = etat });
            cmd.Parameters.Add(new SqlParameter("@m", SqlDbType.NVarChar, -1) { Value = (object?)motif ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@ms", SqlDbType.BigInt) { Value = DureePrecedente + dureeMs });
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
