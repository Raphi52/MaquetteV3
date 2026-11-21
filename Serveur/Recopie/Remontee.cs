// fix-ok: fichier NEUF du lot 9 retouche deux fois avant tout essai : la famille du journal doit exister dans
// soc.FamilleJournal (FK_Journal_Famille, SOC-016_gestes.sql:49 : « fiche-partagee ») ; puis l'avertissement CS8604 de
// la compilation (dotnet build Serveur, 2026-11-21) sur idV3, connu des que la fiche V3 est lue.
// fix-ok: refus du chef (21/11) mesure par RecopieDeuxSensCheck sur l'ancien code : le client « REFUSE PAR LA V3 », refuse par
// la contrainte CK_BANC_REC005_V3 de la V3, arretait la remontee a chaque passage (« return ») -- 0 remontee, point de reprise
// bloque a 366 pour une version courante de 370 (REC-005 CA02 rouge). Un refus est desormais note dans rec.RemonteeEchec.
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
/// <param name="Erreur">Ce qui a arrete le passage : une PANNE (base injoignable, suivi des changements perdu) ; un refus de la
/// V3 n'arrete plus rien (REC-005 CA02).</param>
/// <param name="Refusees">Fiches de la V2 que la V3 a refusees pendant ce passage (un essai de plus, rec.RemonteeEchec).</param>
/// <param name="MisesDeCote">Parmi elles, celles mises de cote a ce passage (3e essai).</param>
/// <param name="Bloquees">Changements qui attendent derriere une fiche mise de cote (la leur, ou une fiche visee).</param>
/// <param name="TableArret">La table V2 de la fiche sur laquelle une panne a arrete le passage.</param>
public sealed record BilanRemontee(int Changements, int Remontees, int Echos, int Laissees, long? Version, string? Erreur,
                                   int Refusees = 0, int MisesDeCote = 0, int Bloquees = 0, string? TableArret = null);

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
/// Une fiche que la V3 REFUSE (contrainte, declencheur) ne retient pas les autres : elle est notee dans rec.RemonteeEchec
/// (un essai par passage, rejouee de son etat COURANT dans la V2), le point de reprise avance quand meme ; au 3e essai elle
/// est mise de cote, ses changements suivants attendent derriere elle, et la relance de l'exploitant la fait repasser avec
/// eux (REC-005 CA02, CA03, <see cref="MisesDeCote"/>). Une PANNE arrete le passage, sans essai, le point n'avance pas.
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
        // Les fiches que la V3 a refusees aux passages d'avant : « en echec », rejouees ici ; « mise de cote », elles attendent
        // la relance (et leurs changements suivants avec elles).
        var echecs = new Dictionary<(string, string), (long Version, string Etat)>();
        await using (var cmd = new SqlCommand("SELECT TableV2, IdV2, Version, Etat FROM rec.RemonteeEchec WHERE BaseV2 = @b AND Etat IN ('en echec', 'mise de cote');", cn))
        {
            cmd.Parameters.AddWithValue("@b", b);
            await using var l = await cmd.ExecuteReaderAsync(ct);
            while (await l.ReadAsync(ct)) echecs[(l.GetString(0), l.GetString(1))] = (l.GetInt64(2), l.GetString(3));
        }
        if (courante == point && !echecs.Values.Any(e => e.Etat == "en echec"))
        {
            await ExecuterAsync(cn, null, "UPDATE rec.Remontee SET DernierPassage = SYSDATETIME() WHERE BaseV2 = @b;", ct, ("@b", b));
            return new BilanRemontee(0, 0, 0, 0, point, null);
        }

        var parFiche = new Dictionary<(string, string), Changement>();
        if (courante > point)
            foreach (var t in fiches.Tables)
            {
                await using var cmd = new SqlCommand(
                    $"SELECT {Sql.EnTexte("c", t.Cle)}, c.SYS_CHANGE_VERSION, c.SYS_CHANGE_OPERATION FROM CHANGETABLE(CHANGES {t.NomV2Sql}, @point) AS c " +
                    "WHERE c.SYS_CHANGE_VERSION <= @courante;", cn) { CommandTimeout = 300 };
                cmd.Parameters.AddWithValue("@point", point);
                cmd.Parameters.AddWithValue("@courante", courante);
                await using var l = await cmd.ExecuteReaderAsync(ct);
                while (await l.ReadAsync(ct)) parFiche[(t.NomV2, l.GetString(0))] = new Changement(t, l.GetString(0), l.GetInt64(1), l.GetString(2));
            }
        // Une fiche en echec repasse a la version de son premier refus (son rang dans l'ordre), de son etat courant dans la V2.
        foreach (var ((table, idV2), (version, etat)) in echecs)
            if (etat == "en echec" && fiches.ParTableV2.TryGetValue(table, out var te) && !parFiche.ContainsKey((table, idV2)))
                parFiche[(table, idV2)] = new Changement(te, idV2, version, "U");
        var compte = new Compte();
        var faites = new HashSet<(string, string)>();
        var arretees = new HashSet<(string, string)>(echecs.Where(e => e.Value.Etat == "mise de cote").Select(e => e.Key));
        var ordre = parFiche.Values.Select(c => echecs.TryGetValue((c.Table.NomV2, c.IdV2), out var e) && e.Version < c.Version ? c with { Version = e.Version } : c)
                                   .OrderBy(c => c.Version).ThenBy(c => c.Table.NomV2, StringComparer.Ordinal).ToList();
        foreach (var c in ordre)
        {
            if (faites.Contains((c.Table.NomV2, c.IdV2))) continue;
            if (arretees.Contains((c.Table.NomV2, c.IdV2))) { compte.Bloquees++; continue; }
            try
            {
                await FicheAsync(cn, fiches, c, parFiche, [], faites, arretees, compte, ct);
            }
            catch (Exception e) when (!ct.IsCancellationRequested && e is SqlException or RecopieEnAttente or RecopieBloquee or EchecFiche)
            {
                await RetirerMarqueAsync(cn);
                var (tableEnCause, idEnCause, cause) = e is EchecFiche f ? (f.Table, f.Id, f.InnerException!) : (c.Table.NomV2, c.IdV2, e);
                if (cause is RecopieBloquee)
                {
                    // Elle attend sans essai ; notee « en echec » pour etre rejouee : le point de reprise avance quand meme.
                    arretees.Add((c.Table.NomV2, c.IdV2));
                    compte.Bloquees++;
                    await MisesDeCote.AttendreV2Async(cn, b, c.Table.NomV2, c.IdV2, c.Version, cause.Message, ct);
                    continue;
                }
                // Une panne : le point de reprise n'avance pas, les fiches deja remontees seront reconnues (etat commun) au
                // passage suivant.
                if (MisesDeCote.EstPanne(cause, cn))
                    return new BilanRemontee(parFiche.Count, compte.Remontees, compte.Echos, compte.Laissees, point, $"{tableEnCause} {idEnCause} : {cause.Message}",
                                             compte.Refusees, compte.MisesDeCote, compte.Bloquees, tableEnCause);
                // Le refus de la V3 : il ne coute qu'a la fiche refusee ; celle qui la vise attend.
                var version = parFiche.TryGetValue((tableEnCause, idEnCause), out var cc) ? cc.Version : c.Version;
                if (echecs.TryGetValue((tableEnCause, idEnCause), out var dejaEnEchec) && dejaEnEchec.Version < version) version = dejaEnEchec.Version;
                arretees.Add((tableEnCause, idEnCause));
                arretees.Add((c.Table.NomV2, c.IdV2));
                compte.Refusees++;
                if (await MisesDeCote.RefuserV2Async(cn, b, tableEnCause, idEnCause, version, cause.Message, ct)) compte.MisesDeCote++;
                if ((tableEnCause, idEnCause) != (c.Table.NomV2, c.IdV2))
                {
                    compte.Bloquees++;
                    await MisesDeCote.AttendreV2Async(cn, b, c.Table.NomV2, c.IdV2, c.Version, $"attend {tableEnCause} {idEnCause}, refusee par la V3 : {cause.Message}", ct);
                }
            }
        }
        if (courante > point)
            await ExecuterAsync(cn, null, "UPDATE rec.Remontee SET Version = @v, DernierPassage = SYSDATETIME() WHERE BaseV2 = @b AND Version = @p;", ct,
                                ("@v", courante), ("@b", b), ("@p", point));
        else
            await ExecuterAsync(cn, null, "UPDATE rec.Remontee SET DernierPassage = SYSDATETIME() WHERE BaseV2 = @b;", ct, ("@b", b));
        return new BilanRemontee(parFiche.Count, compte.Remontees, compte.Echos, compte.Laissees, courante, null, compte.Refusees, compte.MisesDeCote, compte.Bloquees);
    }

    private sealed class Compte { public int Remontees, Echos, Laissees, Refusees, MisesDeCote, Bloquees; }

    private static async Task FicheAsync(SqlConnection cn, FichesRecopiees fiches, Changement c, Dictionary<(string, string), Changement> parFiche,
                                         HashSet<(string, string)> pile, HashSet<(string, string)> faites, HashSet<(string, string)> arretees,
                                         Compte compte, CancellationToken ct)
    {
        var t = c.Table;
        if (faites.Contains((t.NomV2, c.IdV2)) || !pile.Add((t.NomV2, c.IdV2))) return;
        try
        {
            await FicheSansNomAsync(cn, fiches, c, parFiche, pile, faites, arretees, compte, ct);
        }
        // L'echec porte le nom de SA fiche : une fiche visee refusee compte son essai, pas celle qui la vise.
        catch (Exception e) when (!ct.IsCancellationRequested && e is SqlException or RecopieEnAttente)
        {
            throw new EchecFiche(t.NomV2, c.IdV2, e);
        }
    }

    private static async Task FicheSansNomAsync(SqlConnection cn, FichesRecopiees fiches, Changement c, Dictionary<(string, string), Changement> parFiche,
                                                HashSet<(string, string)> pile, HashSet<(string, string)> faites, HashSet<(string, string)> arretees,
                                                Compte compte, CancellationToken ct)
    {
        var t = c.Table;

        // 1. Les fiches visees qui remontent dans ce lot passent d'abord (CA03) ; une visee refusee ou mise de cote, et pas
        // encore dans la V3, fait attendre celle-ci.
        if (await LireV2Async(cn, null, t, c.IdV2, false, ct) is { } avantTout)
            foreach (var (colonne, visee) in t.Liens)
                if (avantTout.TryGetValue(colonne, out var brut) && TableRecopiee.TexteDe(brut) is { } valeur && await IdV3Async(cn, null, visee, valeur, ct) is null)
                {
                    if (arretees.Contains((visee.NomV2, valeur)))
                        throw new RecopieBloquee($"{t.NomV2} {c.IdV2} attend {visee.NomV2} {valeur} ({colonne}), refusee par la V3 ou mise de cote");
                    if (parFiche.TryGetValue((visee.NomV2, valeur), out var avant))
                        await FicheAsync(cn, fiches, avant, parFiche, pile, faites, arretees, compte, ct);
                }

        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var idV3 = await IdV3Async(cn, tx, t, c.IdV2, ct);
            // La V3 a un changement de cette fiche pas encore recopie (en file, ou mis de cote) : l'applicateur le traite, la
            // V3 gagne et l'ecart est ecrit (REC-004).
            if (idV3 is not null && await ScalaireAsync(cn, tx,
                    "SELECT CONVERT(nvarchar(20), MIN(Id)) FROM rec.FileRecopie WHERE TableV3 = @t AND IdV3 = @id AND Etat IN ('en attente', 'mise de cote');",
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
                    await JournalAsync(cn, tx, t, idV3, "a cree", Lisibles(enV3, t.Colonnes.Select(x => x.Nom)), ct,
                                       compteV2: (await AgentV2Async(cn, tx, t, v2, null, ct))?.Compte);
                    compte.Remontees++;
                }
                else
                {
                    var aEcrire = LigneJson.Differences(enV3, v3, t.Colonnes.Select(x => x.Nom));
                    if (aEcrire.Count > 0)
                    {
                        // v3 lue -> idV3 connu
                        await ModifierV3Async(cn, tx, t, idV3!, enV3, aEcrire, ct);
                        await JournalAsync(cn, tx, t, idV3!, "a modifie", Lisibles(enV3, aEcrire), ct, Lisibles(v3, aEcrire),
                                           (await AgentV2Async(cn, tx, t, v2, v3, ct))?.Compte);
                        compte.Remontees++;
                    }
                    else compte.Echos++;
                }
                await NoterEtatAsync(cn, tx, t, c.IdV2, v2, "V2 vers V3", ct);
            }
            // La fiche est passee : son refus d'un passage d'avant, s'il y en a un, est resolu (meme transaction).
            await MisesDeCote.ResoudreV2Async(cn, tx, t.BaseV2, t.NomV2, c.IdV2, ct);
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

    /// <summary>Le geste au journal de la V3, au nom de la recopie V2 et du compte V2 d'origine (SOC-016 CA06) : le compte
    /// de l'agent que la V2 a note sur la fiche pour ce changement (<see cref="EcrituresRecopie.AgentV2Async"/> : un client,
    /// CLIEN_ID_UTLST). Sinon le compte est inconnu, et le journal le dit : les autres fiches partagees n'ont pas de colonne
    /// d'agent dans la V2, un changement qui n'a note ni date ni agent ne dit pas qui l'a fait, une suppression n'en laisse rien.</summary>
    private static Task JournalAsync(SqlConnection cn, SqlTransaction tx, TableRecopiee t, string idV3, string verbe, object? apres, CancellationToken ct,
                                     object? avant = null, string? compteV2 = null)
        => JournalGestes.EcrireRecopieAsync(cn, tx, compteV2 ?? "non note par la V2", "fiche-partagee", $"{t.Regle.Libelle} {idV3}", idV3, verbe, avant, apres, ct);
}
