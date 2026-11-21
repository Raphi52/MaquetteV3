using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;

namespace RigV3.Serveur.Socle.Chaines;

/// <summary>
/// LE CONTROLE D'INTEGRITE ET DE SEQUENCE (SOC-018 CA04, T02). Pour chacune des 7 chaines, il refait la pre-image de
/// chaque maillon avec la signature du maillon present juste en dessous -- la meme regle qu'a la signature -- et la
/// verifie avec la cle PUBLIQUE en base : une ligne modifiee directement en base ne verifie plus, c'est un evenement 90
/// « Detection d'un defaut d'integrite ». Il compte ensuite les rangs : un trou, des premiers ou des derniers maillons
/// disparus sont un evenement 95 « Detection d'une rupture de la sequence continue ». Les archives fiscales sont
/// verifiees aussi (empreinte, sceau, evenement 30 qui les enregistre) : un defaut y est un 90.
///
/// La V2 fait les deux en deux programmes (CertifExpressoApp mode 10 pour le 90, SP_VERIF_RUPTURE_SEQUENCE pour le 95,
/// Note_Methodologie_Verif_Integrite_90.md) ; ici un seul passage. Les evenements en attente sont signes d'abord, comme
/// le job de la V2. Sur une copie anonymisee (SOC-021) rien n'est signe ni ecrit : le bilan le dit, sans valeur probante.
/// </summary>
public static class ControleIntegrite
{
    private sealed record Maillon(long Seq, string Signature, int IdCle, string Nom, int Exercice, Func<string?, string> Preimage);

    public static async Task<BilanIntegrite> ControlerAsync(ServiceChaines chaines, SqlConnection cn, string auteur, int? idAuteur,
                                                            CancellationToken ct = default)
    {
        var le = DateTime.UtcNow;
        if (await ServiceChaines.ScalaireAsync<int>(cn, null, "SELECT COUNT(*) FROM soc.Certification;", ct) == 0)
            throw new ChainesNonOuvertes("Les chaînes signées de la V3 ne sont pas ouvertes dans cette base : il n'y a rien à contrôler.");
        var reserve = await ServiceChaines.CopieAnonymiseeAsync(cn, null, ct);
        var signees = reserve is null ? await chaines.SignerEnAttenteAsync(cn, ct) : 0;

        var cles = await ClesAsync(cn, ct);
        var etats = await EtatsAsync(cn, ct);
        long Derniere(string c) => etats.TryGetValue((c, (short)0), out var e) ? e.Derniere : 0;
        var bilans = new List<BilanChaine>
        {
            Verifier(ChainesSignees.Factures, await FacturesAsync(cn, ct), Derniere(ChainesSignees.Factures), cles),
            Verifier(ChainesSignees.Encaissements, await EncaissementsAsync(cn, ct), Derniere(ChainesSignees.Encaissements), cles),
            Verifier(ChainesSignees.TotauxFactures, await TotauxAsync(cn, ChainesSignees.TotauxFactures, ct), Derniere(ChainesSignees.TotauxFactures), cles),
            Verifier(ChainesSignees.TotauxEncaissements, await TotauxAsync(cn, ChainesSignees.TotauxEncaissements, ct), Derniere(ChainesSignees.TotauxEncaissements), cles),
            Verifier(ChainesSignees.Duplicatas, await DuplicatasAsync(cn, ct), Derniere(ChainesSignees.Duplicatas), cles),
            Verifier(ChainesSignees.JournalNonPurgeable, await EvenementsAsync(cn, ChainesSignees.JournalNonPurgeable, ct), Derniere(ChainesSignees.JournalNonPurgeable), cles),
            VerifierPurgeable(await EvenementsAsync(cn, ChainesSignees.JournalPurgeable, ct), etats, cles),
        };
        var archives = await ArchivesAsync(cn, cles, ct);

        var ecrits = new List<long>();
        if (reserve is null && (bilans.Any(b => b.Defauts.Length > 0 || b.Ruptures.Length > 0) || archives.Count > 0))
        {
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
            var ctx = await chaines.ContexteAsync(cn, tx, ct);
            foreach (var b in bilans)
            {
                foreach (var d in b.Defauts)
                    ecrits.Add(await ServiceChaines.EcrireEvenementAsync(cn, tx, ServiceChaines.CodeDefautIntegrite,
                        $"Défaut d'intégrité, chaîne « {b.Libelle} » : {d}.", auteur, idAuteur, ct));
                foreach (var r in b.Ruptures)
                    ecrits.Add(await ServiceChaines.EcrireEvenementAsync(cn, tx, ServiceChaines.CodeRuptureSequence,
                        $"Rupture de la séquence, chaîne « {b.Libelle} » : {r}.", auteur, idAuteur, ct));
            }
            foreach (var a in archives)
                ecrits.Add(await ServiceChaines.EcrireEvenementAsync(cn, tx, ServiceChaines.CodeDefautIntegrite,
                    $"Défaut d'intégrité d'une archive fiscale : {a}.", auteur, idAuteur, ct));
            await chaines.SignerEnAttenteAsync(cn, tx, ctx, null, ct);
            await tx.CommitAsync(ct);
        }
        foreach (var k in cles.Values) k.Dispose();
        return new BilanIntegrite(le, reserve is null, reserve, signees, [.. bilans], [.. archives], [.. ecrits]);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Verification d'une chaine
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>Une chaine continue depuis le maillon 1 jusqu'a son dernier emis (toutes sauf le journal purgeable).</summary>
    private static BilanChaine Verifier(string code, List<Maillon> maillons, long derniere, IReadOnlyDictionary<int, ECDsa> cles)
    {
        var defauts = new List<string>();
        var ruptures = new List<string>();
        var valides = VerifierSuite(maillons, 1, derniere, cles, defauts, ruptures, "");
        return new BilanChaine(code, ChainesSignees.Libelle(code), maillons.Count, valides, [.. defauts], [.. ruptures]);
    }

    /// <summary>Le journal purgeable : une suite par exercice, qui commence apres ce que sa purge a efface (CA07).</summary>
    private static BilanChaine VerifierPurgeable(List<Maillon> maillons, IReadOnlyDictionary<(string, short), (long Derniere, long? Purge)> etats,
                                                 IReadOnlyDictionary<int, ECDsa> cles)
    {
        var defauts = new List<string>();
        var ruptures = new List<string>();
        long valides = 0;
        var exercices = etats.Keys.Where(k => k.Item1 == ChainesSignees.JournalPurgeable).Select(k => (int)k.Item2)
            .Union(maillons.Select(m => m.Exercice)).Order();
        foreach (var exercice in exercices)
        {
            var (derniere, purge) = etats.TryGetValue((ChainesSignees.JournalPurgeable, (short)exercice), out var e) ? e : (0L, null);
            var suite = maillons.Where(m => m.Exercice == exercice).ToList();
            if (derniere == 0 && suite.Count > 0)
                ruptures.Add($"exercice {exercice} : {suite.Count} maillon(s) signé(s) sans état de chaîne");
            valides += VerifierSuite(suite, (purge ?? 0) + 1, derniere, cles, defauts, ruptures, $"exercice {exercice}, ");
        }
        return new BilanChaine(ChainesSignees.JournalPurgeable, ChainesSignees.Libelle(ChainesSignees.JournalPurgeable),
            maillons.Count, valides, [.. defauts], [.. ruptures]);
    }

    /// <summary>
    /// Une suite de maillons qui doit aller de <paramref name="premier"/> a <paramref name="derniere"/> sans trou. Chaque
    /// signature est verifiee avec celle du maillon present juste en dessous (regle de la signature). Un maillon dont le
    /// precedent manque ne prouve plus rien : il est compte avec la rupture (95), pas comme une alteration (90).
    /// </summary>
    private static long VerifierSuite(List<Maillon> suite, long premier, long derniere, IReadOnlyDictionary<int, ECDsa> cles,
                                      List<string> defauts, List<string> ruptures, string ou)
    {
        suite.Sort((a, b) => a.Seq.CompareTo(b.Seq));
        long valides = 0;
        var attendu = premier;
        Maillon? precedent = null;
        foreach (var m in suite)
        {
            var trou = m.Seq > attendu;
            if (trou) ruptures.Add($"{ou}{Trou(attendu, m.Seq - 1)}");
            var ok = cles.TryGetValue(m.IdCle, out var cle) && Signatures.Verifier(cle, m.Preimage(precedent?.Signature), m.Signature);
            if (ok) valides++;
            else if (precedent is null && !trou && m.Seq == premier && premier > 1)
            {
                // Le premier maillon apres une purge (CA07) : son precedent est parti avec l'exercice archive. Il ne se
                // verifie plus, sans rupture ni alteration : l'archive scellee garde ce qui a ete efface.
            }
            else if (trou || (precedent is null && m.Seq > 1))
                ruptures.Add($"{ou}maillon {m.Seq} ({m.Nom}) : son précédent manque, sa signature ne peut plus être vérifiée");
            else defauts.Add($"{ou}maillon {m.Seq} ({m.Nom}) : la signature ne correspond plus au contenu");
            attendu = m.Seq + 1;
            precedent = m;
        }
        if (attendu <= derniere)
            ruptures.Add($"{ou}{(attendu == derniere ? $"le dernier maillon émis ({derniere}) a disparu" : $"les derniers maillons émis ({attendu} à {derniere}) ont disparu")}");
        return valides;
    }

    private static string Trou(long de, long a) => de == a ? $"le maillon {de} manque" : $"les maillons {de} à {a} manquent";

    // ---------------------------------------------------------------------------------------------------------
    // Lecture des maillons (memes champs, meme ordre que la signature : ServiceChaines, ServiceChaines.Clotures)
    // ---------------------------------------------------------------------------------------------------------

    private static async Task<Dictionary<int, ECDsa>> ClesAsync(SqlConnection cn, CancellationToken ct)
    {
        var cles = new Dictionary<int, ECDsa>();
        await using var cmd = new SqlCommand("SELECT Id, ClePublique FROM soc.CleSignature;", cn);
        await using var l = await cmd.ExecuteReaderAsync(ct);
        while (await l.ReadAsync(ct)) cles[l.GetInt32(0)] = Signatures.ClePublique((byte[])l[1]);
        return cles;
    }

    private static async Task<Dictionary<(string, short), (long Derniere, long? Purge)>> EtatsAsync(SqlConnection cn, CancellationToken ct)
    {
        var etats = new Dictionary<(string, short), (long, long?)>();
        await using var cmd = new SqlCommand("SELECT Chaine, Exercice, DerniereSeq, PurgeJusqua FROM soc.EtatChaine;", cn);
        await using var l = await cmd.ExecuteReaderAsync(ct);
        while (await l.ReadAsync(ct)) etats[(l.GetString(0), l.GetInt16(1))] = (l.GetInt64(2), l.IsDBNull(3) ? null : l.GetInt64(3));
        return etats;
    }

    private static async Task<List<Maillon>> FacturesAsync(SqlConnection cn, CancellationToken ct)
    {
        var lignes = new Dictionary<long, List<(decimal, decimal)>>();
        await using (var cmd = new SqlCommand("SELECT IdFacture, Taux, MontantTtc FROM soc.FactureSigneeLigne ORDER BY IdFacture, Rang;", cn))
        await using (var l = await cmd.ExecuteReaderAsync(ct))
            while (await l.ReadAsync(ct))
            {
                if (!lignes.TryGetValue(l.GetInt64(0), out var liste)) lignes[l.GetInt64(0)] = liste = [];
                liste.Add((l.GetDecimal(1), l.GetDecimal(2)));
            }
        var maillons = new List<Maillon>();
        await using (var cmd = new SqlCommand(
            "SELECT Id, Seq, Signature, IdCle, Numero, Nature, EmiseLe, MontantTtc, Client, CodePostal, TvaIntra FROM soc.FactureSignee;", cn))
        await using (var l = await cmd.ExecuteReaderAsync(ct))
            while (await l.ReadAsync(ct))
            {
                var (seq, numero, nature, emise, montant, client) = (l.GetInt64(1), l.GetString(4), l.GetString(5), l.GetDateTime(6), l.GetDecimal(7), l.GetString(8));
                var (cp, tva) = (l.IsDBNull(9) ? null : l.GetString(9), l.IsDBNull(10) ? null : l.GetString(10));
                var ventilation = lignes.TryGetValue(l.GetInt64(0), out var v) ? v : [];
                maillons.Add(new Maillon(seq, l.GetString(2), l.GetInt32(3), "facture " + numero, 0,
                    p => Preimages.Facture(seq, p, numero, nature, emise, montant, client, cp, tva, ventilation)));
            }
        return maillons;
    }

    private static async Task<List<Maillon>> EncaissementsAsync(SqlConnection cn, CancellationToken ct)
    {
        var maillons = new List<Maillon>();
        await using var cmd = new SqlCommand("SELECT Seq, Signature, IdCle, Numero, EncaisseLe, Montant FROM soc.EncaissementSigne;", cn);
        await using var l = await cmd.ExecuteReaderAsync(ct);
        while (await l.ReadAsync(ct))
        {
            var (seq, numero, le, montant) = (l.GetInt64(0), l.GetString(3), l.GetDateTime(4), l.GetDecimal(5));
            maillons.Add(new Maillon(seq, l.GetString(1), l.GetInt32(2), "encaissement " + numero, 0,
                p => Preimages.Encaissement(seq, p, numero, le, montant)));
        }
        return maillons;
    }

    private static async Task<List<Maillon>> DuplicatasAsync(SqlConnection cn, CancellationToken ct)
    {
        var maillons = new List<Maillon>();
        await using var cmd = new SqlCommand("""
            SELECT d.Seq, d.Signature, d.IdCle, d.NumeroFacture, ISNULL(f.Signature, ''), d.Rang, d.ImprimeLe, d.Motif, d.Compte
            FROM soc.DuplicataSigne d LEFT JOIN soc.FactureSignee f ON f.Numero = d.NumeroFacture;
            """, cn);
        await using var l = await cmd.ExecuteReaderAsync(ct);
        while (await l.ReadAsync(ct))
        {
            var (seq, facture, sigFacture, rang, le, motif, compte) =
                (l.GetInt64(0), l.GetString(3), l.GetString(4), l.GetInt32(5), l.GetDateTime(6), l.GetString(7), l.GetString(8));
            maillons.Add(new Maillon(seq, l.GetString(1), l.GetInt32(2), $"duplicata {rang} de la facture {facture}", 0,
                p => Preimages.Duplicata(seq, p, facture, sigFacture, rang, le, motif, compte)));
        }
        return maillons;
    }

    private static async Task<List<Maillon>> TotauxAsync(SqlConnection cn, string chaine, CancellationToken ct)
    {
        var tva = new Dictionary<long, List<(decimal, decimal)>>();
        if (chaine == ChainesSignees.TotauxFactures)
        {
            await using var cmd = new SqlCommand("SELECT IdTotal, Taux, MontantTtc FROM soc.GrandTotalFacturesTva ORDER BY IdTotal, Taux DESC;", cn);
            await using var l = await cmd.ExecuteReaderAsync(ct);
            while (await l.ReadAsync(ct))
            {
                if (!tva.TryGetValue(l.GetInt64(0), out var liste)) tva[l.GetInt64(0)] = liste = [];
                liste.Add((l.GetDecimal(1), l.GetDecimal(2)));
            }
        }
        var table = chaine == ChainesSignees.TotauxFactures ? "soc.GrandTotalFactures" : "soc.GrandTotalEncaissements";
        var maillons = new List<Maillon>();
        await using (var cmd = new SqlCommand(
            $"SELECT Id, Seq, Signature, IdCle, Type, Periode, Total, CumulReel, CumulAbsolu, NbPieces, CalculeLe, Information FROM {table};", cn))
        await using (var l = await cmd.ExecuteReaderAsync(ct))
            while (await l.ReadAsync(ct))
            {
                var (seq, type, periode, total, reel, absolu, nb, calcule) =
                    (l.GetInt64(1), l.GetString(4), l.GetString(5), l.GetDecimal(6), l.GetDecimal(7), l.GetDecimal(8), l.GetInt32(9), l.GetDateTime(10));
                var info = l.IsDBNull(11) ? null : l.GetString(11);
                var parTaux = tva.TryGetValue(l.GetInt64(0), out var t) ? t : [];
                maillons.Add(new Maillon(seq, l.GetString(2), l.GetInt32(3), $"total {type} {periode}", 0,
                    p => Preimages.GrandTotal(chaine, seq, p, type, periode, total, reel, absolu, nb, calcule, info, parTaux)));
            }
        return maillons;
    }

    private static async Task<List<Maillon>> EvenementsAsync(SqlConnection cn, string chaine, CancellationToken ct)
    {
        var maillons = new List<Maillon>();
        await using var cmd = new SqlCommand("""
            SELECT Seq, Signature, IdCle, Id, Exercice, Instant, Code, Libelle, Detail, Auteur
            FROM soc.EvenementTechnique WHERE Chaine = @c;
            """, cn);
        cmd.Parameters.Add(ServiceChaines.P("@c", SqlDbType.VarChar, 3, chaine));
        await using var l = await cmd.ExecuteReaderAsync(ct);
        while (await l.ReadAsync(ct))
        {
            var (seq, id, exercice, instant, code, libelle, detail, auteur) =
                (l.GetInt64(0), l.GetInt64(3), (int)l.GetInt16(4), l.GetDateTime(5), l.GetInt16(6), l.GetString(7), l.GetString(8), l.GetString(9));
            maillons.Add(new Maillon(seq, l.GetString(1), l.GetInt32(2), $"événement {code} n° {id}", exercice,
                p => Preimages.Evenement(chaine, seq, p, id, exercice, instant, code, libelle, detail, auteur)));
        }
        return maillons;
    }

    /// <summary>Chaque archive : son empreinte refaite sur son contenu, son sceau, l'evenement 30 du journal non purgeable qui la porte.</summary>
    private static async Task<List<string>> ArchivesAsync(SqlConnection cn, IReadOnlyDictionary<int, ECDsa> cles, CancellationToken ct)
    {
        var defauts = new List<string>();
        await using var cmd = new SqlCommand("""
            SELECT a.Exercice, a.Contenu, a.Empreinte, a.Sceau, a.IdCle, a.NbPieces, a.DerniereSeqJnp, a.DerniereSeqJp, a.IdEvenement,
                   e.Code, e.Chaine, e.Detail, c.CodeGreffe
            FROM soc.ArchiveFiscale a
            LEFT JOIN soc.EvenementTechnique e ON e.Id = a.IdEvenement
            CROSS JOIN soc.Certification c;
            """, cn);
        await using var l = await cmd.ExecuteReaderAsync(ct);
        while (await l.ReadAsync(ct))
        {
            var exercice = (int)l.GetInt16(0);
            var empreinte = l.GetString(2).Trim();
            var refaite = Signatures.Empreinte((byte[])l[1]);
            var sceau = Preimages.Sceau(l.GetString(12), exercice, empreinte, l.GetInt32(5), l.GetInt64(6), l.GetInt64(7), l.GetInt64(8));
            if (refaite != empreinte)
                defauts.Add($"exercice {exercice} : son contenu ne correspond plus à son empreinte ({refaite[..12]}… au lieu de {empreinte[..12]}…)");
            if (!cles.TryGetValue(l.GetInt32(4), out var cle) || !Signatures.Verifier(cle, sceau, l.GetString(3)))
                defauts.Add($"exercice {exercice} : son sceau ne correspond plus");
            if (l.IsDBNull(9) || l.GetInt16(9) != ServiceChaines.CodeArchiveExercice || l.IsDBNull(10)
                || l.GetString(10) != ChainesSignees.JournalNonPurgeable || !l.GetString(11).Contains(empreinte, StringComparison.Ordinal))
                defauts.Add($"exercice {exercice} : l'événement 30 du journal non purgeable qui l'enregistre manque ou ne porte pas son empreinte");
        }
        return defauts;
    }
}
