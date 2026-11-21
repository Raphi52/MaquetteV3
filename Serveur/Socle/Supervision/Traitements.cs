// fix-ok: fichier neuf du lot 2 ; la 1re ecriture laissait PrendreAsync vide et TerminerAsync absent (relu avant toute
// compilation), la 2e les ecrit, la 3e pose l'etat « en erreur » AVANT l'ecriture en base pour qu'une base commune muette
// n'empeche pas l'arret du traitement (relecture du chemin d'erreur, pas un essai a l'aveugle).
using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;

namespace RigV3.Serveur.Socle.Supervision;

// ============================================================
// LES TRAITEMENTS DE FOND (SOC-011 T02, lot 2 de la phase 2).
//
// La V2 : des services Windows (Rig_Batch, Rig_Email...) dont les messages attendent dans COMMUN_RIG.dbo.AMIMESSAGE ;
// on les demarre et on les arrete sur le serveur par l'outil MaintenanceServices (catalogue-services.md:323), et
// chacun ecrit son etat dans ETAT_SERVICE.
// La V3 : un traitement est une classe du serveur qui implemente ITraitementDeFond, inscrite par son module
// (services.AddSingleton<ITraitementDeFond, MonTraitement>()). Ce qu'il a a faire attend dans soc.FileTraitement
// (Base\migrations\SOC-011_supervision.sql), deposee par MoteurTraitements.DeposerAsync. Le moteur prend les
// elements un a un ; une erreur ARRETE le traitement (etat « en erreur »), l'element reste en file, l'erreur va
// au journal technique avec le greffe de l'element. L'exploitant le relance depuis la supervision (CA04) : il
// reprend la ou il en etait, rien n'est perdu ; un element laisse « en cours » par un arret du serveur est remis
// en attente au demarrage.
// Aujourd'hui aucun traitement du socle n'est encore ecrit (courriels, recopie : lots suivants) ; le banc
// tests\SocleSupervisionCheck en inscrit un pour prouver le moteur.
// ============================================================

/// <summary>Un traitement de fond : il fait UN element de sa file ; une exception l'arrete (« en erreur »), l'element reste en file.</summary>
public interface ITraitementDeFond
{
    /// <summary>Le nom montre a l'exploitant et ecrit dans les alertes (100 caracteres au plus).</summary>
    string Nom { get; }

    Task TraiterAsync(ElementFile element, CancellationToken ct);
}

/// <summary>Un element de la file d'un traitement.</summary>
/// <param name="Essais">Combien de fois il a deja ete pris (et a echoue).</param>
public sealed record ElementFile(long Id, string Traitement, string? CodeGreffe, string Charge, int Essais);

/// <summary>L'etat d'un traitement a un instant, tel que la supervision le publie.</summary>
public sealed record EtatVivant(string Traitement, string Etat, int EnCours, int EnAttente, int EnErreur, string? CodeGreffe, string? Message);

/// <summary>Le moteur des traitements de fond de ce serveur.</summary>
public sealed class MoteurTraitements(IEnumerable<ITraitementDeFond> inscrits, BaseRig commune, JournalErreurs journal,
                                      ReglagesSupervision reglages, ILogger<MoteurTraitements> log) : BackgroundService
{
    private readonly Dictionary<string, ITraitementDeFond> traitements =
        inscrits.ToDictionary(t => t.Nom, StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Vivant> vivants = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim reveil = new(0);

    private sealed class Vivant
    {
        public volatile string Etat = EtatsTraitement.EnMarche;
        public volatile bool Occupe;
        public string? Erreur;
        public string? GreffeErreur;
        public readonly SemaphoreSlim Passage = new(1, 1);
    }

    private Vivant De(string nom) => vivants.GetOrAdd(nom, _ => new Vivant());

    /// <summary>Les traitements de ce serveur, par nom.</summary>
    public IReadOnlyCollection<string> Noms => traitements.Keys;

    /// <summary>Le traitement existe-t-il sur ce serveur ?</summary>
    public bool Connait(string nom) => traitements.ContainsKey(nom);

    /// <summary>Depose un element dans la file du traitement ; rend son identifiant.</summary>
    public async Task<long> DeposerAsync(string traitement, string? greffe, string charge, CancellationToken ct = default)
    {
        if (!Connait(traitement)) throw new ArgumentException($"Traitement inconnu sur ce serveur : {traitement}.");
        await using var cn = await commune.OuvrirAsync(ct);
        await using var cmd = new SqlCommand("""
            INSERT INTO soc.FileTraitement (Traitement, CodeGreffe, Charge, DeposeLe, Etat)
            OUTPUT inserted.Id VALUES (@t, @g, @c, SYSUTCDATETIME(), 'en attente');
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@t", SqlDbType.VarChar, 100) { Value = traitements[traitement].Nom });
        cmd.Parameters.Add(new SqlParameter("@g", SqlDbType.VarChar, 20) { Value = (object?)greffe ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@c", SqlDbType.NVarChar, -1) { Value = charge });
        var id = (long)(await cmd.ExecuteScalarAsync(ct))!;
        reveil.Release();
        return id;
    }

    /// <summary>
    /// Fait les elements de la file du traitement, un a un, jusqu'a la vider ; s'arrete a la premiere erreur (etat
    /// « en erreur », element remis en attente, erreur au journal technique avec son greffe). Un traitement arrete ou en
    /// erreur ne fait rien : il attend sa relance. Rend le nombre d'elements faits.
    /// </summary>
    public async Task<int> PasserAsync(string nom, CancellationToken ct = default)
    {
        var traitement = traitements[nom];
        var vivant = De(traitement.Nom);
        await vivant.Passage.WaitAsync(ct);
        try
        {
            var faits = 0;
            while (!ct.IsCancellationRequested && vivant.Etat == EtatsTraitement.EnMarche)
            {
                var element = await PrendreAsync(traitement.Nom, ct);
                if (element is null) break;
                vivant.Occupe = true;
                try
                {
                    await traitement.TraiterAsync(element, ct);
                    await TerminerAsync(element.Id, null, ct);
                    faits++;
                }
                catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    var suivi = await journal.EcrireAsync(ErreurAEcrire.Depuis(e, Suivis.Nouveau(), NiveauxErreur.Erreur,
                        OriginesErreur.Traitement, element.CodeGreffe, null, reglages.Site, $"traitement {traitement.Nom}"), CancellationToken.None);
                    var message = Nettoyage.Texte(e.Message, 900) ?? e.GetType().Name;
                    // L'etat d'abord : si la base commune lache aussi, le traitement est arrete quand meme ; l'element,
                    // reste « en cours », revient en attente a la relance.
                    vivant.Erreur = $"{message} (suivi {suivi})";
                    vivant.GreffeErreur = element.CodeGreffe;
                    vivant.Etat = EtatsTraitement.EnErreur;
                    await TerminerAsync(element.Id, vivant.Erreur, CancellationToken.None);
                }
                finally
                {
                    vivant.Occupe = false;
                }
            }
            return faits;
        }
        finally
        {
            vivant.Passage.Release();
        }
    }

    /// <summary>Arrete le traitement : il finit l'element en cours et garde sa file.</summary>
    public void Arreter(string nom)
    {
        var vivant = De(traitements[nom].Nom);
        if (vivant.Etat == EtatsTraitement.EnMarche) vivant.Etat = EtatsTraitement.Arrete;
    }

    /// <summary>
    /// Relance un traitement arrete ou en erreur (CA04). Dans UNE transaction de la base commune : les elements laisses
    /// « en cours » reviennent en attente (rien n'est perdu), puis <paramref name="trace"/> ecrit le geste avec son
    /// auteur (journal des gestes) ; ensuite seulement le traitement repart. Null si le traitement n'est pas arrete.
    /// </summary>
    public async Task<ResultatRelance?> RelancerAsync(string nom, Func<SqlConnection, SqlTransaction, ResultatRelance, Task> trace,
                                                      CancellationToken ct)
    {
        var traitement = traitements[nom];
        var vivant = De(traitement.Nom);
        var avant = vivant.Etat;
        if (avant is not (EtatsTraitement.EnErreur or EtatsTraitement.Arrete)) return null;
        await using var cn = await commune.OuvrirAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        await using (var cmd = new SqlCommand(
            "UPDATE soc.FileTraitement SET Etat = 'en attente' WHERE Traitement = @t AND Etat = 'en cours';", cn, tx))
        {
            cmd.Parameters.Add(new SqlParameter("@t", SqlDbType.VarChar, 100) { Value = traitement.Nom });
            await cmd.ExecuteNonQueryAsync(ct);
        }
        int enFile;
        await using (var cmd = new SqlCommand(
            "SELECT COUNT(*) FROM soc.FileTraitement WHERE Traitement = @t AND Etat = 'en attente';", cn, tx))
        {
            cmd.Parameters.Add(new SqlParameter("@t", SqlDbType.VarChar, 100) { Value = traitement.Nom });
            enFile = (int)(await cmd.ExecuteScalarAsync(ct))!;
        }
        var resultat = new ResultatRelance(traitement.Nom, avant, EtatsTraitement.EnMarche, enFile);
        await trace(cn, tx, resultat);
        await tx.CommitAsync(ct);
        vivant.Erreur = null;
        vivant.GreffeErreur = null;
        vivant.Etat = EtatsTraitement.EnMarche;
        reveil.Release();
        return resultat;
    }

    /// <summary>L'etat de chaque traitement : son etat de marche et les compteurs de sa file (base commune).</summary>
    public async Task<IReadOnlyList<EtatVivant>> EtatsAsync(CancellationToken ct = default)
    {
        if (traitements.Count == 0) return [];
        var comptes = new Dictionary<string, (int EnCours, int EnAttente, int EnErreur)>(StringComparer.OrdinalIgnoreCase);
        await using (var cn = await commune.OuvrirAsync(ct))
        await using (var cmd = new SqlCommand("""
            SELECT Traitement,
                   SUM(CASE WHEN Etat = 'en cours' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN Etat = 'en attente' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN Etat = 'en attente' AND Erreur IS NOT NULL THEN 1 ELSE 0 END)
            FROM soc.FileTraitement WHERE Etat <> 'fait' GROUP BY Traitement;
            """, cn))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) comptes[r.GetString(0)] = (r.GetInt32(1), r.GetInt32(2), r.GetInt32(3));
        return [.. traitements.Values.OrderBy(t => t.Nom, StringComparer.OrdinalIgnoreCase).Select(t =>
        {
            var v = De(t.Nom);
            var (enCours, enAttente, enErreur) = comptes.GetValueOrDefault(t.Nom);
            var etat = v.Etat is EtatsTraitement.EnErreur or EtatsTraitement.Arrete ? v.Etat
                     : v.Occupe ? EtatsTraitement.TravauxEnCours
                     : enAttente > 0 ? EtatsTraitement.EnAttente
                     : EtatsTraitement.EnMarche;
            return new EtatVivant(t.Nom, etat, enCours, enAttente, enErreur,
                v.Etat == EtatsTraitement.EnErreur ? v.GreffeErreur : null,
                v.Etat == EtatsTraitement.EnErreur ? v.Erreur : null);
        })];
    }

    /// <summary>Au demarrage : un element laisse « en cours » par un arret du serveur revient en attente.</summary>
    public override async Task StartAsync(CancellationToken ct)
    {
        if (traitements.Count > 0 && commune.Connexion is not null)
        {
            try
            {
                await using var cn = await commune.OuvrirAsync(ct);
                await using var cmd = new SqlCommand(
                    $"UPDATE soc.FileTraitement SET Etat = 'en attente' WHERE Etat = 'en cours' AND Traitement IN ({string.Join(", ", traitements.Keys.Select((_, i) => "@t" + i))});", cn);
                var i = 0;
                foreach (var nom in traitements.Keys) cmd.Parameters.Add(new SqlParameter("@t" + i++, SqlDbType.VarChar, 100) { Value = nom });
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (SqlException e)
            {
                // Le serveur demarre quand meme (sante, autres routes) ; la reprise de file se refera a la relance.
                log.LogError(e, "Traitements : elements « en cours » non remis en attente au demarrage : {Cause}", e.Message);
            }
        }
        await base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        if (!reglages.Automatique || traitements.Count == 0 || commune.Connexion is null) return;
        while (!arret.IsCancellationRequested)
        {
            foreach (var nom in traitements.Keys)
            {
                try { await PasserAsync(nom, arret); }
                catch (Exception e) when (e is not OperationCanceledException || !arret.IsCancellationRequested)
                {
                    // La file elle-meme est injoignable (base commune) : on le journalise et on repasse plus tard.
                    log.LogError(e, "Traitement {Traitement} : file injoignable : {Cause}", nom, e.Message);
                }
            }
            try { await reveil.WaitAsync(reglages.AttenteFile, arret); }
            catch (OperationCanceledException) when (arret.IsCancellationRequested) { return; }
        }
    }

    /// <summary>Prend le plus ancien element en attente (« en cours ») ; null si la file est vide. READPAST : deux
    /// serveurs qui font le meme traitement ne prennent jamais le meme element.</summary>
    private async Task<ElementFile?> PrendreAsync(string nom, CancellationToken ct)
    {
        await using var cn = await commune.OuvrirAsync(ct);
        await using var cmd = new SqlCommand("""
            WITH Prochain AS (
                SELECT TOP (1) * FROM soc.FileTraitement WITH (ROWLOCK, UPDLOCK, READPAST)
                WHERE Traitement = @t AND Etat = 'en attente' ORDER BY Id)
            UPDATE Prochain SET Etat = 'en cours', Essais = Essais + 1
            OUTPUT inserted.Id, inserted.Traitement, inserted.CodeGreffe, inserted.Charge, inserted.Essais;
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@t", SqlDbType.VarChar, 100) { Value = nom });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)
            ? new ElementFile(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetInt32(4))
            : null;
    }

    /// <summary>L'element est fait (<paramref name="erreur"/> null) ou revient en attente avec son erreur.</summary>
    private async Task TerminerAsync(long id, string? erreur, CancellationToken ct)
    {
        await using var cn = await commune.OuvrirAsync(ct);
        await using var cmd = new SqlCommand(erreur is null
            ? "UPDATE soc.FileTraitement SET Etat = 'fait', TraiteLe = SYSUTCDATETIME(), Erreur = NULL WHERE Id = @id;"
            : "UPDATE soc.FileTraitement SET Etat = 'en attente', Erreur = @e WHERE Id = @id;", cn);
        cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.BigInt) { Value = id });
        if (erreur is not null) cmd.Parameters.Add(new SqlParameter("@e", SqlDbType.NVarChar, 1000) { Value = erreur.Length <= 1000 ? erreur : erreur[..1000] });
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
