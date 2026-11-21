using System.Data;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;

namespace RigV3.Serveur.Socle.Preferences;

/// <summary>
/// Les documents que la partie serveur prepare pour un agent, et la session ou il travaille (SOC-013 T02, CA03, CA04).
///
/// La regle de la V2 (LigneEdition.AfficheDocument, LigneEdition.cs:810-820) : un document s'affiche sur le serveur
/// et dans la session que l'agent a notes a son ouverture (UTLST_LAST_SERVER, UTLST_LAST_SESSIONID,
/// ThreadAccueil.cs:89-94). La V2 les envoyait par file de messages Windows sur ce serveur : un agent sans
/// session ouverte n'avait rien. La V3 garde le document dans soc.DocumentAgent jusqu'a ce que LA session notee le
/// releve, ou que l'agent en ouvre une nouvelle (CA04 : rien n'est perdu).
///
/// Un document n'est remis qu'une fois : la releve le marque remis (serveur, session, date) dans la meme instruction
/// qui le lit (UPDATE … OUTPUT), deux releves simultanees ne peuvent pas l'emporter toutes les deux.
/// </summary>
public static class DocumentsAgents
{
    /// <summary>
    /// La V3 de l'agent <paramref name="idCompte"/> s'ouvre sur <paramref name="ouverture"/> : elle devient SA session
    /// (la precedente ne recoit plus rien) et recoit les documents qui l'attendaient (CA04).
    /// </summary>
    public static async Task<DocumentRecu[]> OuvrirSessionAsync(SqlConnection cn, int idCompte, OuvertureSessionAgent ouverture, CancellationToken ct)
    {
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        await using (var cmd = new SqlCommand("""
            UPDATE soc.SessionAgent WITH (UPDLOCK, SERIALIZABLE) SET Serveur = @serveur, Session = @session, OuverteLe = SYSDATETIME()
            WHERE IdCompte = @id;
            IF @@ROWCOUNT = 0 INSERT INTO soc.SessionAgent (IdCompte, Serveur, Session) VALUES (@id, @serveur, @session);
            """, cn, tx))
        {
            Parametres(cmd, idCompte, ouverture.Serveur, ouverture.Session);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        var remis = await RemettreAsync(cn, tx, idCompte, ouverture.Serveur, ouverture.Session, ct);
        await tx.CommitAsync(ct);
        return remis;
    }

    /// <summary>
    /// Les documents prepares pour l'agent depuis la derniere releve, si <paramref name="serveur"/> et
    /// <paramref name="session"/> sont bien la session ou il travaille ; rien sinon (CA03 : nulle part ailleurs).
    /// </summary>
    public static async Task<DocumentRecu[]> ReleverAsync(SqlConnection cn, int idCompte, string serveur, string session, CancellationToken ct)
    {
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        await using (var cmd = new SqlCommand(
            "SELECT COUNT(*) FROM soc.SessionAgent WITH (UPDLOCK) WHERE IdCompte = @id AND Serveur = @serveur AND Session = @session;", cn, tx))
        {
            Parametres(cmd, idCompte, serveur, session);
            if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 0)
            {
                await tx.RollbackAsync(ct);
                return [];
            }
        }
        var remis = await RemettreAsync(cn, tx, idCompte, serveur, session, ct);
        await tx.CommitAsync(ct);
        return remis;
    }

    /// <summary>
    /// Depose un document pour le compte Windows <paramref name="compte"/> (la partie serveur : une edition prete).
    /// Rend son numero ; leve si le compte n'existe pas dans la base de ce greffe. Il part a la session de l'agent a
    /// sa prochaine releve, ou attend sa prochaine ouverture.
    /// </summary>
    public static async Task<int> DeposerAsync(SqlConnection cn, string compte, string nature, string titre, string fichier,
                                               string deposePar, CancellationToken ct)
    {
        if (nature is not (NaturesDocument.Afficher or NaturesDocument.Relire))
            throw new ArgumentException($"Nature de document inconnue : « {nature} » (afficher ou relire).", nameof(nature));
        await using var cmd = new SqlCommand("""
            INSERT INTO soc.DocumentAgent (IdCompte, Nature, Titre, Fichier, DeposePar)
            OUTPUT inserted.Id
            SELECT c.Id, @nature, @titre, @fichier, @par FROM soc.Compte c WHERE c.CompteWindows = @compte;
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@compte", SqlDbType.VarChar, 35) { Value = compte });
        cmd.Parameters.Add(new SqlParameter("@nature", SqlDbType.VarChar, 10) { Value = nature });
        cmd.Parameters.Add(new SqlParameter("@titre", SqlDbType.NVarChar, 200) { Value = titre });
        cmd.Parameters.Add(new SqlParameter("@fichier", SqlDbType.NVarChar, 400) { Value = fichier });
        cmd.Parameters.Add(new SqlParameter("@par", SqlDbType.VarChar, 80) { Value = deposePar });
        return await cmd.ExecuteScalarAsync(ct) is int id
            ? id
            : throw new InvalidOperationException($"Aucun compte « {compte} » dans la base de ce greffe : le document n'est pas depose.");
    }

    /// <summary>Marque remis a cette session les documents non remis de l'agent, et les rend (du plus ancien au plus recent).</summary>
    private static async Task<DocumentRecu[]> RemettreAsync(SqlConnection cn, SqlTransaction tx, int idCompte, string serveur, string session,
                                                            CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            UPDATE soc.DocumentAgent SET RemisLe = SYSDATETIME(), RemisServeur = @serveur, RemisSession = @session
            OUTPUT inserted.Id, inserted.Nature, inserted.Titre, inserted.Fichier, inserted.DeposeLe, inserted.DeposePar
            WHERE IdCompte = @id AND RemisLe IS NULL;
            """, cn, tx);
        Parametres(cmd, idCompte, serveur, session);
        var remis = new List<DocumentRecu>();
        await using (var lecteur = await cmd.ExecuteReaderAsync(ct))
            while (await lecteur.ReadAsync(ct))
                remis.Add(new DocumentRecu(lecteur.GetInt32(0), lecteur.GetString(1), lecteur.GetString(2), lecteur.GetString(3),
                                           lecteur.GetDateTime(4), lecteur.GetString(5)));
        return [.. remis.OrderBy(d => d.Id)];
    }

    private static void Parametres(SqlCommand cmd, int idCompte, string serveur, string session)
    {
        cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = idCompte });
        cmd.Parameters.Add(new SqlParameter("@serveur", SqlDbType.VarChar, 64) { Value = serveur });
        cmd.Parameters.Add(new SqlParameter("@session", SqlDbType.VarChar, 64) { Value = session });
    }
}
