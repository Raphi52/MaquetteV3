// fix-ok: fichier NEUF du lot 9, ecrit en une fois puis retouche deux fois a la relecture avant compilation : les tuples
// des champs perdus passaient une valeur non nullable la ou la liste attend (string?) (avertissement CS8620 a la compilation).
// Puis la date V2 d'un ecart sans date de mise a jour : DateTime.Now du programme, alors que le SQL Server des bancs (Docker)
// est a l'heure UTC -- mesure le 2026-11-21 : SYSDATETIME() 08:52, poste 09:52 -- ; elle vient desormais de SYSDATETIME().
// Puis l'auteur V2 d'un ecart : ecrit NULL en dur, alors que la V2 note l'agent d'un client (CLIEN_ID_UTLST) -- mesure par
// RecopieDeuxSensCheck (REC-004 CA01 rouge : CLIEN_ID_UTLST 1 -> 189, AuteurV2 vide) ; il est lu par RegleRecopie.ColonneAuteurV2.
// fix-ok: refus du chef (21/11) mesure par RecopieDeuxSensCheck sur l'ancien code : le client « REFUSE PAR LA V2 », refuse par
// la contrainte CK_BANC_REC005, arretait tout le passage au premier echec (« return » ici) -- SUIT LE REFUS 1 et 2 n'arrivaient
// jamais dans la V2 (REC-005 CA02 rouge). Une panne arrete toujours le passage, un refus ne coute plus qu'a sa fiche.
using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using static RigV3.Serveur.Recopie.EcrituresRecopie;

namespace RigV3.Serveur.Recopie;

/// <param name="Appliquees">Lignes de file appliquees.</param>
/// <param name="Fiches">Fiches recopiees (plusieurs lignes d'une meme fiche passent ensemble).</param>
/// <param name="Ecarts">Fiches modifiees des deux cotes : la V3 a gagne, l'ecart est ecrit (REC-004).</param>
/// <param name="Erreur">Ce qui a arrete le passage : une PANNE (base V2 muette, injoignable) ; null sinon -- un refus de la
/// V2 n'arrete plus rien (REC-005 CA02).</param>
/// <param name="Refusees">Fiches dont le changement de tete a ete refuse par la V2 pendant ce passage (un essai de plus).</param>
/// <param name="MisesDeCote">Parmi elles, celles mises de cote a ce passage (3e essai).</param>
/// <param name="Bloquees">Fiches qui attendent derriere un changement mis de cote (le leur, ou celui d'une fiche visee).</param>
/// <param name="TableArret">La table V2 de la fiche sur laquelle une panne a arrete le passage.</param>
public sealed record BilanApplication(int Appliquees, int Fiches, int Ecarts, string? Erreur, long? LigneArretee,
                                      int Refusees = 0, int MisesDeCote = 0, int Bloquees = 0, string? TableArret = null);

/// <summary>
/// L'APPLICATEUR V3 -> V2 (REC-002 T02, REC-004 T02). Il lit rec.FileRecopie dans l'ordre et recopie chaque fiche dans la
/// base V2 du greffe, sur la meme instance, dans UNE transaction par fiche : la fiche V2, sa correspondance, son etat
/// commun et ses lignes de file passent ensemble ou pas du tout.
///
///  - ORDRE GARANTI. Les fiches passent dans l'ordre de leur premiere ligne en attente. Avant une fiche, toute fiche
///    qu'elle vise (une action du registre et son dossier) et qui attend encore passe d'abord : la ligne visee existe
///    dans la V2 avant celle qui la vise, aucune erreur de cle etrangere (CA03). Une PANNE de la V2 (muette, hors ligne)
///    ARRETE le passage sans compter d'essai : tout reste en file avec le motif (CA06). Un REFUS de la V2 (contrainte,
///    declencheur) ne coute qu'a sa fiche : un essai de plus, mise de cote au 3e, les autres fiches continuent et les
///    changements suivants de la meme fiche attendent derriere lui (REC-005 CA02, <see cref="MisesDeCote"/>). La V3, elle,
///    n'attend jamais la V2 : elle n'ecrit que la file.
///  - CREATION (CA02). Une fiche sans correspondance est creee dans la V2, qui lui donne son identifiant ; la
///    correspondance est ecrite ; un changement suivant met a jour CETTE ligne.
///  - TRANSMISSION (CA04). Les colonnes propres (etat de transmission) ne sont jamais recopiees et sont remises a leur
///    valeur d'avant si le declencheur de la V2 les marque « a transmettre » ; une fiche creee recoit l'etat « N ».
///  - PAS D'ALLER-RETOUR (CA05). Apres l'ecriture, la fiche V2 est relue (declencheurs de la V2 compris) : la V3 recoit
///    ce que ces declencheurs ont ecrit, sous la marque de la remontee, et l'etat commun est note. La remontee reconnait
///    alors l'ecriture : la V2 a exactement l'etat commun, elle n'a rien a remonter.
///  - CONFLIT (REC-004). Si la fiche V2 n'a plus l'etat commun (son dernier passage, ou l'etat d'avant le premier
///    changement V3 en attente), la V2 l'a changee entre deux passages : la V3 gagne sur la fiche entiere, et l'ecart
///    est ecrit avec chaque champ que la V2 avait change (valeur perdue, valeur gardee, dates, auteurs) (CA01, CA04).
/// </summary>
internal static class Applicateur
{
    private sealed record LigneFile(long Id, string Genre, string? Avant, DateTime Le, string Auteur);

    public static async Task<BilanApplication> AppliquerAsync(string connexionV3, FichesRecopiees fiches, int idTransfert, int auPlus, CancellationToken ct)
    {
        await using var cn = new SqlConnection(connexionV3);
        await cn.OpenAsync(ct);
        var attente = new List<(long Id, string TableV3, string IdV3)>();
        // Une fiche qui a un changement mis de cote ne passe pas : ses changements suivants attendent derriere lui.
        await using (var cmd = new SqlCommand(
            $"SELECT TOP ({auPlus}) f.Id, f.TableV3, f.IdV3 FROM rec.FileRecopie AS f WHERE f.Etat = 'en attente' AND NOT EXISTS " +
            "(SELECT 1 FROM rec.FileRecopie AS m WHERE m.TableV3 = f.TableV3 AND m.IdV3 = f.IdV3 AND m.Etat = 'mise de cote') ORDER BY f.Id;", cn))
        await using (var l = await cmd.ExecuteReaderAsync(ct))
            while (await l.ReadAsync(ct)) attente.Add((l.GetInt64(0), l.GetString(1), l.GetString(2)));

        var bilan = new Compte();
        var faites = new HashSet<(string, string)>();
        // Les fiches refusees ou bloquees a ce passage : un seul essai par passage, et celles qui les visent attendent.
        var arretees = new HashSet<(string, string)>();
        foreach (var (id, tableV3, idV3) in attente)
        {
            if (faites.Contains((tableV3, idV3)) || arretees.Contains((tableV3, idV3))) continue;
            try
            {
                if (!fiches.ParTableV3.TryGetValue(tableV3, out var t))
                    throw new RecopieBloquee($"{tableV3} n'est plus dans la recopie de {fiches.BaseV2} ({string.Join(" ; ", fiches.Problemes)})");
                await FicheAsync(cn, fiches, t, idV3, idTransfert, [], faites, arretees, bilan, ct);
            }
            // Une annulation (arret du service) n'est ni un refus ni une panne : elle remonte, la transaction de la fiche est
            // annulee et tout repart du dernier changement applique au demarrage suivant (REC-005 CA01).
            catch (Exception e) when (!ct.IsCancellationRequested && e is SqlException or RecopieEnAttente or RecopieBloquee or EchecFiche)
            {
                await RetirerMarqueAsync(cn);
                var (tableEnCause, idEnCause, cause) = e is EchecFiche f ? (f.Table, f.Id, f.InnerException!) : (tableV3, idV3, e);
                var nomV2 = fiches.ParTableV3.TryGetValue(tableEnCause, out var tc) ? tc.NomV2 : tableEnCause;
                if (cause is RecopieBloquee)
                {
                    arretees.Add((tableV3, idV3));
                    await MisesDeCote.NoterMotifV3Async(cn, tableV3, idV3, cause.Message, ct);
                    bilan.Bloquees++;
                    continue;
                }
                if (MisesDeCote.EstPanne(cause, cn))
                {
                    await MisesDeCote.NoterMotifV3Async(cn, tableEnCause, idEnCause, cause.Message, ct);
                    return new BilanApplication(bilan.Lignes, bilan.Fiches, bilan.Ecarts, $"{tableEnCause} {idEnCause} (ligne {id}) : {cause.Message}", id,
                                                bilan.Refusees, bilan.MisesDeCote, bilan.Bloquees, nomV2);
                }
                // Le refus de la V2 : il ne coute qu'a la fiche refusee (la visee, si c'est elle) ; celle qui la vise attend.
                arretees.Add((tableEnCause, idEnCause));
                arretees.Add((tableV3, idV3));
                bilan.Refusees++;
                if (await MisesDeCote.RefuserV3Async(cn, tableEnCause, idEnCause, cause.Message, ct)) bilan.MisesDeCote++;
                if ((tableEnCause, idEnCause) != (tableV3, idV3))
                    await MisesDeCote.NoterMotifV3Async(cn, tableV3, idV3, $"attend {nomV2} {idEnCause}, refuse par la V2 : {cause.Message}", ct);
            }
        }
        return new BilanApplication(bilan.Lignes, bilan.Fiches, bilan.Ecarts, null, null, bilan.Refusees, bilan.MisesDeCote, bilan.Bloquees);
    }

    private sealed class Compte { public int Lignes, Fiches, Ecarts, Refusees, MisesDeCote, Bloquees; }

    private static async Task FicheAsync(SqlConnection cn, FichesRecopiees fiches, TableRecopiee t, string idV3, int idTransfert,
                                         HashSet<(string, string)> pile, HashSet<(string, string)> faites, HashSet<(string, string)> arretees,
                                         Compte bilan, CancellationToken ct)
    {
        if (faites.Contains((t.NomV3, idV3)) || !pile.Add((t.NomV3, idV3))) return;
        try
        {
            await FicheSansNomAsync(cn, fiches, t, idV3, idTransfert, pile, faites, arretees, bilan, ct);
        }
        // L'echec porte le nom de SA fiche : une fiche visee refusee compte son essai, pas celle qui la vise.
        catch (Exception e) when (!ct.IsCancellationRequested && e is SqlException or RecopieEnAttente)
        {
            throw new EchecFiche(t.NomV3, idV3, e);
        }
    }

    private static async Task FicheSansNomAsync(SqlConnection cn, FichesRecopiees fiches, TableRecopiee t, string idV3, int idTransfert,
                                                HashSet<(string, string)> pile, HashSet<(string, string)> faites, HashSet<(string, string)> arretees,
                                                Compte bilan, CancellationToken ct)
    {
        // 1. Les fiches visees d'abord (CA03), chacune dans sa transaction. Une visee mise de cote, ou refusee a ce passage,
        // fait attendre celle-ci : la ligne visee doit exister dans la V2 avant elle.
        if (await LireV3Async(cn, null, t, idV3, false, ct) is { } avantTout)
            foreach (var (colonne, visee) in t.Liens)
            {
                if (!avantTout.TryGetValue(colonne, out var brut) || TableRecopiee.TexteDe(brut) is not { } valeur) continue;
                if (await IdV2Async(cn, null, visee, valeur, ct) is not null || !await ExisteV3Async(cn, null, visee, valeur, ct)) continue;
                if (arretees.Contains((visee.NomV3, valeur)) || await MisesDeCote.BloqueeV3Async(cn, null, visee.NomV3, valeur, ct))
                    throw new RecopieBloquee($"{t.NomV2} {idV3} attend {visee.NomV2} {valeur} ({colonne}), dont un changement est refuse par la V2 ou mis de cote");
                if (await ScalaireAsync(cn, null, "SELECT CONVERT(nvarchar(20), MIN(Id)) FROM rec.FileRecopie WHERE TableV3 = @t AND IdV3 = @id AND Etat = 'en attente';",
                                        ct, ("@t", visee.NomV3), ("@id", valeur)) is null)
                    throw new RecopieEnAttente($"{t.NomV2} {idV3} vise {visee.NomV2} {valeur} ({colonne}), qui n'a ni correspondance dans la V2 ni changement en attente");
                await FicheAsync(cn, fiches, visee, valeur, idTransfert, pile, faites, arretees, bilan, ct);
            }

        // 2. La fiche, dans UNE transaction.
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var lignes = new List<LigneFile>();
            await using (var cmd = new SqlCommand("SELECT Id, Genre, Avant, Le, Auteur FROM rec.FileRecopie WITH (UPDLOCK, ROWLOCK) " +
                                                  "WHERE TableV3 = @t AND IdV3 = @id AND Etat = 'en attente' ORDER BY Id;", cn, tx))
            {
                cmd.Parameters.AddWithValue("@t", t.NomV3);
                cmd.Parameters.AddWithValue("@id", idV3);
                await using var l = await cmd.ExecuteReaderAsync(ct);
                while (await l.ReadAsync(ct))
                    lignes.Add(new LigneFile(l.GetInt64(0), l.GetString(1), l.IsDBNull(2) ? null : l.GetString(2), l.GetDateTime(3), l.GetString(4)));
            }
            if (lignes.Count == 0) { await tx.CommitAsync(ct); faites.Add((t.NomV3, idV3)); return; }

            var v3 = await LireV3Async(cn, tx, t, idV3, true, ct);
            var idV2 = await IdV2Async(cn, tx, t, idV3, ct);
            long? ecart = null;
            if (v3 is null)
            {
                // Supprimee dans la V3 : supprimee dans la V2 (ce que la V2 avait change depuis le dernier passage est perdu).
                if (idV2 is not null)
                {
                    if (await LireV2Async(cn, tx, t, idV2, true, ct) is { } v2)
                    {
                        var reference = await ReferenceAsync(cn, tx, t, idV2, lignes, ct);
                        var perdues = reference is null ? [] : LigneJson.Differences(v2, reference, t.Colonnes.Select(c => c.Nom));
                        if (perdues.Count > 0)
                            ecart = await EcrireEcartAsync(cn, tx, t, idV2, idV3, v2, reference!, perdues.Select(c => (c, (string?)v2[c], (string?)null)).ToList(),
                                                           lignes, "supprimee dans la V3", ct);
                        await SupprimerV2Async(cn, tx, t, idV2, ct);
                    }
                    await OublierAsync(cn, tx, t, idV2, ct);
                }
            }
            else
            {
                var enV2 = await TraduireAsync(cn, tx, t, v3, versV2: true, ct);
                LigneJson? v2 = idV2 is null ? null : await LireV2Async(cn, tx, t, idV2, true, ct);
                if (idV2 is not null && v2 is null)
                {
                    // Supprimee dans la V2 entre deux passages, modifiee dans la V3 : la V3 gagne, la fiche est recreee.
                    ecart = await EcrireEcartAsync(cn, tx, t, idV2, idV3, null, null, [("(fiche)", "\"supprimee dans la V2\"", "\"gardee par la V3\"")],
                                                   lignes, null, ct);
                    await OublierAsync(cn, tx, t, idV2, ct);
                    idV2 = null;
                }
                if (idV2 is null)
                {
                    // CA02 : creee dans la V3 -> la V2 lui donne son identifiant, la correspondance le garde.
                    var avecPropres = await TraduireAsync(cn, tx, t, (await LireV3Async(cn, tx, t, idV3, false, ct, avecPropres: true))!, versV2: true, ct);
                    idV2 = await CreerV2Async(cn, tx, t, idV3, avecPropres, ct);
                    await CorrespondreAsync(cn, tx, t, idV2, idV3, idTransfert, "recopie", ct);
                }
                else
                {
                    var reference = await ReferenceAsync(cn, tx, t, idV2, lignes, ct);
                    var perdues = reference is null ? [] : LigneJson.Differences(v2!, reference, t.Colonnes.Select(c => c.Nom));
                    if (perdues.Count > 0)
                        ecart = await EcrireEcartAsync(cn, tx, t, idV2, idV3, v2, reference!, perdues.Select(c => (c, (string?)v2![c], (string?)enV2[c])).ToList(),
                                                       lignes, null, ct);
                    var aEcrire = LigneJson.Differences(enV2, v2!, t.Colonnes.Select(c => c.Nom));
                    if (aEcrire.Count > 0) await ModifierV2Async(cn, tx, t, idV2, enV2, aEcrire, ct);
                }

                // 3. L'etat commun : la fiche V2 relue (ce que ses declencheurs ont ecrit compris) ; la V3 le recoit.
                var apres = (await LireV2Async(cn, tx, t, idV2, false, ct))!;
                var apresEnV3 = await TraduireAsync(cn, tx, t, apres, versV2: false, ct);
                var ecritsParLaV2 = LigneJson.Differences(apresEnV3, v3, t.Colonnes.Select(c => c.Nom));
                if (ecritsParLaV2.Count > 0) await ModifierV3Async(cn, tx, t, idV3, apresEnV3, ecritsParLaV2, ct);
                await NoterEtatAsync(cn, tx, t, idV2, apres, "V3 vers V2", ct);
            }

            await using (var cmd = new SqlCommand(
                // Un changement relance garde le message qui l'avait mis de cote : la trace de l'incident reste sur sa ligne.
                $"UPDATE rec.FileRecopie SET Etat = 'appliquee', AppliqueeLe = SYSDATETIME(), BaseV2 = @b, IdV2 = @id2, IdEcart = @e, " +
                "Erreur = CASE WHEN RelanceLe IS NULL THEN NULL ELSE Erreur END " +
                $"WHERE Id IN ({string.Join(", ", lignes.Select(l => l.Id))});", cn, tx))
            {
                cmd.Parameters.AddWithValue("@b", t.BaseV2);
                cmd.Parameters.AddWithValue("@id2", (object?)idV2 ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@e", (object?)ecart ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            faites.Add((t.NomV3, idV3));
            bilan.Lignes += lignes.Count;
            bilan.Fiches++;
            if (ecart is not null) bilan.Ecarts++;
        }
        catch
        {
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>L'etat que les deux bases avaient en commun : celui du dernier passage (rec.EtatLigne), sinon -- fiche
    /// jamais recopiee depuis le transfert -- la fiche V3 d'avant son premier changement en attente, en identifiants V2.</summary>
    private static async Task<LigneJson?> ReferenceAsync(SqlConnection cn, SqlTransaction tx, TableRecopiee t, string idV2, List<LigneFile> lignes, CancellationToken ct)
    {
        if (await EtatAsync(cn, tx, t, idV2, ct) is { } etat) return etat;
        return lignes.FirstOrDefault(l => l.Avant is not null) is { Avant: { } avant } && LigneJson.Lire(avant) is { } ligne
            ? await TraduireAsync(cn, tx, t, ligne, versV2: true, ct) : null;
    }

    /// <summary>L'ecart d'une fiche modifiee des deux cotes (REC-004 CA01) : un champ par valeur que la V2 avait changee.</summary>
    private static async Task<long> EcrireEcartAsync(SqlConnection cn, SqlTransaction tx, TableRecopiee t, string idV2, string idV3, LigneJson? v2,
                                                     LigneJson? reference, List<(string Champ, string? ValeurV2, string? ValeurV3)> champs,
                                                     List<LigneFile> lignes, string? sortV3, CancellationToken ct)
    {
        var v3 = await LireV3Async(cn, tx, t, idV3, false, ct);
        string? Valeur(string? colonne, LigneJson? l) => colonne is not null && l is not null && l.TryGetValue(colonne, out var b) ? LigneJson.Lisible(b)?.Trim() : null;
        var nom = Valeur(t.Regle.ColonneNom, v3) ?? Valeur(t.Regle.ColonneNom, v2);
        var fiche = $"{t.Regle.Libelle} {idV3}" + (string.IsNullOrEmpty(nom) ? "" : $" — {nom}") + (sortV3 is null ? "" : $" ({sortV3})");
        string? cible = t.Regle.Cible;
        if (cible is not null)
            foreach (var c in t.Colonnes.Concat([t.Cle]))
                cible = cible.Replace("{" + c.Nom + "}", c == t.Cle ? idV3 : Valeur(c.Nom, v3 ?? v2) ?? "", StringComparison.OrdinalIgnoreCase);
        // Sans date de mise a jour changee dans la V2 : le moment ou la recopie l'a vu, a l'horloge du serveur SQL (celle de
        // toutes les dates de rec.*), jamais a celle du programme.
        object dateV2 = Valeur(t.Regle.ColonneDateV2, v2) is { } d && DateTime.TryParse(d, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dv) &&
                        (reference is null || Valeur(t.Regle.ColonneDateV2, reference) != d) ? dv : DBNull.Value;
        var auteursV3 = string.Join(", ", lignes.Select(l => l.Auteur).Distinct());
        // L'auteur cote V2 : l'agent que la V2 note sur la fiche (CLIEN_ID_UTLST d'un client), nomme comme la V2 l'affiche
        // (Utilisateur.NomPrenomAbrege : « NOM Prenom (ABREGE) »). Seulement si le changement de la V2 l'a note -- sa date
        // ou son agent a change depuis l'etat commun ; sinon l'agent de la fiche est celui d'un enregistrement plus ancien.
        object auteurV2 = await AgentV2Async(cn, tx, t, v2, reference, ct) is { } agentV2 ? agentV2.Nom : DBNull.Value;
        var id = long.Parse((await ScalaireAsync(cn, tx, """
            INSERT INTO rec.Ecart (BaseV2, TableV2, IdV2, TableV3, IdV3, Fiche, Cible, DateV2, AuteurV2, DateV3, AuteurV3)
            OUTPUT CONVERT(nvarchar(20), inserted.Id) VALUES (@b, @t2, @id2, @t3, @id3, LEFT(@f, 300), @c, ISNULL(CONVERT(datetime2(3), @d2), SYSDATETIME()), LEFT(@a2, 128), @d3, LEFT(@a3, 128));
            """, ct, ("@b", t.BaseV2), ("@t2", t.NomV2), ("@id2", idV2), ("@t3", t.NomV3), ("@id3", idV3), ("@f", fiche), ("@c", (object?)cible ?? DBNull.Value),
            ("@d2", dateV2), ("@a2", auteurV2), ("@d3", lignes[0].Le), ("@a3", auteursV3)))!, CultureInfo.InvariantCulture);
        foreach (var (champ, valeurV2, valeurV3) in champs)
            await ExecuterAsync(cn, tx, "INSERT INTO rec.EcartChamp (IdEcart, Champ, ValeurV2, ValeurV3) VALUES (@id, @c, @v2, @v3);", ct,
                                ("@id", id), ("@c", champ), ("@v2", (object?)valeurV2 ?? DBNull.Value), ("@v3", (object?)valeurV3 ?? DBNull.Value));
        return id;
    }
}
