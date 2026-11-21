// fix-ok: fichier NEUF du lot 9 ; premier essai mesure par RecopieDeuxSensCheck (REC-005 CA02 V2 vers V3 rouge) : le MERGE de
// RefuserV2Async avait deux clauses « WHEN MATCHED ... UPDATE », que SQL Server refuse -- une seule clause, des CASE.
using System.Data;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Recopie;
using static RigV3.Serveur.Recopie.EcrituresRecopie;

namespace RigV3.Serveur.Recopie;

/// <summary>Une fiche qui ne peut pas passer maintenant sans que ce soit un refus : elle attend derriere un changement mis
/// de cote (le sien, ou celui d'une fiche qu'elle vise), ou sa table n'est plus dans la recopie. Aucun essai n'est compte.</summary>
public sealed class RecopieBloquee(string message) : Exception(message);

/// <summary>L'echec d'UNE fiche, nommee : une fiche visee qui echoue pendant le passage d'une autre porte son propre essai.</summary>
internal sealed class EchecFiche(string table, string id, Exception cause) : Exception(cause.Message, cause)
{
    /// <summary>La table de la fiche : V3 pour l'applicateur (« v2.CLIENT »), V2 pour la remontee (« CLIENT »).</summary>
    public string Table { get; } = table;
    public string Id { get; } = id;
}

/// <summary>
/// UN CHANGEMENT REFUSE NE BLOQUE PAS LA RECOPIE DU GREFFE (REC-005 CA02, CA03 sur la file continue ; decision du chef du
/// 21/11). Ce que l'applicateur (V3 -> V2) et la remontee (V2 -> V3) partagent :
///  - PANNE ou REFUS. Une panne de la base (hors ligne, injoignable, delai, connexion coupee, droit retire) touche toutes
///    les fiches : le passage s'arrete, rien ne compte d'essai, tout attend le suivant (REC-002 CA06). Toute autre erreur de
///    SQL Server est un REFUS du changement par la base d'en face (contrainte, cle, declencheur, conversion) : il compte un
///    essai et les autres fiches continuent ; au <see cref="Seuil"/>e essai, il est MIS DE COTE avec son message.
///  - ATTENTE PAR FICHE. Les changements suivants d'une fiche mise de cote attendent derriere elle (l'ordre de la fiche est
///    garde) ; une fiche qui en vise une mise de cote aussi (la ligne visee doit exister avant elle, REC-002 CA03).
///  - RELANCE. L'exploitant corrige la cause et relance : le changement redevient « en attente » (essais a zero, trace de
///    la relance gardee), il repasse au passage suivant, puis ceux qui attendaient derriere lui, dans l'ordre.
/// Les numeros d'erreur de panne : « Database engine events and errors » de SQL Server
/// (https://learn.microsoft.com/sql/relational-databases/errors-events/database-engine-events-and-errors), et -2 / 53 /
/// 10053 / 10054 / 10060 cote client (delai, reseau). La base hors ligne (942) est mesuree par RecopieDeuxSensCheck (CA06).
/// </summary>
internal static class MisesDeCote
{
    /// <summary>Le seuil propose par REC-005 CA02 : au 3e essai refuse, le changement est mis de cote.</summary>
    public const int Seuil = ServiceRecopie.EssaisAvantMiseDeCote;

    private static readonly HashSet<int> NumerosDePanne =
    [
        -2, 2, 53, 64, 121, 233, 10053, 10054, 10060, 11001,   // delai, reseau, connexion coupee (client)
        942, 945, 946, 952, 922, 924, 926, 4060, 40613,        // base hors ligne, en transition, inaccessible, mono-utilisateur, suspecte
        18456, 229, 230, 262, 297, 300,                        // compte refuse, droit retire : toute la recopie est touchee
        1205, 1222, 701, 802, 8645, 3960,                      // interblocage, verrou, memoire, conflit d'instantane : passagers
    ];

    /// <summary>Vrai si l'erreur touche la base entiere (le passage s'arrete, aucun essai) ; faux si c'est le changement
    /// lui-meme que la base refuse.</summary>
    public static bool EstPanne(Exception e, SqlConnection cn)
        => cn.State != ConnectionState.Open
           || e is SqlException s && (s.Class >= 20 || s.Errors.Cast<SqlError>().Any(x => NumerosDePanne.Contains(x.Number)));

    // --- V3 -> V2 : rec.FileRecopie ----------------------------------------------------------------------------------

    /// <summary>La fiche V3 a-t-elle un changement mis de cote ? (ses changements suivants attendent derriere lui)</summary>
    public static async Task<bool> BloqueeV3Async(SqlConnection cn, SqlTransaction? tx, string tableV3, string idV3, CancellationToken ct)
        => await ScalaireAsync(cn, tx, "SELECT TOP (1) CONVERT(nvarchar(20), Id) FROM rec.FileRecopie WHERE TableV3 = @t AND IdV3 = @id AND Etat = 'mise de cote';",
                               ct, ("@t", tableV3), ("@id", idV3)) is not null;

    /// <summary>Le refus du changement en tete de la fiche : un essai de plus ; au seuil, mis de cote. Vrai s'il l'est.
    /// <paramref name="seuil"/> 1 : un refus qu'aucun essai ne levera (RecopieImpossible), mis de cote d'emblee.</summary>
    public static async Task<bool> RefuserV3Async(SqlConnection cn, string tableV3, string idV3, string erreur, CancellationToken ct, int seuil = Seuil)
        => await ScalaireAsync(cn, null, """
            DECLARE @tete bigint = (SELECT MIN(Id) FROM rec.FileRecopie WHERE TableV3 = @t AND IdV3 = @id AND Etat = 'en attente');
            UPDATE rec.FileRecopie SET Essais = Essais + 1, Erreur = LEFT(@e, 2000),
                   Etat = CASE WHEN Essais + 1 >= @seuil THEN 'mise de cote' ELSE Etat END,
                   MiseDeCoteLe = CASE WHEN Essais + 1 >= @seuil THEN SYSDATETIME() ELSE MiseDeCoteLe END
            OUTPUT inserted.Etat WHERE Id = @tete;
            """, ct, ("@t", tableV3), ("@id", idV3), ("@e", erreur), ("@seuil", seuil)) == "mise de cote";

    /// <summary>Le motif d'une attente sans essai (panne, fiche bloquee), note sur la tete de la fiche.</summary>
    public static async Task NoterMotifV3Async(SqlConnection cn, string tableV3, string idV3, string motif, CancellationToken ct)
    {
        if (cn.State != ConnectionState.Open) return;
        await ExecuterAsync(cn, null, """
            UPDATE rec.FileRecopie SET Erreur = LEFT(@e, 2000)
            WHERE Id = (SELECT MIN(Id) FROM rec.FileRecopie WHERE TableV3 = @t AND IdV3 = @id AND Etat = 'en attente');
            """, ct, ("@t", tableV3), ("@id", idV3), ("@e", motif));
    }

    /// <summary>La relance d'un changement mis de cote (CA03) ; faux si la ligne n'est pas mise de cote.</summary>
    public static async Task<bool> RelancerV3Async(SqlConnection cn, long ligne, string par, CancellationToken ct)
        => await ScalaireAsync(cn, null, """
            UPDATE rec.FileRecopie SET Etat = 'en attente', Essais = 0, RelanceLe = SYSDATETIME(), RelancePar = LEFT(@par, 128)
            OUTPUT CONVERT(nvarchar(20), inserted.Id) WHERE Id = @id AND Etat = 'mise de cote';
            """, ct, ("@id", ligne), ("@par", par)) is not null;

    // --- V2 -> V3 : rec.RemonteeEchec --------------------------------------------------------------------------------

    /// <summary>Le refus d'une fiche de la V2 par la V3 : un essai de plus (une fiche « resolue » qui echoue a nouveau
    /// repart d'un premier essai) ; au seuil, mise de cote. Vrai si elle l'est.</summary>
    public static async Task<bool> RefuserV2Async(SqlConnection cn, string baseV2, string tableV2, string idV2, long version, string erreur, CancellationToken ct,
                                                  int seuil = Seuil)
        => await ScalaireAsync(cn, null, """
            MERGE rec.RemonteeEchec WITH (HOLDLOCK) AS r
            USING (SELECT @b AS BaseV2, @t AS TableV2, @id AS IdV2) AS s ON r.BaseV2 = s.BaseV2 AND r.TableV2 = s.TableV2 AND r.IdV2 = s.IdV2
            -- Une seule clause WHEN MATCHED ... UPDATE par MERGE (« An action of type 'WHEN MATCHED' cannot appear more than once
            -- in a 'UPDATE' clause », mesure par RecopieDeuxSensCheck) : une fiche « resolue » qui echoue de nouveau repart d'un
            -- premier essai, par les CASE.
            WHEN MATCHED THEN UPDATE SET
                Essais = CASE WHEN r.Etat = 'resolue' THEN 1 ELSE r.Essais + 1 END,
                Etat = CASE WHEN (CASE WHEN r.Etat = 'resolue' THEN 1 ELSE r.Essais + 1 END) >= @seuil THEN 'mise de cote' ELSE 'en echec' END,
                MiseDeCoteLe = CASE WHEN (CASE WHEN r.Etat = 'resolue' THEN 1 ELSE r.Essais + 1 END) >= @seuil THEN SYSDATETIME() ELSE NULL END,
                Version = CASE WHEN r.Etat = 'resolue' THEN @v ELSE r.Version END,
                PremierEchecLe = CASE WHEN r.Etat = 'resolue' THEN SYSDATETIME() ELSE r.PremierEchecLe END,
                RelanceLe = CASE WHEN r.Etat = 'resolue' THEN NULL ELSE r.RelanceLe END,
                RelancePar = CASE WHEN r.Etat = 'resolue' THEN NULL ELSE r.RelancePar END,
                ResolueLe = NULL, Erreur = LEFT(@e, 2000), DernierEssaiLe = SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT (BaseV2, TableV2, IdV2, Version, Etat, Essais, Erreur)
                VALUES (@b, @t, @id, @v, CASE WHEN 1 >= @seuil THEN 'mise de cote' ELSE 'en echec' END, 1, LEFT(@e, 2000))
            OUTPUT inserted.Etat;
            """, ct, ("@b", baseV2), ("@t", tableV2), ("@id", idV2), ("@v", version), ("@e", erreur), ("@seuil", seuil)) == "mise de cote";

    /// <summary>Une fiche de la V2 qui ATTEND une fiche refusee ou mise de cote qu'elle vise : sans essai, mais notee « en
    /// echec » pour etre rejouee aux passages suivants -- le point de reprise de la remontee avance, son changement ne doit
    /// pas se perdre (mesure par RecopieDeuxSensCheck : l'action creee dans la V2 sur un dossier refuse par la V3).</summary>
    public static Task AttendreV2Async(SqlConnection cn, string baseV2, string tableV2, string idV2, long version, string motif, CancellationToken ct)
        => ExecuterAsync(cn, null, """
            MERGE rec.RemonteeEchec WITH (HOLDLOCK) AS r
            USING (SELECT @b AS BaseV2, @t AS TableV2, @id AS IdV2) AS s ON r.BaseV2 = s.BaseV2 AND r.TableV2 = s.TableV2 AND r.IdV2 = s.IdV2
            WHEN MATCHED THEN UPDATE SET Erreur = LEFT(@e, 2000), DernierEssaiLe = SYSDATETIME(), ResolueLe = NULL,
                Etat = CASE WHEN r.Etat = 'resolue' THEN 'en echec' ELSE r.Etat END,
                Essais = CASE WHEN r.Etat = 'resolue' THEN 0 ELSE r.Essais END,
                Version = CASE WHEN r.Etat = 'resolue' THEN @v ELSE r.Version END
            WHEN NOT MATCHED THEN INSERT (BaseV2, TableV2, IdV2, Version, Etat, Essais, Erreur) VALUES (@b, @t, @id, @v, 'en echec', 0, LEFT(@e, 2000));
            """, ct, ("@b", baseV2), ("@t", tableV2), ("@id", idV2), ("@v", version), ("@e", motif));

    /// <summary>La fiche de la V2 est passee : son echec est resolu (dans la transaction de la fiche).</summary>
    public static Task ResoudreV2Async(SqlConnection cn, SqlTransaction tx, string baseV2, string tableV2, string idV2, CancellationToken ct)
        => ExecuterAsync(cn, tx, "UPDATE rec.RemonteeEchec SET Etat = 'resolue', ResolueLe = SYSDATETIME() WHERE BaseV2 = @b AND TableV2 = @t AND IdV2 = @id AND Etat = 'en echec';",
                         ct, ("@b", baseV2), ("@t", tableV2), ("@id", idV2));

    /// <summary>La relance d'une fiche de la V2 mise de cote (CA03) ; faux si elle ne l'est pas.</summary>
    public static async Task<bool> RelancerV2Async(SqlConnection cn, string baseV2, string tableV2, string idV2, string par, CancellationToken ct)
        => await ScalaireAsync(cn, null, """
            UPDATE rec.RemonteeEchec SET Etat = 'en echec', Essais = 0, RelanceLe = SYSDATETIME(), RelancePar = LEFT(@par, 128)
            OUTPUT inserted.Etat WHERE BaseV2 = @b AND TableV2 = @t AND IdV2 = @id AND Etat = 'mise de cote';
            """, ct, ("@b", baseV2), ("@t", tableV2), ("@id", idV2), ("@par", par)) is not null;

    // --- la liste de l'exploitation ----------------------------------------------------------------------------------

    /// <summary>Les changements mis de cote d'une base V3 (et, pour une relance, celui qu'on vient de relancer).</summary>
    public static async Task<ChangementMisDeCote[]> ListerAsync(SqlConnection cn, CancellationToken ct, long? ligne = null, string? tableV2 = null, string? idV2 = null)
    {
        var tables = ReglesRecopie.Decouvrir().ToDictionary(r => r.TableV3, r => r.TableV2, StringComparer.OrdinalIgnoreCase);
        var liste = new List<ChangementMisDeCote>();
        await using var cmd = new SqlCommand("""
            SELECT 'V3 vers V2', f.Id, f.TableV3, f.IdV3, f.Essais, f.Erreur, f.MiseDeCoteLe,
                   (SELECT COUNT(*) FROM rec.FileRecopie AS d WHERE d.TableV3 = f.TableV3 AND d.IdV3 = f.IdV3 AND d.Etat = 'en attente' AND d.Id > f.Id),
                   f.RelanceLe, f.RelancePar
            FROM rec.FileRecopie AS f WHERE f.Etat = 'mise de cote' OR f.Id = @ligne
            UNION ALL
            SELECT 'V2 vers V3', NULL, r.TableV2, r.IdV2, r.Essais, r.Erreur, r.MiseDeCoteLe, 0, r.RelanceLe, r.RelancePar
            FROM rec.RemonteeEchec AS r WHERE r.Etat = 'mise de cote' OR (r.TableV2 = @t AND r.IdV2 = @id)
            ORDER BY 7, 2;
            """, cn);
        cmd.Parameters.AddWithValue("@ligne", (object?)ligne ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@t", (object?)tableV2 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", (object?)idV2 ?? DBNull.Value);
        await using var l = await cmd.ExecuteReaderAsync(ct);
        while (await l.ReadAsync(ct))
        {
            var sens = l.GetString(0);
            var table = l.GetString(2);
            if (sens == RoutesMisesDeCote.SensV3VersV2) table = tables.TryGetValue(table, out var t2) ? t2 : table[(table.IndexOf('.') + 1)..];
            liste.Add(new ChangementMisDeCote(sens, l.IsDBNull(1) ? null : l.GetInt64(1), table, l.GetString(3), l.GetInt32(4),
                l.IsDBNull(5) ? null : l.GetString(5), l.IsDBNull(6) ? null : l.GetDateTime(6), l.GetInt32(7),
                l.IsDBNull(8) ? null : l.GetDateTime(8), l.IsDBNull(9) ? null : l.GetString(9)));
        }
        return [.. liste];
    }
}
