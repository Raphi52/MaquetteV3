using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.Chaines;

/// <summary>
/// LES CLOTURES ET LA PURGE (SOC-018 CA05, CA07).
///
/// Clore un jour, un mois ou un exercice (V2 : SP_INSERT_Totaux_Factures_Periodiques et _Encaissements_, job quotidien)
/// calcule et signe les grands totaux de la periode -- factures et encaissements : le total, le cumul perpetuel en
/// valeur reelle et en valeur absolue (le precedent du meme type plus la periode), et pour les factures le total par
/// taux de TVA -- puis ecrit l'evenement 50 (jour, mois) ou 60 (exercice). Clore un exercice produit aussi son ARCHIVE
/// FISCALE : toutes ses pieces, ses totaux et son journal, avec les cles publiques, scellee (empreinte SHA-256 signee)
/// et enregistree par l'evenement 30 du journal NON purgeable, auquel elle renvoie. Une periode se clot une fois, et pas
/// avant sa fin (heure du greffe).
///
/// Purger : seulement le journal PURGEABLE, d'un exercice dont l'archive est faite, et seulement ce qu'elle couvre ;
/// l'evenement 205 l'ecrit dans le journal non purgeable (CODE_JET : « obligatoirement precedee par la constitution
/// d'une archive de l'annee »).
/// </summary>
public sealed partial class ServiceChaines
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public async Task<ResultatCloture> CloturerAsync(SqlConnection cn, DemandeCloture demande, string auteur, int? idAuteur,
                                                     TraceGeste? trace, CancellationToken ct = default)
    {
        if (!DateOnly.TryParseExact(demande.Date ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var jour))
            throw new RefusChaines("La date de la période est attendue au format aaaa-mm-jj.");
        var periode = (demande.Periode ?? "").Trim().ToLowerInvariant();
        var (type, idPeriode, debut, fin, information, code, nom) = periode switch
        {
            PeriodesCloture.Jour => (PeriodesCloture.TypeJour, jour.ToString("yyyyMMdd", CultureInfo.InvariantCulture), jour, jour.AddDays(1),
                $"Total du {jour.Day:00} {jour.ToString("MMMM", Fr).ToUpper(Fr)} {jour.Year}", CodeCloturePeriode, $"la journée du {jour:dd/MM/yyyy}"),
            PeriodesCloture.Mois => (PeriodesCloture.TypeMois, jour.ToString("yyyyMM", CultureInfo.InvariantCulture), new DateOnly(jour.Year, jour.Month, 1),
                new DateOnly(jour.Year, jour.Month, 1).AddMonths(1), $"Total de {jour.ToString("MMMM", Fr).ToUpper(Fr)} {jour.Year}",
                CodeCloturePeriode, $"le mois de {jour.ToString("MMMM yyyy", Fr)}"),
            PeriodesCloture.Exercice => (PeriodesCloture.TypeExercice, jour.Year.ToString(CultureInfo.InvariantCulture), new DateOnly(jour.Year, 1, 1),
                new DateOnly(jour.Year + 1, 1, 1), $"Clôture de l'exercice {jour.Year}", CodeClotureExercice, $"l'exercice {jour.Year}"),
            _ => throw new RefusChaines("La période à clore est « jour », « mois » ou « exercice »."),
        };

        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        var ctx = await ContexteAsync(cn, tx, ct);
        var aujourdhui = DateOnly.FromDateTime(Fuseaux.MaintenantLocal(ctx.Fuseau));
        if (fin.AddDays(-1) > aujourdhui)
            throw new RefusChaines($"{Majuscule(nom)} n'est pas terminé : il se clôt à partir du {fin.AddDays(-1):dd/MM/yyyy} (heure du greffe). Rien n'a été écrit.");
        await VerrouAsync(cn, tx, ChainesSignees.TotauxFactures, ct);
        await VerrouAsync(cn, tx, ChainesSignees.TotauxEncaissements, ct);
        var deja = await ScalaireOuNulAsync<DateTime?>(cn, tx, "SELECT CalculeLe FROM soc.GrandTotalFactures WHERE Type = @t AND Periode = @p;", ct,
            P("@t", SqlDbType.VarChar, 5, type), P("@p", SqlDbType.VarChar, 8, idPeriode));
        if (deja is { } le)
            throw new RefusChaines($"{Majuscule(nom)} est déjà clos (le {le:dd/MM/yyyy HH:mm} UTC) : ses grands totaux sont signés et ne changent plus.");

        var debutDt = debut.ToDateTime(TimeOnly.MinValue);
        var finDt = fin.ToDateTime(TimeOnly.MinValue);
        var gtf = await TotalAsync(cn, tx, ctx, ChainesSignees.TotauxFactures, type, idPeriode, debutDt, finDt, information, ct);
        var gte = await TotalAsync(cn, tx, ctx, ChainesSignees.TotauxEncaissements, type, idPeriode, debutDt, finDt, information, ct);
        var idEvt = await EcrireEvenementAsync(cn, tx, code,
            $"Clôture de {nom} : {gtf.NbPieces} facture(s) pour {Euros(gtf.Total)} (cumul perpétuel {Euros(gtf.CumulReel)}), " +
            $"{gte.NbPieces} encaissement(s) pour {Euros(gte.Total)} (cumul perpétuel {Euros(gte.CumulReel)}). Grands totaux signés.",
            auteur, idAuteur, ct);
        await SignerEnAttenteAsync(cn, tx, ctx, idEvt, ct);
        var archive = periode == PeriodesCloture.Exercice ? await ArchiverAsync(cn, tx, ctx, jour.Year, debutDt, finDt, auteur, idAuteur, ct) : null;
        if (trace is not null) await trace(cn, tx, ct);
        await tx.CommitAsync(ct);
        var message = $"{Majuscule(nom)} est clos : grands totaux signés (factures {Euros(gtf.Total)}, encaissements {Euros(gte.Total)})"
                      + (archive is null ? "." : $", archive fiscale {archive.Fichier} scellée et enregistrée au journal non purgeable (maillon {archive.SeqEvenement}).");
        return new ResultatCloture(periode, idPeriode, [gtf, gte], archive, message);
    }

    /// <summary>Le grand total d'une chaine (GTF ou GTE) pour la periode, signe et insere ; rendu tel qu'il est en base.</summary>
    private static async Task<GrandTotal> TotalAsync(SqlConnection cn, SqlTransaction tx, ContexteSignature ctx, string chaine, string type,
                                                     string idPeriode, DateTime debut, DateTime fin, string information, CancellationToken ct)
    {
        var factures = chaine == ChainesSignees.TotauxFactures;
        var (pieces, colonneDate, colonneMontant, table) = factures
            ? ("soc.FactureSignee", "EmiseLe", "MontantTtc", "soc.GrandTotalFactures")
            : ("soc.EncaissementSigne", "EncaisseLe", "Montant", "soc.GrandTotalEncaissements");
        int nb; decimal total, absolu;
        await using (var cmd = new SqlCommand($"""
            SELECT COUNT(*), ISNULL(SUM({colonneMontant}), 0), ISNULL(SUM(ABS({colonneMontant})), 0)
            FROM {pieces} WHERE {colonneDate} >= @debut AND {colonneDate} < @fin;
            """, cn, tx))
        {
            cmd.Parameters.Add(new SqlParameter("@debut", SqlDbType.DateTime2) { Value = debut });
            cmd.Parameters.Add(new SqlParameter("@fin", SqlDbType.DateTime2) { Value = fin });
            await using var l = await cmd.ExecuteReaderAsync(ct);
            await l.ReadAsync(ct);
            (nb, total, absolu) = (l.GetInt32(0), l.GetDecimal(1), l.GetDecimal(2));
        }
        var parTaux = new List<(decimal Taux, decimal Montant)>();
        if (factures)
        {
            await using var cmd = new SqlCommand("""
                SELECT l.Taux, SUM(l.MontantTtc) FROM soc.FactureSigneeLigne l JOIN soc.FactureSignee f ON f.Id = l.IdFacture
                WHERE f.EmiseLe >= @debut AND f.EmiseLe < @fin GROUP BY l.Taux ORDER BY l.Taux DESC;
                """, cn, tx);
            cmd.Parameters.Add(new SqlParameter("@debut", SqlDbType.DateTime2) { Value = debut });
            cmd.Parameters.Add(new SqlParameter("@fin", SqlDbType.DateTime2) { Value = fin });
            await using var l = await cmd.ExecuteReaderAsync(ct);
            while (await l.ReadAsync(ct)) parTaux.Add((l.GetDecimal(0), l.GetDecimal(1)));
        }
        // Les cumuls perpetuels : la derniere ligne du MEME type (SP_INSERT_Totaux_Factures_Periodiques, « plus de
        // comparaison de date »), plus la periode.
        decimal reelAvant = 0, absoluAvant = 0;
        await using (var cmd = new SqlCommand($"SELECT TOP (1) CumulReel, CumulAbsolu FROM {table} WHERE Type = @t ORDER BY Seq DESC;", cn, tx))
        {
            cmd.Parameters.Add(P("@t", SqlDbType.VarChar, 5, type));
            await using var l = await cmd.ExecuteReaderAsync(ct);
            if (await l.ReadAsync(ct)) (reelAvant, absoluAvant) = (l.GetDecimal(0), l.GetDecimal(1));
        }
        var (reel, cumulAbsolu) = (reelAvant + total, absoluAvant + absolu);
        var calcule = ALaMilliseconde(DateTime.UtcNow);
        var (seq, precedente) = await ProchainAsync(cn, tx, chaine, table, ct);
        var signature = Signatures.Signer(ctx.Cle,
            Preimages.GrandTotal(chaine, seq, precedente, type, idPeriode, total, reel, cumulAbsolu, nb, calcule, information, parTaux));
        await SousSignatureAsync(cn, tx, async () =>
        {
            long id;
            await using (var cmd = new SqlCommand($"""
                INSERT INTO {table} (Type, Periode, Total, CumulReel, CumulAbsolu, NbPieces, CalculeLe, Information, Seq, Signature, IdCle)
                VALUES (@type, @periode, @total, @reel, @absolu, @nb, @calcule, @info, @seq, @signature, @cle);
                SELECT CAST(SCOPE_IDENTITY() AS bigint);
                """, cn, tx))
            {
                cmd.Parameters.Add(P("@type", SqlDbType.VarChar, 5, type));
                cmd.Parameters.Add(P("@periode", SqlDbType.VarChar, 8, idPeriode));
                cmd.Parameters.Add(Montant("@total", total));
                cmd.Parameters.Add(Montant("@reel", reel));
                cmd.Parameters.Add(Montant("@absolu", cumulAbsolu));
                cmd.Parameters.Add(new SqlParameter("@nb", SqlDbType.Int) { Value = nb });
                cmd.Parameters.Add(new SqlParameter("@calcule", SqlDbType.DateTime2) { Scale = 3, Value = calcule });
                cmd.Parameters.Add(P("@info", SqlDbType.NVarChar, 255, information));
                AjouterSignature(cmd, seq, signature, ctx.IdCle);
                id = (long)(await cmd.ExecuteScalarAsync(ct))!;
            }
            foreach (var (taux, montant) in parTaux)
            {
                await using var t = new SqlCommand("INSERT INTO soc.GrandTotalFacturesTva (IdTotal, Taux, MontantTtc) VALUES (@id, @taux, @montant);", cn, tx);
                t.Parameters.Add(new SqlParameter("@id", SqlDbType.BigInt) { Value = id });
                t.Parameters.Add(new SqlParameter("@taux", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = taux });
                t.Parameters.Add(Montant("@montant", montant));
                await t.ExecuteNonQueryAsync(ct);
            }
        }, ct);
        await AvancerAsync(cn, tx, chaine, seq, ct);
        return new GrandTotal(chaine, type, idPeriode, total, reel, cumulAbsolu, nb, DateTime.SpecifyKind(calcule, DateTimeKind.Utc), information, seq,
            [.. parTaux.Select(t => new TotalParTaux(t.Taux, t.Montant))]);
    }

    /// <summary>
    /// L'archive fiscale de l'exercice (V2 : ArchiveFiscaleExercice.cs, ARCHIVE_FISCALE_EXERCICE) : un document JSON de
    /// toutes ses pieces signees, de ses grands totaux et de son journal, avec les cles publiques qui les verifient.
    /// Son empreinte est ecrite dans l'evenement 30 du journal non purgeable ; son sceau signe l'empreinte et cet evenement.
    /// </summary>
    private async Task<ArchiveProduite> ArchiverAsync(SqlConnection cn, SqlTransaction tx, ContexteSignature ctx, int exercice,
                                                             DateTime debut, DateTime fin, string auteur, int? idAuteur, CancellationToken ct)
    {
        string json;
        await using (var cmd = new SqlCommand("""
            SELECT (
              SELECT @greffe AS greffe, @exercice AS exercice, SYSUTCDATETIME() AS produiteLeUtc,
                (SELECT Id AS id, Empreinte AS empreinte, Algorithme AS algorithme, ClePublique AS clePubliqueSpki, DepuisLe AS depuisLeUtc
                 FROM soc.CleSignature ORDER BY Id FOR JSON PATH) AS cles,
                (SELECT f.Numero AS numero, f.Nature AS nature, f.EmiseLe AS emiseLe, f.MontantTtc AS montantTtc, f.Client AS client,
                        f.CodePostal AS codePostal, f.TvaIntra AS tvaIntra, f.Seq AS seq, f.Signature AS signature, f.IdCle AS cle,
                        (SELECT l.Rang AS rang, l.Taux AS taux, l.MontantTtc AS montantTtc FROM soc.FactureSigneeLigne l
                         WHERE l.IdFacture = f.Id ORDER BY l.Rang FOR JSON PATH) AS lignes
                 FROM soc.FactureSignee f WHERE f.EmiseLe >= @debut AND f.EmiseLe < @fin ORDER BY f.Seq FOR JSON PATH) AS factures,
                (SELECT Numero AS numero, EncaisseLe AS encaisseLe, Montant AS montant, Seq AS seq, Signature AS signature, IdCle AS cle
                 FROM soc.EncaissementSigne WHERE EncaisseLe >= @debut AND EncaisseLe < @fin ORDER BY Seq FOR JSON PATH) AS encaissements,
                (SELECT NumeroFacture AS numeroFacture, Rang AS rang, ImprimeLe AS imprimeLe, Motif AS motif, Compte AS compte, Seq AS seq,
                        Signature AS signature, IdCle AS cle
                 FROM soc.DuplicataSigne WHERE ImprimeLe >= @debut AND ImprimeLe < @fin ORDER BY Seq FOR JSON PATH) AS duplicatas,
                (SELECT t.Type AS type, t.Periode AS periode, t.Total AS total, t.CumulReel AS cumulReel, t.CumulAbsolu AS cumulAbsolu,
                        t.NbPieces AS nbPieces, t.CalculeLe AS calculeLeUtc, t.Information AS information, t.Seq AS seq, t.Signature AS signature,
                        t.IdCle AS cle,
                        (SELECT v.Taux AS taux, v.MontantTtc AS montantTtc FROM soc.GrandTotalFacturesTva v WHERE v.IdTotal = t.Id
                         ORDER BY v.Taux DESC FOR JSON PATH) AS parTaux
                 FROM soc.GrandTotalFactures t WHERE t.Periode LIKE @prefixe ORDER BY t.Seq FOR JSON PATH) AS totauxFactures,
                (SELECT Type AS type, Periode AS periode, Total AS total, CumulReel AS cumulReel, CumulAbsolu AS cumulAbsolu, NbPieces AS nbPieces,
                        CalculeLe AS calculeLeUtc, Information AS information, Seq AS seq, Signature AS signature, IdCle AS cle
                 FROM soc.GrandTotalEncaissements WHERE Periode LIKE @prefixe ORDER BY Seq FOR JSON PATH) AS totauxEncaissements,
                (SELECT Id AS id, Instant AS instantUtc, Code AS code, Libelle AS libelle, Detail AS detail, Auteur AS auteur, Seq AS seq,
                        Signature AS signature, IdCle AS cle
                 FROM soc.EvenementTechnique WHERE Chaine = 'JNP' AND Exercice = @exercice ORDER BY Seq FOR JSON PATH) AS journalNonPurgeable,
                (SELECT Id AS id, Instant AS instantUtc, Code AS code, Libelle AS libelle, Detail AS detail, Auteur AS auteur, Seq AS seq,
                        Signature AS signature, IdCle AS cle
                 FROM soc.EvenementTechnique WHERE Chaine = 'JP' AND Exercice = @exercice ORDER BY Seq FOR JSON PATH) AS journalPurgeable
              FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
            """, cn, tx))
        {
            cmd.Parameters.Add(P("@greffe", SqlDbType.VarChar, 20, ctx.Greffe));
            cmd.Parameters.Add(new SqlParameter("@exercice", SqlDbType.SmallInt) { Value = (short)exercice });
            cmd.Parameters.Add(new SqlParameter("@debut", SqlDbType.DateTime2) { Value = debut });
            cmd.Parameters.Add(new SqlParameter("@fin", SqlDbType.DateTime2) { Value = fin });
            cmd.Parameters.Add(P("@prefixe", SqlDbType.VarChar, 9, exercice.ToString(CultureInfo.InvariantCulture) + "%"));
            json = (string)(await cmd.ExecuteScalarAsync(ct))!;
        }
        var nbPieces = await ScalaireAsync<int>(cn, tx, """
            SELECT (SELECT COUNT(*) FROM soc.FactureSignee WHERE EmiseLe >= @debut AND EmiseLe < @fin)
                 + (SELECT COUNT(*) FROM soc.EncaissementSigne WHERE EncaisseLe >= @debut AND EncaisseLe < @fin)
                 + (SELECT COUNT(*) FROM soc.DuplicataSigne WHERE ImprimeLe >= @debut AND ImprimeLe < @fin);
            """, ct, new SqlParameter("@debut", SqlDbType.DateTime2) { Value = debut }, new SqlParameter("@fin", SqlDbType.DateTime2) { Value = fin });
        var seqJnp = await ScalaireAsync<long>(cn, tx, "SELECT DerniereSeq FROM soc.EtatChaine WHERE Chaine = 'JNP' AND Exercice = 0;", ct);
        var seqJp = await ScalaireOuNulAsync<long?>(cn, tx, "SELECT DerniereSeq FROM soc.EtatChaine WHERE Chaine = 'JP' AND Exercice = @x;", ct,
            new SqlParameter("@x", SqlDbType.SmallInt) { Value = (short)exercice }) ?? 0;
        var contenu = Encoding.UTF8.GetBytes(json);
        var empreinte = Signatures.Empreinte(contenu);
        var fichier = $"archive-fiscale-{ctx.Greffe}-{exercice}.json";
        var id30 = await EcrireEvenementAsync(cn, tx, CodeArchiveExercice,
            $"Archivage fiscal de l'exercice {exercice} : {fichier}, {nbPieces} pièce(s), empreinte SHA-256 {empreinte}, " +
            $"journal non purgeable couvert jusqu'au maillon {seqJnp}, journal purgeable de l'exercice jusqu'au maillon {seqJp}.",
            auteur, idAuteur, ct);
        await SignerEnAttenteAsync(cn, tx, ctx, id30, ct);
        var seq30 = await ScalaireAsync<long>(cn, tx, "SELECT Seq FROM soc.EvenementTechnique WHERE Id = @id;", ct,
            new SqlParameter("@id", SqlDbType.BigInt) { Value = id30 });
        var sceau = Signatures.Signer(ctx.Cle, Preimages.Sceau(ctx.Greffe, exercice, empreinte, nbPieces, seqJnp, seqJp, id30));
        await SousSignatureAsync(cn, tx, async () =>
        {
            await using var cmd = new SqlCommand("""
                INSERT INTO soc.ArchiveFiscale (Exercice, ProduitePar, Fichier, Contenu, Empreinte, Sceau, IdCle, NbPieces, DerniereSeqJnp, DerniereSeqJp, IdEvenement)
                VALUES (@exercice, @par, @fichier, @contenu, @empreinte, @sceau, @cle, @nb, @jnp, @jp, @evt);
                """, cn, tx);
            cmd.Parameters.Add(new SqlParameter("@exercice", SqlDbType.SmallInt) { Value = (short)exercice });
            cmd.Parameters.Add(P("@par", SqlDbType.NVarChar, 128, Borne(auteur, 128)));
            cmd.Parameters.Add(P("@fichier", SqlDbType.NVarChar, 260, fichier));
            cmd.Parameters.Add(new SqlParameter("@contenu", SqlDbType.VarBinary, -1) { Value = contenu });
            cmd.Parameters.Add(P("@empreinte", SqlDbType.Char, 64, empreinte));
            cmd.Parameters.Add(P("@sceau", SqlDbType.VarChar, 120, sceau));
            cmd.Parameters.Add(new SqlParameter("@cle", SqlDbType.Int) { Value = ctx.IdCle });
            cmd.Parameters.Add(new SqlParameter("@nb", SqlDbType.Int) { Value = nbPieces });
            cmd.Parameters.Add(new SqlParameter("@jnp", SqlDbType.BigInt) { Value = seqJnp });
            cmd.Parameters.Add(new SqlParameter("@jp", SqlDbType.BigInt) { Value = seqJp });
            cmd.Parameters.Add(new SqlParameter("@evt", SqlDbType.BigInt) { Value = id30 });
            await cmd.ExecuteNonQueryAsync(ct);
        }, ct);
        return new ArchiveProduite(exercice, fichier, empreinte, nbPieces, seqJnp, seqJp, id30, seq30);
    }

    /// <summary>Purge le journal PURGEABLE de l'exercice archive (CA07) et ecrit l'evenement 205 ; rien d'autre ne s'efface.</summary>
    public async Task<ResultatPurge> PurgerAsync(SqlConnection cn, int exercice, string auteur, int? idAuteur, TraceGeste? trace,
                                                 CancellationToken ct = default)
    {
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        var ctx = await ContexteAsync(cn, tx, ct);
        await VerrouAsync(cn, tx, ChainesSignees.JournalNonPurgeable, ct);
        await VerrouAsync(cn, tx, ChainesSignees.JournalPurgeable, ct);
        string? empreinte = null;
        long couvert = 0;
        await using (var cmd = new SqlCommand("SELECT Empreinte, DerniereSeqJp FROM soc.ArchiveFiscale WHERE Exercice = @x;", cn, tx))
        {
            cmd.Parameters.Add(new SqlParameter("@x", SqlDbType.SmallInt) { Value = (short)exercice });
            await using var l = await cmd.ExecuteReaderAsync(ct);
            if (await l.ReadAsync(ct)) (empreinte, couvert) = (l.GetString(0).Trim(), l.GetInt64(1));
        }
        if (empreinte is null)
            throw new RefusChaines($"Le journal purgeable de l'exercice {exercice} ne se purge qu'après son archive fiscale (clôture de l'exercice) : rien n'a été effacé.");
        var purges = 0;
        await SousContexteAsync(cn, tx, ContextePurgeSql, async () =>
        {
            await using var cmd = new SqlCommand(
                "DELETE FROM soc.EvenementTechnique WHERE Chaine = 'JP' AND Exercice = @x AND Seq <= @couvert;", cn, tx);
            cmd.Parameters.Add(new SqlParameter("@x", SqlDbType.SmallInt) { Value = (short)exercice });
            cmd.Parameters.Add(new SqlParameter("@couvert", SqlDbType.BigInt) { Value = couvert });
            purges = await cmd.ExecuteNonQueryAsync(ct);
        }, ct);
        if (purges > 0)
        {
            await using var cmd = new SqlCommand(
                "UPDATE soc.EtatChaine SET PurgeJusqua = @couvert, MisAJourLe = SYSUTCDATETIME() WHERE Chaine = 'JP' AND Exercice = @x;", cn, tx);
            cmd.Parameters.Add(new SqlParameter("@x", SqlDbType.SmallInt) { Value = (short)exercice });
            cmd.Parameters.Add(new SqlParameter("@couvert", SqlDbType.BigInt) { Value = couvert });
            await cmd.ExecuteNonQueryAsync(ct);
        }
        var id205 = await EcrireEvenementAsync(cn, tx, CodePurgePartielle,
            $"Purge partielle du journal : {purges} événement(s) purgeable(s) de l'exercice {exercice} effacé(s) (maillons 1 à {couvert}), " +
            $"après son archive fiscale (empreinte {empreinte}). Le journal non purgeable n'est pas touché.", auteur, idAuteur, ct);
        await SignerEnAttenteAsync(cn, tx, ctx, id205, ct);
        if (trace is not null) await trace(cn, tx, ct);
        await tx.CommitAsync(ct);
        return new ResultatPurge(exercice, purges, id205,
            $"{purges} événement(s) purgeable(s) de l'exercice {exercice} effacé(s), après son archive fiscale ; la purge est écrite au journal non purgeable.");
    }

    private static string Euros(decimal m) => m.ToString("N2", Fr) + " €";

    private static string Majuscule(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Fr) + s[1..];
}
