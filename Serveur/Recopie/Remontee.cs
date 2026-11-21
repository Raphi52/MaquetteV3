// fix-ok: fichier NEUF du lot 9 retouche deux fois avant tout essai : la famille du journal doit exister dans
// soc.FamilleJournal (FK_Journal_Famille, SOC-016_gestes.sql:49 : « fiche-partagee ») ; puis l'avertissement CS8604 de
// la compilation (dotnet build Serveur, 2026-11-21) sur idV3, connu des que la fiche V3 est lue.
using System.Data;
using Microsoft.Data.SqlClient;
using RigV3.Serveur.Socle.Comptes;
using static RigV3.Serveur.Recopie.EcrituresRecopie;

namespace RigV3.Serveur.Recopie;

/// <param name="Remontees">Fiches ecrites dans la V3 (creees, modifiees ou supprimees).</param>
/// <param name="Echos">Changements de la V2 reconnus comme des ecritures de la recopie, ou sans effet : ignores (CA05).</param>
/// <param name="Laissees">Fiches que la V3 a aussi changees et pas encore recopiees : l'applicateur les traite au passage
/// suivant, la V3 gagne et l'ecart est ecrit (REC-004).</param>
/// <param name="Version">La version du suivi des changements de la V2 jusqu'ou tout est remonte.</param>
public sealed record BilanRemontee(int Changements, int Remontees, int Echos, int Laissees, long? Version, string? Erreur);

/// <summary>
/// LE LECTEUR DE CHANGEMENTS V2 -> V3 (REC-003 T02). La V2 n'est pas modifiee : ses ecritures sont captees par la base
/// elle-meme, le suivi des changements de SQL Server (CA02, Base\v2\recopie-v2.sql). A chaque passage :
///   1. la version courante du suivi de la base V2, et pour chaque table partagee la version minimale encore gardee : si
///      elle depasse le point de reprise, des changements sont perdus -- ARRET, la base est a comparer (REC-006) ;
///   2. les changements depuis le point de reprise (CHANGETABLE), dans l'ordre de leur version ; une fiche qui en vise
///      une autre encore a remonter la fait passer avant elle (CA03) ;
///   3. chaque fiche dans UNE transaction de la base V3, sous la marque de la remontee (le declencheur de file ne la
///      renvoie pas vers la V2) : creee avec sa correspondance (CA03), modifiee colonne par colonne, ou supprimee ;
///      l'ecriture va au journal des gestes au nom de la recopie (SOC-016 CA06) ;
///   4. le point de reprise avance a la version lue, apres le lot.
/// La V3 arretee une heure ne coute rien a la V2 (le suivi s'y ecrit dans la transaction de l'agent, sans attendre la V3)
/// et rien ne manque au retour tant que la retention du suivi couvre l'arret (CA04).
/// Une fiche dont l'etat V2 est exactement l'etat commun du dernier passage n'a rien a remonter : c'est une ecriture de la
/// recopie V3 -> V2 (ou un aller-retour sans effet) -- elle est ignoree (CA05). Une fiche que la V3 a aussi changee et pas
/// encore recopiee est laissee a l'applicateur : la V3 gagne (REC-004).
/// </summary>
internal static class Remontee
{
    private sealed record Changement(TableRecopiee Table, string IdV2, long Version, string Operation);

    public static async Task<BilanRemontee> RemonterAsync(string connexionV3, FichesRecopiees fiches, CancellationToken ct)
    {
        await using var cn = new SqlConnection(connexionV3);
        await cn.OpenAsync(ct);
        var b = fiches.BaseV2;
        var point = await ScalaireAsync(cn, null, "SELECT CONVERT(nvarchar(20), Version) FROM rec.Remontee WHERE BaseV2 = @b;", ct, ("@b", b)) is { } p
            ? long.Parse(p) : throw new InvalidOperationException($"la recopie de {b} n'est pas en service (rec.Remontee)");
        var courante = await PontRecopie.VersionCouranteAsync(cn, b, ct)
                       ?? throw new InvalidOperationException($"le suivi des changements n'est plus active sur la base {b}");
        foreach (var t in fiches.Tables)
        {
            var min = await PontRecopie.VersionMinimaleAsync(cn, null, b, t, ct);
            if (min is null) return new BilanRemontee(0, 0, 0, 0, point, $"{t.NomV2} : le suivi des changements n'est plus active sur la table de la V2");
            if (min > point)
                return new BilanRemontee(0, 0, 0, 0, point, $"{t.NomV2} : la V2 ne garde plus ses changements depuis la version {point} (minimum {min}) : " +
                                                            "des ecritures de la V2 sont perdues pour la V3, la base est a comparer (REC-006)");
        }
        if (courante == point)
        {
            await ExecuterAsync(cn, null, "UPDATE rec.Remontee SET DernierPassage = SYSDATETIME() WHERE BaseV2 = @b;", ct, ("@b", b));
            return new BilanRemontee(0, 0, 0, 0, point, null);
        }

        var changements = new List<Changement>();
        foreach (var t in fiches.Tables)
        {
            await using var cmd = new SqlCommand(
                $"SELECT {Sql.EnTexte("c", t.Cle)}, c.SYS_CHANGE_VERSION, c.SYS_CHANGE_OPERATION FROM CHANGETABLE(CHANGES {t.NomV2Sql}, @point) AS c " +
                "WHERE c.SYS_CHANGE_VERSION <= @courante;", cn) { CommandTimeout = 300 };
            cmd.Parameters.AddWithValue("@point", point);
            cmd.Parameters.AddWithValue("@courante", courante);
            await using var l = await cmd.ExecuteReaderAsync(ct);
            while (await l.ReadAsync(ct)) changements.Add(new Changement(t, l.GetString(0), l.GetInt64(1), l.GetString(2)));
        }
        var parFiche = changements.ToDictionary(c => (c.Table.NomV2, c.IdV2));
        var compte = new Compte();
        var faites = new HashSet<(string, string)>();
        foreach (var c in changements.OrderBy(c => c.Version).ThenBy(c => c.Table.NomV2, StringComparer.Ordinal))
            try
            {
                await FicheAsync(cn, fiches, c, parFiche, [], faites, compte, ct);
            }
            catch (Exception e) when (e is SqlException or RecopieEnAttente)
            {
                // Le point de reprise n'avance pas : les fiches deja remontees seront reconnues (etat commun) au passage suivant.
                await RetirerMarqueAsync(cn);
                return new BilanRemontee(changements.Count, compte.Remontees, compte.Echos, compte.Laissees, point, $"{c.Table.NomV2} {c.IdV2} : {e.Message}");
            }
        await ExecuterAsync(cn, null, "UPDATE rec.Remontee SET Version = @v, DernierPassage = SYSDATETIME() WHERE BaseV2 = @b AND Version = @p;", ct,
                            ("@v", courante), ("@b", b), ("@p", point));
        return new BilanRemontee(changements.Count, compte.Remontees, compte.Echos, compte.Laissees, courante, null);
    }

    private sealed class Compte { public int Remontees, Echos, Laissees; }

    private static async Task FicheAsync(SqlConnection cn, FichesRecopiees fiches, Changement c, Dictionary<(string, string), Changement> parFiche,
                                         HashSet<(string, string)> pile, HashSet<(string, string)> faites, Compte compte, CancellationToken ct)
    {
        var t = c.Table;
        if (faites.Contains((t.NomV2, c.IdV2)) || !pile.Add((t.NomV2, c.IdV2))) return;

        // 1. Les fiches visees qui remontent dans ce lot passent d'abord (CA03).
        if (await LireV2Async(cn, null, t, c.IdV2, false, ct) is { } avantTout)
            foreach (var (colonne, visee) in t.Liens)
                if (avantTout.TryGetValue(colonne, out var brut) && TableRecopiee.TexteDe(brut) is { } valeur
                    && await IdV3Async(cn, null, visee, valeur, ct) is null && parFiche.TryGetValue((visee.NomV2, valeur), out var avant))
                    await FicheAsync(cn, fiches, avant, parFiche, pile, faites, compte, ct);

        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var idV3 = await IdV3Async(cn, tx, t, c.IdV2, ct);
            if (idV3 is not null && await ScalaireAsync(cn, tx,
                    "SELECT CONVERT(nvarchar(20), MIN(Id)) FROM rec.FileRecopie WHERE TableV3 = @t AND IdV3 = @id AND Etat = 'en attente';",
                    ct, ("@t", t.NomV3), ("@id", idV3)) is not null)
            {
                compte.Laissees++;
            }
            else if (await LireV2Async(cn, tx, t, c.IdV2, false, ct) is not { } v2)
            {
                // Supprimee dans la V2.
                if (idV3 is not null)
                {
                    if (await ExisteV3Async(cn, tx, t, idV3, ct))
                    {
                        await SupprimerV3Async(cn, tx, t, idV3, ct);
                        await JournalAsync(cn, tx, t, idV3, "a supprime", null, ct);
                        compte.Remontees++;
                    }
                    await OublierAsync(cn, tx, t, c.IdV2, ct);
                }
                else compte.Echos++;
            }
            else if (await EtatAsync(cn, tx, t, c.IdV2, ct) is { } etat && LigneJson.Differences(v2, etat, t.Colonnes.Select(x => x.Nom)).Count == 0)
            {
                compte.Echos++;   // CA05 : l'etat que la recopie vient d'ecrire, ou rien de neuf
            }
            else
            {
                var enV3 = await TraduireAsync(cn, tx, t, v2, versV2: false, ct);
                LigneJson? v3 = idV3 is null ? null : await LireV3Async(cn, tx, t, idV3, true, ct);
                if (v3 is null)
                {
                    // CA03 : creee dans la V2 -> creee dans la V3 avec sa correspondance.
                    var idTransfert = int.Parse((await ScalaireAsync(cn, tx, "SELECT CONVERT(nvarchar(20), IdTransfert) FROM rec.Remontee WHERE BaseV2 = @b;", ct,
                                                                     ("@b", t.BaseV2)))!);
                    var avecPropres = await TraduireAsync(cn, tx, t,
                        LigneJson.Lire(await ScalaireAsync(cn, tx, t.SelectJson(t.NomV2Sql, t.Colonnes.Concat(t.Propres), "@id"), ct, ("@id", c.IdV2)))!,
                        versV2: false, ct);
                    idV3 = await CreerV3Async(cn, tx, t, c.IdV2, avecPropres, ct);
                    await CorrespondreAsync(cn, tx, t, c.IdV2, idV3, idTransfert, "remontee", ct);
                    await JournalAsync(cn, tx, t, idV3, "a cree", Lisibles(enV3, t.Colonnes.Select(x => x.Nom)), ct);
                    compte.Remontees++;
                }
                else
                {
                    var aEcrire = LigneJson.Differences(enV3, v3, t.Colonnes.Select(x => x.Nom));
                    if (aEcrire.Count > 0)
                    {
                        // v3 lue -> idV3 connu
                        await ModifierV3Async(cn, tx, t, idV3!, enV3, aEcrire, ct);
                        await JournalAsync(cn, tx, t, idV3!, "a modifie", Lisibles(enV3, aEcrire), ct, Lisibles(v3, aEcrire));
                        compte.Remontees++;
                    }
                    else compte.Echos++;
                }
                await NoterEtatAsync(cn, tx, t, c.IdV2, v2, "V2 vers V3", ct);
            }
            await tx.CommitAsync(ct);
            faites.Add((t.NomV2, c.IdV2));
        }
        catch
        {
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static Dictionary<string, string?> Lisibles(LigneJson l, IEnumerable<string> colonnes)
        => colonnes.Where(l.ContainsKey).ToDictionary(c => c, c => LigneJson.Lisible(l[c]));

    /// <summary>Le geste au journal de la V3, au nom de la recopie V2 (SOC-016 CA06). La V2 ne note pas qui modifie ses
    /// fiches partagees : le compte d'origine est inconnu, et le journal le dit.</summary>
    private static Task JournalAsync(SqlConnection cn, SqlTransaction tx, TableRecopiee t, string idV3, string verbe, object? apres, CancellationToken ct,
                                     object? avant = null)
        => JournalGestes.EcrireRecopieAsync(cn, tx, "non note par la V2", "fiche-partagee", $"{t.Regle.Libelle} {idV3}", idV3, verbe, avant, apres, ct);
}
