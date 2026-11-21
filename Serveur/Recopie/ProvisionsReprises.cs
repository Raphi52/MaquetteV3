using System.Data;
using Microsoft.Data.SqlClient;
using static RigV3.Serveur.Recopie.EcrituresRecopie;

namespace RigV3.Serveur.Recopie;

/// <summary>La consommation d'une provision reprise : faite, ou refusee avec son motif.</summary>
public sealed record ConsommationProvision(bool Consommee, string? Motif);

/// <summary>
/// UNE PROVISION REPRISE D'UN ANCIEN LOGICIEL NE SE CONSOMME QU'UNE FOIS, DANS UN LOGICIEL OU DANS L'AUTRE (REC-013 CA06, pour
/// FAC-005). La V2 la consomme par un UPDATE conditionnel (Encaissement.Sauve, RigMetier\Facture\Encaissement.cs:405-410 :
/// « Update ENCAISSEMENT_REPRIS Set ENRPS_CONSOMMER=1 Where ENRPS_NUMERO_TITRE= ... AND ENRPS_CONSOMMER = 0 ») ; la V2 ne voit
/// pas la V3. La V3 la RESERVE donc d'abord dans la V2, par le meme UPDATE conditionnel, dans sa propre transaction (meme
/// instance, Q4) : de deux consommations simultanees, la base V2 n'en laisse passer qu'une (verrou de ligne). Puis elle
/// l'ecrit chez elle, sous la marque de la remontee (rien a renvoyer : la V2 l'a deja), et note l'etat commun -- la
/// remontee reconnait l'ecriture. Si la V2 l'a consommee la premiere, la V3 refuse avec le motif, meme avant que la remontee
/// ne l'ait apprise ; si la V3 l'a consommee la premiere, l'UPDATE de la V2 ne touche plus aucune ligne. Le solde est le meme
/// des deux cotes. C'est la seule ecriture de la V3 dans la V2 qui n'attend pas la file : la reservation l'exige (une file
/// laisserait consommer deux fois).
/// </summary>
public static class ProvisionsReprises
{
    public static async Task<ConsommationProvision> ConsommerAsync(string connexionV3, string baseV2, string numeroTitre, CancellationToken ct = default)
    {
        var fiches = await FichesRecopiees.ChargerAsync(connexionV3, baseV2, null, ct);
        if (!fiches.ParTableV2.TryGetValue("ENCAISSEMENT_REPRIS", out var t))
            return new ConsommationProvision(false, $"les provisions reprises ne sont pas recopiees pour {baseV2} ({string.Join(" ; ", fiches.Problemes)})");
        await using var cn = new SqlConnection(connexionV3);
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            // 1. La reservation, dans la V2 : le premier qui la reserve la consomme.
            var reservee = await LignesAsync(cn, tx, $"WITH CHANGE_TRACKING_CONTEXT (@marque) UPDATE x SET x.ENRPS_CONSOMMER = 1 FROM {t.NomV2Sql} AS x " +
                                                     "WHERE x.ENRPS_NUMERO_TITRE = @n AND x.ENRPS_CONSOMMER = 0;", ct, ("@n", numeroTitre), ("@marque", PontRecopie.MarqueRecopie));
            if (reservee == 0)
            {
                var etat = await ScalaireAsync(cn, tx, $"SELECT CONVERT(nvarchar(10), ENRPS_CONSOMMER) FROM {t.NomV2Sql} WHERE ENRPS_NUMERO_TITRE = @n;", ct, ("@n", numeroTitre));
                await tx.RollbackAsync(CancellationToken.None);
                return new ConsommationProvision(false, etat is null
                    ? $"la provision {numeroTitre} n'existe pas dans l'ancien RIG"
                    : $"la provision {numeroTitre} est deja utilisee : l'ancien RIG ou la V3 l'a consommee le premier");
            }
            // 2. La V3, sous la marque de la remontee : la V2 l'a deja, rien ne part dans la file.
            await ExecuterAsync(cn, tx, $"EXEC sys.sp_set_session_context @key = N'{PontRecopie.CleRemontee}', @value = 1;", ct);
            var ecrite = await LignesAsync(cn, tx, $"UPDATE x SET x.{Sql.Q(t.ColonneV3(t.V2.Colonne("ENRPS_CONSOMMER")).Nom)} = 1 FROM {t.NomV3Sql} AS x " +
                                                   $"WHERE {t.CleEgaleV3("x", "@n")};", ct, ("@n", numeroTitre));
            await ExecuterAsync(cn, tx, $"EXEC sys.sp_set_session_context @key = N'{PontRecopie.CleRemontee}', @value = NULL;", ct);
            if (ecrite == 0)
            {
                await tx.RollbackAsync(CancellationToken.None);
                return new ConsommationProvision(false, $"la provision {numeroTitre} est dans l'ancien RIG mais pas encore dans la V3 : reessayer apres la recopie");
            }
            // 3. L'etat commun : la remontee reconnaitra ce changement de la V2 comme le sien (pas d'aller-retour).
            await NoterEtatAsync(cn, tx, t, numeroTitre, (await LireV2Async(cn, tx, t, numeroTitre, false, ct))!, "V3 vers V2", ct);
            await tx.CommitAsync(ct);
            return new ConsommationProvision(true, null);
        }
        catch
        {
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None);
            await RetirerMarqueAsync(cn);
            throw;
        }
    }

    private static async Task<int> LignesAsync(SqlConnection cn, SqlTransaction tx, string sql, CancellationToken ct, params (string Nom, object Valeur)[] p)
    {
        await using var cmd = new SqlCommand(sql, cn, tx) { CommandTimeout = 120 };
        foreach (var (n, v) in p) cmd.Parameters.Add(v is byte[] b ? new SqlParameter(n, SqlDbType.VarBinary, 128) { Value = b } : new SqlParameter(n, v));
        return await cmd.ExecuteNonQueryAsync(ct);
    }
}
