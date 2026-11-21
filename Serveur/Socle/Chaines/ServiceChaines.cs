using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.Chaines;

/// <summary>Les chaines ne sont pas ouvertes dans cette base : rien n'est signe avant le passage (CA01).</summary>
public sealed class ChainesNonOuvertes(string message) : InvalidOperationException(message);

/// <summary>Les chaines sont deja ouvertes : une seconde ouverture ne change rien (CA01).</summary>
public sealed class ChainesDejaOuvertes(string message) : InvalidOperationException(message);

/// <summary>Un geste refuse par une regle des chaines (cloture prematuree ou deja faite, purge sans archive...) : rien n'est ecrit.</summary>
public sealed class RefusChaines(string message) : InvalidOperationException(message);

/// <summary>Une ligne de la ventilation d'une facture : un taux de TVA et le montant TTC qui s'y rapporte.</summary>
public sealed record LigneTva(decimal Taux, decimal MontantTtc);

/// <summary>
/// Ce qui est signe d'une facture (ou d'un avoir) de la V3 : les champs de la pre-image de la V2 (SP_HashFactureR12).
/// <paramref name="EmiseLe"/> est l'heure du greffe ; elle est arrondie a la seconde (la colonne de la V2 aussi).
/// </summary>
public sealed record PieceFacture(string Numero, string Nature, DateTime EmiseLe, decimal MontantTtc, string Client,
                                  string? CodePostal, string? TvaIntra, IReadOnlyList<LigneTva> Lignes);

/// <summary>Ce qui est signe d'un encaissement (SP_HashEncaissement13) ; <paramref name="EncaisseLe"/> a l'heure du greffe.</summary>
public sealed record PieceEncaissement(string Numero, DateTime EncaisseLe, decimal Montant);

/// <summary>Un duplicata imprime d'une facture deja signee dans la V3 (SP_HashDuplicata).</summary>
public sealed record PieceDuplicata(string NumeroFacture, DateTime ImprimeLe, string Motif, string Compte);

/// <summary>Le maillon qu'une piece vient de recevoir.</summary>
public sealed record MaillonSigne(string Chaine, long Seq, string Signature);

/// <summary>Ce qu'une operation ecrit aussi au journal des gestes (SOC-016), dans sa transaction.</summary>
public delegate Task TraceGeste(SqlConnection cn, SqlTransaction tx, CancellationToken ct);

/// <summary>Ce qu'une signature a besoin de savoir : le greffe, son fuseau, la cle (privee) et son rang dans soc.CleSignature.</summary>
internal sealed record ContexteSignature(string Greffe, string Fuseau, int IdCle, string EmpreinteCle, ECDsa Cle);

/// <summary>
/// LES CHAINES SIGNEES DE LA V3 (SOC-018 T02, T04).
///
/// - L'OUVERTURE au passage (<see cref="OuvrirAsync"/>, CA01) : les 7 chaines commencent vides, l'evenement 260
///   « Initialisation des donnees » est leur premier maillon. Rien n'est lu ni ecrit dans la base V2 (CA02).
/// - La SIGNATURE d'une piece (<see cref="SignerFactureAsync"/>, <see cref="SignerEncaissementAsync"/>,
///   <see cref="SignerDuplicataAsync"/>) dans la transaction de son enregistrement : son rang suit le dernier de la
///   chaine, sa pre-image reprend la signature du maillon precedent (CA03). Le domaine Facturation l'appelle.
/// - La SIGNATURE des evenements : n'importe quel programme ecrit un evenement sans signature
///   (<see cref="EcrireEvenementAsync"/>, JournalGestes, la livraison, les declencheurs SQL) ; il est signe dans l'ordre
///   par <see cref="SignerEnAttenteAsync(SqlConnection, CancellationToken)"/> : a chaque geste des chaines, et par
///   <see cref="SignatureDeFond"/> (comme le job Job_Exec_Hash_JET de la V2).
/// - La certification n'a PAS d'interrupteur (CA06) : aucune lecture de ACTIVATION_CERTIF_NF525, aucune base V2.
///
/// Les controles et les clotures sont dans ControleIntegrite.cs et ServiceChaines.Clotures.cs.
/// </summary>
public sealed partial class ServiceChaines(ICoffreCles coffre)
{
    public const string ContexteSignatureSql = "rigv3.signature";
    public const string ContextePurgeSql = "rigv3.purge-jet";

    // Les codes du catalogue (soc.CodeEvenement, CODE_JET de la V2) qu'ecrit la V3 elle-meme.
    public const short CodeChangementMode = 10;
    public const short CodeArchiveExercice = 30;
    public const short CodeCloturePeriode = 50;
    public const short CodeClotureExercice = 60;
    public const short CodeDefautIntegrite = 90;
    public const short CodeRuptureSequence = 95;
    public const short CodeDuplicata = 155;
    public const short CodePurgePartielle = 205;
    public const short CodeInitialisation = 260;
    public const short CodeChangementCle = 450;

    private const string Signataire = "Partie serveur de la V3";

    // =====================================================================================================
    // Ouverture (CA01, CA02, CA06 ; T04)
    // =====================================================================================================

    /// <summary>
    /// Ouvre les 7 chaines du greffe dans sa base V3 : vides, avec l'evenement 260 en premier maillon du journal non
    /// purgeable. Un evenement ecrit avant (livraison 250, reprise 210) est signe APRES lui, dans son ordre. Une seconde
    /// ouverture est refusee sans rien changer. Aucune base V2 n'est ouverte.
    /// </summary>
    public async Task<EtatCertification> OuvrirAsync(SqlConnection cn, string codeGreffe, string fuseau, string auteur,
                                                     TraceGeste? trace, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(codeGreffe)) throw new RefusChaines("Le code du greffe est obligatoire.");
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(fuseau ?? "", out _))
            throw new RefusChaines($"Le fuseau « {fuseau} » est inconnu : il dit a quelle heure se lisent les jours et l'exercice du greffe.");
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        await VerrouAsync(cn, tx, "ouverture", ct);
        if (await ScalaireAsync<int>(cn, tx, "SELECT COUNT(*) FROM soc.Certification;", ct) > 0)
            throw new ChainesDejaOuvertes($"Les chaines signees du greffe {codeGreffe} sont deja ouvertes : rien n'a change.");
        var deja = await ScalaireAsync<int>(cn, tx, """
            SELECT (SELECT COUNT(*) FROM soc.FactureSignee) + (SELECT COUNT(*) FROM soc.EncaissementSigne)
                 + (SELECT COUNT(*) FROM soc.GrandTotalFactures) + (SELECT COUNT(*) FROM soc.GrandTotalEncaissements)
                 + (SELECT COUNT(*) FROM soc.DuplicataSigne) + (SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Seq IS NOT NULL);
            """, ct);
        if (deja > 0) throw new RefusChaines($"La base porte deja {deja} maillon(s) signe(s) sans ouverture : elle ne s'ouvre pas une seconde fois.");

        await using (var cmd = new SqlCommand("""
            INSERT INTO soc.Certification (Id, CodeGreffe, Fuseau, OuvertePar) VALUES (1, @greffe, @fuseau, @auteur);
            -- Le journal purgeable a une ligne par exercice, posee a son premier evenement.
            INSERT INTO soc.EtatChaine (Chaine) VALUES ('FAC'), ('ENC'), ('GTF'), ('GTE'), ('DUP'), ('JNP');
            """, cn, tx))
        {
            cmd.Parameters.Add(P("@greffe", SqlDbType.VarChar, 20, codeGreffe.Trim()));
            cmd.Parameters.Add(P("@fuseau", SqlDbType.VarChar, 64, fuseau));
            cmd.Parameters.Add(P("@auteur", SqlDbType.NVarChar, 128, Borne(auteur, 128)));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        var ctx = await ContexteAsync(cn, tx, ct);
        var detail = $"Initialisation des données : ouverture des 7 chaînes signées de la V3 au passage du greffe {codeGreffe.Trim()} " +
                     "(factures, encaissements, totaux des factures, totaux des encaissements, duplicatas, journal non purgeable, " +
                     "journal purgeable), vides. Les chaînes de la V2 restent dans la V2 : aucune ligne copiée, modifiée ni signée " +
                     $"de nouveau. Clé de signature {ctx.EmpreinteCle}.";
        var id260 = await EcrireEvenementAsync(cn, tx, CodeInitialisation, detail, auteur, null, ct);
        await SignerEnAttenteAsync(cn, tx, ctx, id260, ct);
        if (trace is not null) await trace(cn, tx, ct);
        await tx.CommitAsync(ct);
        return await LectureChaines.EtatAsync(cn, codeGreffe.Trim(), ct);
    }

    // =====================================================================================================
    // Signature des pieces (CA03)
    // =====================================================================================================

    /// <summary>Signe une facture dans la transaction de son enregistrement ; rend son maillon.</summary>
    public async Task<MaillonSigne> SignerFactureAsync(SqlConnection cn, SqlTransaction tx, PieceFacture f, CancellationToken ct = default)
    {
        Exiger(!string.IsNullOrWhiteSpace(f.Numero) && f.Numero.Length <= 25, "Le numero de facture (25 caracteres au plus) est obligatoire.");
        Exiger(f.Nature is "FACTURE" or "AVOIR", "La nature d'une facture est FACTURE ou AVOIR.");
        Exiger(!string.IsNullOrWhiteSpace(f.Client), "Le client de la facture est obligatoire.");
        Exiger(DeuxDecimales(f.MontantTtc) && f.Lignes.All(l => DeuxDecimales(l.Taux) && DeuxDecimales(l.MontantTtc)),
            "Les montants et les taux se signent au centime : deux decimales au plus.");
        var ctx = await ContexteAsync(cn, tx, ct);
        await VerrouAsync(cn, tx, ChainesSignees.Factures, ct);
        var (seq, precedente) = await ProchainAsync(cn, tx, ChainesSignees.Factures, "soc.FactureSignee", ct);
        var emise = ALaSeconde(f.EmiseLe);
        var signature = Signatures.Signer(ctx.Cle, Preimages.Facture(seq, precedente, f.Numero, f.Nature, emise, f.MontantTtc, f.Client,
            f.CodePostal, f.TvaIntra, f.Lignes.Select(l => (l.Taux, l.MontantTtc))));
        await SousSignatureAsync(cn, tx, async () =>
        {
            long id;
            await using (var cmd = new SqlCommand("""
                INSERT INTO soc.FactureSignee (Numero, Nature, EmiseLe, MontantTtc, Client, CodePostal, TvaIntra, Seq, Signature, IdCle)
                VALUES (@numero, @nature, @emise, @montant, @client, @cp, @tva, @seq, @signature, @cle);
                SELECT CAST(SCOPE_IDENTITY() AS bigint);
                """, cn, tx))
            {
                cmd.Parameters.Add(P("@numero", SqlDbType.VarChar, 25, f.Numero));
                cmd.Parameters.Add(P("@nature", SqlDbType.VarChar, 7, f.Nature));
                cmd.Parameters.Add(new SqlParameter("@emise", SqlDbType.DateTime2) { Scale = 0, Value = emise });
                cmd.Parameters.Add(Montant("@montant", f.MontantTtc));
                cmd.Parameters.Add(P("@client", SqlDbType.NVarChar, 100, f.Client));
                cmd.Parameters.Add(P("@cp", SqlDbType.VarChar, 10, f.CodePostal));
                cmd.Parameters.Add(P("@tva", SqlDbType.VarChar, 15, f.TvaIntra));
                AjouterSignature(cmd, seq, signature, ctx.IdCle);
                id = (long)(await cmd.ExecuteScalarAsync(ct))!;
            }
            for (var i = 0; i < f.Lignes.Count; i++)
            {
                await using var ligne = new SqlCommand(
                    "INSERT INTO soc.FactureSigneeLigne (IdFacture, Rang, Taux, MontantTtc) VALUES (@id, @rang, @taux, @montant);", cn, tx);
                ligne.Parameters.Add(new SqlParameter("@id", SqlDbType.BigInt) { Value = id });
                ligne.Parameters.Add(new SqlParameter("@rang", SqlDbType.SmallInt) { Value = (short)(i + 1) });
                ligne.Parameters.Add(new SqlParameter("@taux", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = f.Lignes[i].Taux });
                ligne.Parameters.Add(Montant("@montant", f.Lignes[i].MontantTtc));
                await ligne.ExecuteNonQueryAsync(ct);
            }
        }, ct);
        await AvancerAsync(cn, tx, ChainesSignees.Factures, seq, ct);
        await SignerEnAttenteAsync(cn, tx, ctx, null, ct);
        return new MaillonSigne(ChainesSignees.Factures, seq, signature);
    }

    /// <summary>Signe un encaissement dans la transaction de son enregistrement ; rend son maillon.</summary>
    public async Task<MaillonSigne> SignerEncaissementAsync(SqlConnection cn, SqlTransaction tx, PieceEncaissement e, CancellationToken ct = default)
    {
        Exiger(!string.IsNullOrWhiteSpace(e.Numero) && e.Numero.Length <= 32, "Le numero d'encaissement (32 caracteres au plus) est obligatoire.");
        Exiger(DeuxDecimales(e.Montant), "Un montant se signe au centime : deux decimales au plus.");
        var ctx = await ContexteAsync(cn, tx, ct);
        await VerrouAsync(cn, tx, ChainesSignees.Encaissements, ct);
        var (seq, precedente) = await ProchainAsync(cn, tx, ChainesSignees.Encaissements, "soc.EncaissementSigne", ct);
        var le = ALaSeconde(e.EncaisseLe);
        var signature = Signatures.Signer(ctx.Cle, Preimages.Encaissement(seq, precedente, e.Numero, le, e.Montant));
        await SousSignatureAsync(cn, tx, async () =>
        {
            await using var cmd = new SqlCommand("""
                INSERT INTO soc.EncaissementSigne (Numero, EncaisseLe, Montant, Seq, Signature, IdCle)
                VALUES (@numero, @le, @montant, @seq, @signature, @cle);
                """, cn, tx);
            cmd.Parameters.Add(P("@numero", SqlDbType.VarChar, 32, e.Numero));
            cmd.Parameters.Add(new SqlParameter("@le", SqlDbType.DateTime2) { Scale = 0, Value = le });
            cmd.Parameters.Add(Montant("@montant", e.Montant));
            AjouterSignature(cmd, seq, signature, ctx.IdCle);
            await cmd.ExecuteNonQueryAsync(ct);
        }, ct);
        await AvancerAsync(cn, tx, ChainesSignees.Encaissements, seq, ct);
        await SignerEnAttenteAsync(cn, tx, ctx, null, ct);
        return new MaillonSigne(ChainesSignees.Encaissements, seq, signature);
    }

    /// <summary>
    /// Signe le duplicata d'une facture signee dans la V3 (il reprend sa signature et celle du duplicata precedent de la
    /// chaine) et ecrit l'evenement 155 « Duplicata d'une note » (obligatoire, CODE_JET de la V2).
    /// </summary>
    public async Task<MaillonSigne> SignerDuplicataAsync(SqlConnection cn, SqlTransaction tx, PieceDuplicata d, CancellationToken ct = default)
    {
        Exiger(!string.IsNullOrWhiteSpace(d.Motif) && d.Motif.Length <= 200, "Le motif du duplicata (200 caracteres au plus) est obligatoire.");
        Exiger(!string.IsNullOrWhiteSpace(d.Compte), "Le compte qui imprime le duplicata est obligatoire.");
        var ctx = await ContexteAsync(cn, tx, ct);
        await VerrouAsync(cn, tx, ChainesSignees.Duplicatas, ct);
        var signatureFacture = await ScalaireOuNulAsync<string>(cn, tx, "SELECT Signature FROM soc.FactureSignee WHERE Numero = @n;", ct,
            P("@n", SqlDbType.VarChar, 25, d.NumeroFacture))
            ?? throw new RefusChaines($"La facture {d.NumeroFacture} n'est pas signee dans la V3 : son duplicata ne s'y signe pas.");
        var rang = await ScalaireAsync<int>(cn, tx, "SELECT COUNT(*) + 1 FROM soc.DuplicataSigne WHERE NumeroFacture = @n;", ct,
            P("@n", SqlDbType.VarChar, 25, d.NumeroFacture));
        var (seq, precedente) = await ProchainAsync(cn, tx, ChainesSignees.Duplicatas, "soc.DuplicataSigne", ct);
        var le = ALaSeconde(d.ImprimeLe);
        var signature = Signatures.Signer(ctx.Cle, Preimages.Duplicata(seq, precedente, d.NumeroFacture, signatureFacture, rang, le, d.Motif, d.Compte));
        await SousSignatureAsync(cn, tx, async () =>
        {
            await using var cmd = new SqlCommand("""
                INSERT INTO soc.DuplicataSigne (NumeroFacture, Rang, ImprimeLe, Motif, Compte, Seq, Signature, IdCle)
                VALUES (@facture, @rang, @le, @motif, @compte, @seq, @signature, @cle);
                """, cn, tx);
            cmd.Parameters.Add(P("@facture", SqlDbType.VarChar, 25, d.NumeroFacture));
            cmd.Parameters.Add(new SqlParameter("@rang", SqlDbType.Int) { Value = rang });
            cmd.Parameters.Add(new SqlParameter("@le", SqlDbType.DateTime2) { Scale = 0, Value = le });
            cmd.Parameters.Add(P("@motif", SqlDbType.NVarChar, 200, d.Motif));
            cmd.Parameters.Add(P("@compte", SqlDbType.NVarChar, 128, d.Compte));
            AjouterSignature(cmd, seq, signature, ctx.IdCle);
            await cmd.ExecuteNonQueryAsync(ct);
        }, ct);
        await AvancerAsync(cn, tx, ChainesSignees.Duplicatas, seq, ct);
        await EcrireEvenementAsync(cn, tx, CodeDuplicata,
            $"Duplicata n° {rang} de la facture {d.NumeroFacture} imprimé par {d.Compte} : {d.Motif}", d.Compte, null, ct);
        await SignerEnAttenteAsync(cn, tx, ctx, null, ct);
        return new MaillonSigne(ChainesSignees.Duplicatas, seq, signature);
    }

    // =====================================================================================================
    // Evenements
    // =====================================================================================================

    /// <summary>
    /// Ecrit un evenement du catalogue, SANS le signer (il le sera dans l'ordre, au plus tard au passage suivant de la
    /// signature). Rend son identifiant. Un code hors du catalogue est refuse.
    /// </summary>
    public static async Task<long> EcrireEvenementAsync(SqlConnection cn, SqlTransaction tx, short code, string detail, string auteur,
                                                        int? idAuteur, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand("""
            INSERT INTO soc.EvenementTechnique (Code, Libelle, Detail, IdAuteur, Auteur)
            SELECT @code, LEFT(Libelle, 120), @detail, @idAuteur, @auteur FROM soc.CodeEvenement WHERE Code = @code;
            IF @@ROWCOUNT = 0 THROW 51018, N'Code d''evenement absent du catalogue soc.CodeEvenement (CODE_JET).', 1;
            SELECT CAST(SCOPE_IDENTITY() AS bigint);
            """, cn, tx);
        cmd.Parameters.Add(new SqlParameter("@code", SqlDbType.SmallInt) { Value = code });
        cmd.Parameters.Add(P("@detail", SqlDbType.NVarChar, 1000, Borne(detail, 1000)));
        cmd.Parameters.Add(new SqlParameter("@idAuteur", SqlDbType.Int) { Value = (object?)idAuteur ?? DBNull.Value });
        cmd.Parameters.Add(P("@auteur", SqlDbType.VarChar, 80, Borne(string.IsNullOrWhiteSpace(auteur) ? Signataire : auteur, 80)));
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    /// <summary>
    /// Signe, dans l'ordre de leur ecriture, les evenements qui attendent leur signature ; rend combien. Sans effet (0)
    /// sur une base sans chaines ouvertes. Un evenement tenu par une transaction en cours est
    /// laisse pour le passage suivant (READPAST) : jamais d'attente sur le travail d'un autre.
    /// </summary>
    public async Task<int> SignerEnAttenteAsync(SqlConnection cn, CancellationToken ct = default)
    {
        await using (var cmd = new SqlCommand(
            "IF OBJECT_ID(N'soc.Certification', N'U') IS NULL SELECT 0 ELSE EXEC (N'SELECT COUNT(*) FROM soc.Certification');", cn))
            if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 0) return 0;
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        var ctx = await ContexteAsync(cn, tx, ct);
        var n = await SignerEnAttenteAsync(cn, tx, ctx, null, ct);
        await tx.CommitAsync(ct);
        return n;
    }

    /// <summary>La signature des evenements en attente, dans la transaction <paramref name="tx"/> ; <paramref name="enPremier"/> d'abord.</summary>
    internal async Task<int> SignerEnAttenteAsync(SqlConnection cn, SqlTransaction tx, ContexteSignature ctx, long? enPremier, CancellationToken ct)
    {
        await VerrouAsync(cn, tx, ChainesSignees.JournalNonPurgeable, ct);
        await VerrouAsync(cn, tx, ChainesSignees.JournalPurgeable, ct);
        var fuseau = TimeZoneInfo.FindSystemTimeZoneById(ctx.Fuseau);
        var signes = 0;
        while (true)
        {
            var lot = new List<(long Id, DateTime Instant, short Code, string Libelle, string Detail, string Auteur, bool Purgeable)>();
            await using (var cmd = new SqlCommand("""
                SELECT TOP (200) e.Id, e.Instant, e.Code, e.Libelle, e.Detail, e.Auteur, c.Purgeable
                FROM soc.EvenementTechnique e WITH (READPAST)
                JOIN soc.CodeEvenement c ON c.Code = e.Code
                WHERE e.Seq IS NULL
                ORDER BY CASE WHEN e.Id = @premier THEN 0 ELSE 1 END, e.Id;
                """, cn, tx))
            {
                cmd.Parameters.Add(new SqlParameter("@premier", SqlDbType.BigInt) { Value = (object?)enPremier ?? DBNull.Value });
                await using var l = await cmd.ExecuteReaderAsync(ct);
                while (await l.ReadAsync(ct))
                    lot.Add((l.GetInt64(0), l.GetDateTime(1), l.GetInt16(2), l.GetString(3), l.GetString(4), l.GetString(5), l.GetBoolean(6)));
            }
            if (lot.Count == 0) return signes;
            foreach (var e in lot)
            {
                var chaine = e.Purgeable ? ChainesSignees.JournalPurgeable : ChainesSignees.JournalNonPurgeable;
                var exercice = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(e.Instant, DateTimeKind.Utc), fuseau).Year;
                var cleEtat = chaine == ChainesSignees.JournalPurgeable ? (short)exercice : (short)0;
                var seq = await ScalaireAsync<long>(cn, tx, """
                    IF NOT EXISTS (SELECT 1 FROM soc.EtatChaine WHERE Chaine = @c AND Exercice = @x)
                        INSERT INTO soc.EtatChaine (Chaine, Exercice) VALUES (@c, @x);
                    SELECT DerniereSeq FROM soc.EtatChaine WHERE Chaine = @c AND Exercice = @x;
                    """, ct, P("@c", SqlDbType.VarChar, 3, chaine), new SqlParameter("@x", SqlDbType.SmallInt) { Value = cleEtat }) + 1;
                // Le journal purgeable repart a chaque exercice (SP_HashJETR19_P) : une purge retire un exercice entier, la
                // chaine ne doit pas dependre d'un exercice anterieur.
                var precedente = await ScalaireOuNulAsync<string>(cn, tx, chaine == ChainesSignees.JournalNonPurgeable
                    ? "SELECT TOP (1) Signature FROM soc.EvenementTechnique WHERE Chaine = 'JNP' AND Seq < @s ORDER BY Seq DESC;"
                    : "SELECT TOP (1) Signature FROM soc.EvenementTechnique WHERE Chaine = 'JP' AND Exercice = @x AND Seq < @s ORDER BY Seq DESC;",
                    ct, new SqlParameter("@s", SqlDbType.BigInt) { Value = seq }, new SqlParameter("@x", SqlDbType.SmallInt) { Value = (short)exercice });
                var signature = Signatures.Signer(ctx.Cle,
                    Preimages.Evenement(chaine, seq, precedente, e.Id, exercice, e.Instant, e.Code, e.Libelle, e.Detail, e.Auteur));
                await SousSignatureAsync(cn, tx, async () =>
                {
                    await using var maj = new SqlCommand("""
                        UPDATE soc.EvenementTechnique
                        SET Chaine = @c, Exercice = @x, Seq = @seq, Signature = @signature, IdCle = @cle, SigneLe = SYSUTCDATETIME()
                        WHERE Id = @id AND Seq IS NULL;
                        """, cn, tx);
                    maj.Parameters.Add(P("@c", SqlDbType.VarChar, 3, chaine));
                    maj.Parameters.Add(new SqlParameter("@x", SqlDbType.SmallInt) { Value = (short)exercice });
                    maj.Parameters.Add(new SqlParameter("@id", SqlDbType.BigInt) { Value = e.Id });
                    AjouterSignature(maj, seq, signature, ctx.IdCle);
                    if (await maj.ExecuteNonQueryAsync(ct) != 1)
                        throw new InvalidOperationException($"L'evenement {e.Id} a ete signe par un autre pendant cette signature.");
                }, ct);
                await AvancerAsync(cn, tx, chaine, seq, ct, cleEtat);
                signes++;
            }
        }
    }

    // =====================================================================================================
    // Communs
    // =====================================================================================================

    /// <summary>
    /// La copie anonymisee d'une base de greffe (SOC-021, lot 6) porte la propriete RIGV3_ANONYMISATION (sur toute copie,
    /// Outils\Anonymisation\Copie.cs) ou la ligne anonymisation.Copie (ChainesInvalidees = 1). Rend la reserve a dire, ou
    /// null pour une vraie base. Seul le CONTROLE en tient compte (decision du lot 6 : sur une copie, il ne vaut pas
    /// preuve) ; l'ouverture et la signature y restent possibles, pour repeter le passage (SOC-025) sur une copie.
    /// </summary>
    // fix-ok: la garde « copie » refusait aussi l'ouverture et la signature ; or le lot 6 pose RIGV3_ANONYMISATION sur TOUTE copie (Outils/Anonymisation/Copie.cs:382, lu) et la repetition du passage (SOC-025) s'y joue : la decision du lot 6 ne vise que la valeur probante du controle.
    public static async Task<string?> CopieAnonymiseeAsync(SqlConnection cn, SqlTransaction? tx, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            DECLARE @copie int = CASE WHEN EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 0 AND name = N'RIGV3_ANONYMISATION') THEN 1 ELSE 0 END;
            IF @copie = 0 AND OBJECT_ID(N'anonymisation.Copie', N'U') IS NOT NULL AND COL_LENGTH(N'anonymisation.Copie', N'ChainesInvalidees') IS NOT NULL
                EXEC sys.sp_executesql N'SELECT @c = CASE WHEN EXISTS (SELECT 1 FROM anonymisation.Copie WHERE ChainesInvalidees = 1) THEN 1 ELSE 0 END;',
                     N'@c int OUTPUT', @c = @copie OUTPUT;
            SELECT @copie;
            """, cn, tx);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1
            ? "Cette base est une copie anonymisée (SOC-021) : ses chaînes signées sont cassées par l'anonymisation, un contrôle n'y prouve rien."
            : null;
    }

    /// <summary>Le greffe, son fuseau et la cle qui signe ; une cle neuve est inscrite (et, apres la premiere, l'evenement 450).</summary>
    internal async Task<ContexteSignature> ContexteAsync(SqlConnection cn, SqlTransaction tx, CancellationToken ct)
    {
        string greffe, fuseau;
        await using (var cmd = new SqlCommand("SELECT CodeGreffe, Fuseau FROM soc.Certification WHERE Id = 1;", cn, tx))
        await using (var l = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await l.ReadAsync(ct))
                throw new ChainesNonOuvertes("Les chaînes signées de la V3 ne sont pas ouvertes dans cette base : elles s'ouvrent au passage du greffe. Rien n'a été signé.");
            greffe = l.GetString(0);
            fuseau = l.GetString(1);
        }
        var cle = coffre.CleDuGreffe(greffe);
        var spki = cle.ExportSubjectPublicKeyInfo();
        var empreinte = Signatures.Empreinte(spki);
        var id = await ScalaireOuNulAsync<int?>(cn, tx, "SELECT Id FROM soc.CleSignature WHERE Empreinte = @e;", ct, P("@e", SqlDbType.Char, 64, empreinte));
        if (id is null)
        {
            var premiere = await ScalaireAsync<int>(cn, tx, "SELECT COUNT(*) FROM soc.CleSignature;", ct) == 0;
            await using (var ins = new SqlCommand("""
                INSERT INTO soc.CleSignature (Empreinte, ClePublique, Algorithme, Conteneur) VALUES (@e, @spki, @algo, @conteneur);
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """, cn, tx))
            {
                ins.Parameters.Add(P("@e", SqlDbType.Char, 64, empreinte));
                ins.Parameters.Add(new SqlParameter("@spki", SqlDbType.VarBinary, 400) { Value = spki });
                ins.Parameters.Add(P("@algo", SqlDbType.VarChar, 30, Signatures.Algorithme));
                ins.Parameters.Add(P("@conteneur", SqlDbType.NVarChar, 128, coffre.Conteneur(greffe)));
                id = (int)(await ins.ExecuteScalarAsync(ct))!;
            }
            if (!premiere)
                await EcrireEvenementAsync(cn, tx, CodeChangementCle,
                    $"Changement du trousseau de clés de signature : nouvelle clé {empreinte} ({coffre.Conteneur(greffe)}).", Signataire, null, ct);
        }
        return new ContexteSignature(greffe, fuseau, id.Value, empreinte, cle);
    }

    /// <summary>Le rang du prochain maillon et la signature du maillon present le plus proche en dessous (meme regle qu'au controle).</summary>
    private static async Task<(long Seq, string? Precedente)> ProchainAsync(SqlConnection cn, SqlTransaction tx, string chaine, string table, CancellationToken ct)
    {
        var seq = await ScalaireAsync<long>(cn, tx, "SELECT DerniereSeq FROM soc.EtatChaine WHERE Chaine = @c AND Exercice = 0;", ct,
            P("@c", SqlDbType.VarChar, 3, chaine)) + 1;
        var precedente = await ScalaireOuNulAsync<string>(cn, tx, $"SELECT TOP (1) Signature FROM {table} WHERE Seq < @s ORDER BY Seq DESC;", ct,
            new SqlParameter("@s", SqlDbType.BigInt) { Value = seq });
        return (seq, precedente);
    }

    /// <summary>Le dernier maillon de la chaine (de l'exercice, pour le journal purgeable) passe a <paramref name="seq"/> ;
    /// il ne recule ni ne saute jamais.</summary>
    internal static async Task AvancerAsync(SqlConnection cn, SqlTransaction tx, string chaine, long seq, CancellationToken ct, short exercice = 0)
    {
        await using var cmd = new SqlCommand(
            "UPDATE soc.EtatChaine SET DerniereSeq = @s, MisAJourLe = SYSUTCDATETIME() WHERE Chaine = @c AND Exercice = @x AND DerniereSeq = @s - 1;", cn, tx);
        cmd.Parameters.Add(P("@c", SqlDbType.VarChar, 3, chaine));
        cmd.Parameters.Add(new SqlParameter("@x", SqlDbType.SmallInt) { Value = exercice });
        cmd.Parameters.Add(new SqlParameter("@s", SqlDbType.BigInt) { Value = seq });
        if (await cmd.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidOperationException($"La chaine {chaine} a bouge pendant la signature du maillon {seq} : rien n'est signe.");
    }

    /// <summary>Un verrou exclusif par chaine, jusqu'a la fin de la transaction : deux signatures d'une meme chaine passent l'une apres l'autre.
    /// Ordre de prise : la chaine de la piece, puis JNP, puis JP.</summary>
    internal static async Task VerrouAsync(SqlConnection cn, SqlTransaction tx, string chaine, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            DECLARE @r int;
            EXEC @r = sys.sp_getapplock @Resource = @ressource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
            IF @r < 0 THROW 51019, N'Chaine signee tenue par une autre signature au-dela de 15 s : rien n''est signe, reessayer.', 1;
            """, cn, tx);
        cmd.Parameters.Add(P("@ressource", SqlDbType.NVarChar, 255, "rigv3:chaine:" + chaine));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// L'ecriture dans une table signee, la seule que permettent ses declencheurs (SESSION_CONTEXT rigv3.signature) ;
    /// la cle est retiree juste apres. En cas d'erreur, la transaction de l'appelant est annulee et sa connexion fermee :
    /// le pool remet le contexte a zero (sp_reset_connection).
    /// </summary>
    internal static async Task SousSignatureAsync(SqlConnection cn, SqlTransaction tx, Func<Task> ecriture, CancellationToken ct)
        => await SousContexteAsync(cn, tx, ContexteSignatureSql, ecriture, ct);

    internal static async Task SousContexteAsync(SqlConnection cn, SqlTransaction tx, string cle, Func<Task> ecriture, CancellationToken ct)
    {
        await PoserContexteAsync(cn, tx, cle, 1, ct);
        try { await ecriture(); }
        finally { await PoserContexteAsync(cn, tx, cle, null, CancellationToken.None); }
    }

    private static async Task PoserContexteAsync(SqlConnection cn, SqlTransaction tx, string cle, int? valeur, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("EXEC sys.sp_set_session_context @key = @cle, @value = @valeur;", cn, tx);
        cmd.Parameters.Add(P("@cle", SqlDbType.NVarChar, 128, cle));
        cmd.Parameters.Add(new SqlParameter("@valeur", SqlDbType.Int) { Value = (object?)valeur ?? DBNull.Value });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AjouterSignature(SqlCommand cmd, long seq, string signature, int idCle)
    {
        cmd.Parameters.Add(new SqlParameter("@seq", SqlDbType.BigInt) { Value = seq });
        cmd.Parameters.Add(P("@signature", SqlDbType.VarChar, 120, signature));
        cmd.Parameters.Add(new SqlParameter("@cle", SqlDbType.Int) { Value = idCle });
    }

    internal static SqlParameter P(string nom, SqlDbType type, int taille, string? valeur)
        => new(nom, type, taille) { Value = (object?)valeur ?? DBNull.Value };

    internal static SqlParameter Montant(string nom, decimal valeur)
        => new(nom, SqlDbType.Decimal) { Precision = 19, Scale = 2, Value = valeur };

    internal static async Task<T> ScalaireAsync<T>(SqlConnection cn, SqlTransaction? tx, string sql, CancellationToken ct, params SqlParameter[] p)
    {
        var v = await ScalaireOuNulAsync<object>(cn, tx, sql, ct, p)
                ?? throw new InvalidOperationException("Lecture sans resultat : " + sql);
        return (T)Convert.ChangeType(v, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static async Task<T?> ScalaireOuNulAsync<T>(SqlConnection cn, SqlTransaction? tx, string sql, CancellationToken ct, params SqlParameter[] p)
    {
        await using var cmd = new SqlCommand(sql, cn, tx);
        foreach (var x in p) cmd.Parameters.Add(x);
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is null or DBNull ? default : (T)v;
    }

    internal static DateTime ALaSeconde(DateTime d) => new(d.Ticks - d.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Unspecified);

    internal static DateTime ALaMilliseconde(DateTime d) => new(d.Ticks - d.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Unspecified);

    private static bool DeuxDecimales(decimal m) => decimal.Round(m, 2) == m;

    private static void Exiger(bool ok, string motif) { if (!ok) throw new RefusChaines(motif); }

    internal static string Borne(string? texte, int taille) => texte is null ? "" : texte.Length <= taille ? texte : texte[..taille];
}
