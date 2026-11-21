using System.Data;
using System.Globalization;
using System.Reflection;
using Microsoft.Data.SqlClient;
using RigV3.Base;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.Supervision;

// ============================================================
// LE SURVEILLANT (SOC-011 T02, T03 ; NUM-007 CA05 -- lot 2 de la phase 2).
//
// A chaque passage (Supervision:IntervalleSecondes, 60 s par defaut), dans cet ordre :
//   1. chaque greffe du catalogue : sa base V3 repond-elle, a-t-elle toutes les migrations de la base commune ?
//      et les echecs de numerotation venus de l'ancien RIG, lus par Base\v2\echecs.sql (le MEME script que
//      l'exploitation lance a la main, embarque dans le serveur) sur l'instance de chaque greffe ;
//      le tout ecrit dans soc.EtatGreffe -- l'ecran de supervision lit cette table, sans se connecter a aucun
//      serveur de greffe (CA03) ;
//   2. l'etat de la partie serveur et de chacun de ses traitements, publie dans soc.EtatTraitement et son
//      historique (CA01) ;
//   3. les regles d'alerte (CA02) : base injoignable ou en retard de migrations, echec de numerotation, traitement
//      en erreur, file qui ne baisse plus depuis le delai regle. Une cause = une alerte OUVERTE (premiere panne :
//      alerte ; les passages suivants mettent son texte a jour sans la renvoyer) ; la cause disparue, l'alerte est
//      fermee ;
//   4. l'envoi des alertes ouvertes pas encore parties, par le canal (ICanalAlerte) ;
//   5. une fois par jour, la purge selon les durees ecrites dans soc.ConservationTechnique (CA05, SOC-017 CA04).
// La V2 n'a rien de tel : ETAT_SERVICE se lit a la main, sans le greffe, et n'est jamais purgee.
// ============================================================

/// <summary>Ce qu'a fait un passage de la supervision.</summary>
public sealed record BilanPassage(DateTime Instant, int Greffes, int AlertesOuvertes, int AlertesFermees, int AlertesEnvoyees);

/// <summary>Ce qu'a retire une purge, par journal.</summary>
public sealed record BilanPurge(int Erreurs, int Etats, int Alertes, int File);

public sealed class Surveillant(BaseRig commune, CatalogueGreffes catalogue, MoteurTraitements moteur, ICanalAlerte canal,
                                ReglagesSupervision reglages, ILogger<Surveillant> log) : BackgroundService
{
    private const string TraitementServeur = TraitementsSupervision.PartieServeur;
    private const string TraitementBase = TraitementsSupervision.BaseDuGreffe;
    private const string TraitementNumerotation = TraitementsSupervision.Numerotation;

    private static readonly string Version =
        typeof(Surveillant).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Surveillant).Assembly.GetName().Version?.ToString() ?? "?";

    private readonly DateTime demarre = DateTime.UtcNow;
    private readonly SemaphoreSlim unSeul = new(1, 1);
    private DateTime? dernierePurge;

    /// <summary>Un passage complet (voir l'en-tete du fichier) ; deux passages ne se chevauchent jamais.</summary>
    public async Task<BilanPassage> PasserAsync(CancellationToken ct = default)
    {
        await unSeul.WaitAsync(ct);
        try
        {
            var maintenant = DateTime.UtcNow;
            var greffes = await VerifierGreffesAsync(maintenant, ct);
            await PublierAsync(maintenant, ct);
            var (ouvertes, fermees) = await EvaluerAsync(maintenant, ct);
            var envoyees = await EnvoyerAsync(ct);
            if (dernierePurge is null || maintenant - dernierePurge >= TimeSpan.FromDays(1))
            {
                await PurgerAsync(maintenant, ct);
                dernierePurge = maintenant;
            }
            return new BilanPassage(maintenant, greffes, ouvertes, fermees, envoyees);
        }
        finally
        {
            unSeul.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        if (!reglages.Automatique || commune.Connexion is null) return;
        using var minuteur = new PeriodicTimer(reglages.Intervalle);
        while (await minuteur.WaitForNextTickAsync(arret))
        {
            try { await PasserAsync(arret); }
            catch (Exception e) when (e is not OperationCanceledException || !arret.IsCancellationRequested)
            {
                // Pas avalee : au journal technique (categorie RigV3.*) ; le passage suivant reessaie.
                log.LogError(e, "Supervision : passage en panne : {Cause}", e.Message);
            }
        }
    }

    // --- 1. Les greffes ---------------------------------------------------------------------------------

    private sealed record Sante(string Etat, string? Detail, int? Jouees, string? Derniere);

    private async Task<int> VerifierGreffesAsync(DateTime maintenant, CancellationToken ct)
    {
        var fiches = await catalogue.ListerAsync(ct);
        int attendues;
        await using (var cn = await commune.OuvrirAsync(ct))
            (attendues, _) = await Migrateur.EtatJournalAsync(cn, ct);
        // Toutes les bases ENSEMBLE : un greffe muet (10 s de delai de connexion) ne retarde pas les autres.
        var santes = await Task.WhenAll(fiches.Select(f => SanteAsync(f, attendues, ct)));
        var echecs = await LectureEchecs.LireAsync(catalogue, fiches, reglages.SessionEchecs, ct);

        await using var cnC = await commune.OuvrirAsync(ct);
        await using var tx = (SqlTransaction)await cnC.BeginTransactionAsync(ct);
        for (var i = 0; i < fiches.Count; i++)
        {
            var (f, s) = (fiches[i], santes[i]);
            var e = echecs.GetValueOrDefault(f.Code) ?? new LectureEchecs.Echecs(null, null, null, "échecs non lus");
            await using var cmd = new SqlCommand("""
                MERGE soc.EtatGreffe AS c
                USING (SELECT @code AS CodeGreffe) AS s ON c.CodeGreffe = s.CodeGreffe
                WHEN MATCHED THEN UPDATE SET Sante = @sante, Detail = @detail, MigrationsJouees = @jouees, DerniereMigration = @derniere,
                     Echecs = @echecs, PremierEchec = @premier, DernierEchec = @dernier, EchecsDetail = @echecsDetail, VerifieLe = @le
                WHEN NOT MATCHED THEN INSERT (CodeGreffe, Sante, Detail, MigrationsJouees, DerniereMigration, Echecs, PremierEchec, DernierEchec, EchecsDetail, VerifieLe)
                     VALUES (@code, @sante, @detail, @jouees, @derniere, @echecs, @premier, @dernier, @echecsDetail, @le);
                """, cnC, tx);
            cmd.Parameters.Add(new SqlParameter("@code", SqlDbType.VarChar, 20) { Value = f.Code });
            cmd.Parameters.Add(new SqlParameter("@sante", SqlDbType.VarChar, 22) { Value = s.Etat });
            cmd.Parameters.Add(new SqlParameter("@detail", SqlDbType.NVarChar, 1000) { Value = (object?)Couper(s.Detail, 1000) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@jouees", SqlDbType.Int) { Value = (object?)s.Jouees ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@derniere", SqlDbType.VarChar, 200) { Value = (object?)Couper(s.Derniere, 200) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@echecs", SqlDbType.Int) { Value = (object?)e.Nombre ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@premier", SqlDbType.DateTime2) { Value = (object?)e.Premier ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@dernier", SqlDbType.DateTime2) { Value = (object?)e.Dernier ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@echecsDetail", SqlDbType.NVarChar, 500) { Value = (object?)Couper(e.Detail, 500) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@le", SqlDbType.DateTime2) { Value = maintenant });
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return fiches.Count;
    }

    private async Task<Sante> SanteAsync(FicheGreffe f, int attendues, CancellationToken ct)
    {
        if (f.BaseV3CreeeLe is null)
            return new Sante(SantesGreffe.PasCreee, "Base V3 pas encore créée (catalogue, SOC-008).", null, null);
        try
        {
            await using var cn = await catalogue.OuvrirBaseV3Async(f.Code, ct);
            var (jouees, derniere) = await Migrateur.EtatJournalAsync(cn, ct);
            return jouees < attendues
                ? new Sante(SantesGreffe.MigrationsEnRetard,
                    $"{jouees} migration(s) jouée(s) sur {attendues} (dernière : {derniere ?? "aucune"}) : jouer Base\\RigV3.Migrations sur {f.BaseV3}.",
                    jouees, derniere)
                : new Sante(SantesGreffe.Ok, null, jouees, derniere);
        }
        catch (BaseGreffeInjoignable e)
        {
            return new Sante(SantesGreffe.Injoignable, e.Message, null, null);
        }
        catch (SqlException e)
        {
            return new Sante(SantesGreffe.Injoignable, $"Base {f.BaseV3} du greffe {f.Code} jointe mais illisible : {e.Message}", null, null);
        }
    }

    // --- 2. La publication des etats ---------------------------------------------------------------------

    private async Task PublierAsync(DateTime maintenant, CancellationToken ct)
    {
        var etats = new List<EtatVivant>
        {
            new(TraitementServeur, EtatsTraitement.EnMarche, 0, 0, 0, null,
                $"Version {Version}, démarrée le {demarre.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)} (UTC)."),
        };
        etats.AddRange(await moteur.EtatsAsync(ct));
        await using var cn = await commune.OuvrirAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        foreach (var e in etats)
        {
            await using var cmd = new SqlCommand("""
                MERGE soc.EtatTraitement AS c
                USING (SELECT @serveur AS Serveur, @traitement AS Traitement) AS s ON c.Serveur = s.Serveur AND c.Traitement = s.Traitement
                WHEN MATCHED THEN UPDATE SET Etat = @etat, EnCours = @enCours, EnAttente = @enAttente, EnErreur = @enErreur,
                     CodeGreffe = @greffe, Message = @message, PublieLe = @le,
                     FileBaisseLe = CASE WHEN @enAttente = 0 OR @enAttente < c.EnAttente THEN @le ELSE c.FileBaisseLe END
                WHEN NOT MATCHED THEN INSERT (Serveur, Traitement, CodeGreffe, Etat, EnCours, EnAttente, EnErreur, Message, PublieLe, FileBaisseLe)
                     VALUES (@serveur, @traitement, @greffe, @etat, @enCours, @enAttente, @enErreur, @message, @le, @le);
                INSERT INTO soc.EtatTraitementHistorique (Serveur, Traitement, CodeGreffe, Etat, EnCours, EnAttente, EnErreur, Message, PublieLe)
                VALUES (@serveur, @traitement, @greffe, @etat, @enCours, @enAttente, @enErreur, @message, @le);
                """, cn, tx);
            cmd.Parameters.Add(new SqlParameter("@serveur", SqlDbType.VarChar, 120) { Value = Couper(reglages.Site, 120) });
            cmd.Parameters.Add(new SqlParameter("@traitement", SqlDbType.VarChar, 100) { Value = Couper(e.Traitement, 100) });
            cmd.Parameters.Add(new SqlParameter("@greffe", SqlDbType.VarChar, 20) { Value = (object?)e.CodeGreffe ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@etat", SqlDbType.VarChar, 20) { Value = e.Etat });
            cmd.Parameters.Add(new SqlParameter("@enCours", SqlDbType.Int) { Value = e.EnCours });
            cmd.Parameters.Add(new SqlParameter("@enAttente", SqlDbType.Int) { Value = e.EnAttente });
            cmd.Parameters.Add(new SqlParameter("@enErreur", SqlDbType.Int) { Value = e.EnErreur });
            cmd.Parameters.Add(new SqlParameter("@message", SqlDbType.NVarChar, 1000) { Value = (object?)Couper(e.Message, 1000) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@le", SqlDbType.DateTime2) { Value = maintenant });
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    // --- 3. Les regles d'alerte ---------------------------------------------------------------------------

    private sealed record Cause(string Cle, string Regle, string? Greffe, string Traitement, string Erreur);

    private async Task<(int Ouvertes, int Fermees)> EvaluerAsync(DateTime maintenant, CancellationToken ct)
    {
        var causes = new List<Cause>();
        await using var cn = await commune.OuvrirAsync(ct);
        await using (var cmd = new SqlCommand(
            "SELECT CodeGreffe, Sante, Detail, Echecs, PremierEchec, DernierEchec, EchecsDetail FROM soc.EtatGreffe ORDER BY CodeGreffe;", cn))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                var code = r.GetString(0);
                var detail = r.IsDBNull(2) ? "" : r.GetString(2);
                switch (r.GetString(1))
                {
                    case SantesGreffe.Injoignable:
                        causes.Add(new($"{ReglesAlerte.BaseInjoignable}|{code}", ReglesAlerte.BaseInjoignable, code, TraitementBase,
                            $"La base V3 du greffe {code} ne répond pas : {detail}"));
                        break;
                    case SantesGreffe.MigrationsEnRetard:
                        causes.Add(new($"{ReglesAlerte.MigrationsEnRetard}|{code}", ReglesAlerte.MigrationsEnRetard, code, TraitementBase,
                            $"La base V3 du greffe {code} n'est pas à jour : {detail}"));
                        break;
                }
                if (!r.IsDBNull(3) && r.GetInt32(3) > 0)
                    causes.Add(new($"{ReglesAlerte.EchecNumerotation}|{code}", ReglesAlerte.EchecNumerotation, code, TraitementNumerotation,
                        $"{r.GetInt32(3)} échec(s) de numérotation dans l'ancien RIG depuis le {Date(r, 4)} (UTC), le dernier le {Date(r, 5)}" +
                        (r.IsDBNull(6) ? "." : $" ({r.GetString(6)}).")));
            }

        await using (var cmd = new SqlCommand("""
            SELECT t.Serveur, t.Traitement, t.Etat, t.EnAttente, t.CodeGreffe, t.Message, t.FileBaisseLe,
                   (SELECT STRING_AGG(g.CodeGreffe, ', ') FROM (SELECT DISTINCT f.CodeGreffe FROM soc.FileTraitement f
                    WHERE f.Traitement = t.Traitement AND f.Etat = 'en attente' AND f.CodeGreffe IS NOT NULL) g)
            FROM soc.EtatTraitement t ORDER BY t.Serveur, t.Traitement;
            """, cn))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                var (serveur, traitement, etat, enAttente) = (r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3));
                var greffe = r.IsDBNull(4) ? null : r.GetString(4);
                var nomme = $"{traitement} ({serveur})";
                if (etat == EtatsTraitement.EnErreur)
                    causes.Add(new($"{ReglesAlerte.TraitementEnErreur}|{serveur}|{traitement}", ReglesAlerte.TraitementEnErreur, greffe, nomme,
                        $"Le traitement {traitement} est arrêté en erreur" + (greffe is null ? "" : $" sur un élément du greffe {greffe}") +
                        $" : {(r.IsDBNull(5) ? "erreur sans message" : r.GetString(5))}"));
                if (enAttente > 0 && !r.IsDBNull(6) && r.GetDateTime(6) <= maintenant - reglages.DelaiFileBloquee)
                {
                    var greffes = r.IsDBNull(7) ? null : r.GetString(7);
                    causes.Add(new($"{ReglesAlerte.FileBloquee}|{serveur}|{traitement}", ReglesAlerte.FileBloquee,
                        greffes is not null && !greffes.Contains(',') ? greffes : null, nomme,
                        $"La file du traitement {traitement} ne baisse plus : {enAttente} élément(s) en attente depuis le " +
                        $"{r.GetDateTime(6).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)} (UTC), délai réglé " +
                        $"{reglages.DelaiFileBloquee.TotalSeconds.ToString(CultureInfo.InvariantCulture)} s" +
                        (greffes is null ? "." : $" ; greffe(s) en file : {greffes}.")));
                }
            }

        var ouvertes = new Dictionary<string, int>(StringComparer.Ordinal);
        await using (var cmd = new SqlCommand("SELECT Cle, Id FROM soc.AlerteExploitation WHERE FermeeLe IS NULL;", cn))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) ouvertes[r.GetString(0)] = r.GetInt32(1);

        int nouvelles = 0, fermees = 0;
        foreach (var c in causes)
        {
            await using var cmd = new SqlCommand(ouvertes.ContainsKey(c.Cle)
                ? "UPDATE soc.AlerteExploitation SET Erreur = @erreur WHERE Cle = @cle AND FermeeLe IS NULL;"
                : """
                  INSERT INTO soc.AlerteExploitation (Cle, Regle, CodeGreffe, Traitement, Erreur, OuverteLe)
                  VALUES (@cle, @regle, @greffe, @traitement, @erreur, @le);
                  """, cn);
            cmd.Parameters.Add(new SqlParameter("@cle", SqlDbType.VarChar, 240) { Value = Couper(c.Cle, 240) });
            cmd.Parameters.Add(new SqlParameter("@regle", SqlDbType.VarChar, 30) { Value = c.Regle });
            cmd.Parameters.Add(new SqlParameter("@greffe", SqlDbType.VarChar, 20) { Value = (object?)c.Greffe ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@traitement", SqlDbType.VarChar, 100) { Value = Couper(c.Traitement, 100) });
            cmd.Parameters.Add(new SqlParameter("@erreur", SqlDbType.NVarChar, 1000) { Value = Couper(c.Erreur, 1000) });
            cmd.Parameters.Add(new SqlParameter("@le", SqlDbType.DateTime2) { Value = maintenant });
            try
            {
                await cmd.ExecuteNonQueryAsync(ct);
                if (!ouvertes.ContainsKey(c.Cle)) nouvelles++;
            }
            catch (SqlException e) when (e.Number is 2601 or 2627)
            {
                // Un autre serveur vient d'ouvrir la meme alerte (index unique des alertes ouvertes) : elle existe, une seule fois.
            }
        }
        var actuelles = causes.Select(c => Couper(c.Cle, 240)).ToHashSet(StringComparer.Ordinal);
        foreach (var (cle, id) in ouvertes)
        {
            if (actuelles.Contains(cle)) continue;
            await using var cmd = new SqlCommand("UPDATE soc.AlerteExploitation SET FermeeLe = @le WHERE Id = @id AND FermeeLe IS NULL;", cn);
            cmd.Parameters.Add(new SqlParameter("@le", SqlDbType.DateTime2) { Value = maintenant });
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
            fermees += await cmd.ExecuteNonQueryAsync(ct);
        }
        return (nouvelles, fermees);
    }

    // --- 4. L'envoi ---------------------------------------------------------------------------------------

    private async Task<int> EnvoyerAsync(CancellationToken ct)
    {
        var aEnvoyer = (await LireAlertesAsync("WHERE EnvoyeeLe IS NULL AND FermeeLe IS NULL", ct)).ToList();
        var envoyees = 0;
        foreach (var alerte in aEnvoyer)
        {
            string? rate = null;
            try { await canal.EnvoyerAsync(alerte, ct); envoyees++; }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                rate = e.Message;
                log.LogError(e, "Alerte {Id} non envoyee par le canal {Canal} ; elle repart au passage suivant : {Cause}", alerte.Id, canal.Nom, e.Message);
            }
            await using var cn = await commune.OuvrirAsync(ct);
            await using var cmd = new SqlCommand(rate is null
                ? "UPDATE soc.AlerteExploitation SET EnvoyeeLe = SYSUTCDATETIME(), Canal = @canal, EnvoiRate = NULL WHERE Id = @id;"
                : "UPDATE soc.AlerteExploitation SET EnvoiRate = @rate WHERE Id = @id;", cn);
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = alerte.Id });
            cmd.Parameters.Add(new SqlParameter("@canal", SqlDbType.VarChar, 40) { Value = Couper(canal.Nom, 40) });
            cmd.Parameters.Add(new SqlParameter("@rate", SqlDbType.NVarChar, 500) { Value = (object?)Couper(rate, 500) ?? DBNull.Value });
            await cmd.ExecuteNonQueryAsync(ct);
        }
        return envoyees;
    }

    // --- 5. La purge --------------------------------------------------------------------------------------

    /// <summary>
    /// Retire ce qui a depasse sa duree ecrite dans soc.ConservationTechnique, par lots de 5 000 lignes (la V2 en est
    /// a 11 millions de lignes d'etats : un seul DELETE tiendrait la table). Un niveau sans duree ecrite n'est pas purge.
    /// </summary>
    public async Task<BilanPurge> PurgerAsync(DateTime maintenant, CancellationToken ct = default)
    {
        const string Boucle = """
            DECLARE @n int = 1, @total int = 0;
            WHILE @n > 0
            BEGIN
                {0}
                SET @n = @@ROWCOUNT;
                SET @total += @n;
            END;
            SELECT @total;
            """;
        async Task<int> Purger(string suppression)
        {
            await using var cn = await commune.OuvrirAsync(ct);
            await using var cmd = new SqlCommand(string.Format(CultureInfo.InvariantCulture, Boucle, suppression), cn) { CommandTimeout = 600 };
            cmd.Parameters.Add(new SqlParameter("@m", SqlDbType.DateTime2) { Value = maintenant });
            return (int)(await cmd.ExecuteScalarAsync(ct))!;
        }
        static string Duree(string journal) => $"(SELECT c.Jours FROM soc.ConservationTechnique c WHERE c.Journal = '{journal}' AND c.Niveau = '*')";
        var bilan = new BilanPurge(
            Erreurs: await Purger("""
                DELETE TOP (5000) e FROM soc.ErreurTechnique e
                JOIN soc.ConservationTechnique c ON c.Journal = 'erreurs' AND c.Niveau = e.Niveau
                WHERE e.Instant < DATEADD(day, -c.Jours, @m);
                """),
            Etats: await Purger($"DELETE TOP (5000) FROM soc.EtatTraitementHistorique WHERE PublieLe < DATEADD(day, -{Duree("etats")}, @m);"),
            Alertes: await Purger($"DELETE TOP (5000) FROM soc.AlerteExploitation WHERE FermeeLe < DATEADD(day, -{Duree("alertes")}, @m);"),
            File: await Purger($"DELETE TOP (5000) FROM soc.FileTraitement WHERE Etat = 'fait' AND TraiteLe < DATEADD(day, -{Duree("file")}, @m);"));
        if (bilan != new BilanPurge(0, 0, 0, 0))
            log.LogInformation("Purge de la supervision : {Erreurs} erreur(s), {Etats} etat(s), {Alertes} alerte(s), {File} element(s) de file",
                bilan.Erreurs, bilan.Etats, bilan.Alertes, bilan.File);
        return bilan;
    }

    // --- La lecture de l'ecran --------------------------------------------------------------------------

    /// <summary>
    /// Ce que voit l'exploitant (CA03) : les greffes du catalogue avec leur dernier etat, les traitements publies par
    /// chaque serveur, les alertes ouvertes puis les 50 dernieres fermees. Tout est lu dans la base COMMUNE : aucune
    /// base de greffe n'est ouverte.
    /// </summary>
    public async Task<EtatSupervision> LireAsync(CancellationToken ct = default)
    {
        await using var cn = await commune.OuvrirAsync(ct);
        var greffes = new List<EtatGreffe>();
        await using (var cmd = new SqlCommand("""
            SELECT g.CodeGreffe, g.Nom, g.EstOutreMer, g.Serveur, e.Sante, e.Detail, e.MigrationsJouees, e.DerniereMigration,
                   e.Echecs, e.PremierEchec, e.DernierEchec, e.EchecsDetail, e.VerifieLe
            FROM soc.Greffe g LEFT JOIN soc.EtatGreffe e ON e.CodeGreffe = g.CodeGreffe
            ORDER BY g.CodeGreffe;
            """, cn))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                greffes.Add(new EtatGreffe(r.GetString(0), r.GetString(1), r.GetBoolean(2), r.GetString(3),
                    r.IsDBNull(4) ? SantesGreffe.NonVerifiee : r.GetString(4), Texte(r, 5), Entier(r, 6), Texte(r, 7),
                    Entier(r, 8), Instant(r, 9), Instant(r, 10), Texte(r, 11), Instant(r, 12)));

        var traitements = new List<EtatTraitement>();
        await using (var cmd = new SqlCommand("""
            SELECT Serveur, Traitement, Etat, EnCours, EnAttente, EnErreur, CodeGreffe, Message, PublieLe, FileBaisseLe
            FROM soc.EtatTraitement ORDER BY Serveur, CASE WHEN Traitement = @serveur THEN 0 ELSE 1 END, Traitement;
            """, cn))
        {
            cmd.Parameters.Add(new SqlParameter("@serveur", SqlDbType.VarChar, 100) { Value = TraitementServeur });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                traitements.Add(new EtatTraitement(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4),
                    r.GetInt32(5), Texte(r, 6), Texte(r, 7), Instant(r, 8)!.Value, Instant(r, 9)));
        }

        var alertes = new List<AlerteExploitation>(await LireAlertesAsync("WHERE FermeeLe IS NULL", ct));
        alertes.AddRange(await LireAlertesAsync("WHERE FermeeLe IS NOT NULL", ct, 50));
        var dernier = greffes.Max(g => g.VerifieLe);
        return new EtatSupervision(dernier, [.. greffes], [.. traitements], [.. alertes]);
    }

    private async Task<IReadOnlyList<AlerteExploitation>> LireAlertesAsync(string condition, CancellationToken ct, int auPlus = 1000)
    {
        await using var cn = await commune.OuvrirAsync(ct);
        await using var cmd = new SqlCommand($"""
            SELECT TOP ({auPlus}) Id, Regle, CodeGreffe, Traitement, Erreur, OuverteLe, EnvoyeeLe, Canal, FermeeLe
            FROM soc.AlerteExploitation {condition} ORDER BY OuverteLe DESC, Id DESC;
            """, cn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var liste = new List<AlerteExploitation>();
        while (await r.ReadAsync(ct))
            liste.Add(new AlerteExploitation(r.GetInt32(0), r.GetString(1), Texte(r, 2), r.GetString(3), r.GetString(4),
                Instant(r, 5)!.Value, Instant(r, 6), Texte(r, 7), Instant(r, 8)));
        return liste;
    }

    private static string? Texte(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static int? Entier(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);

    private static DateTime? Instant(SqlDataReader r, int i) => r.IsDBNull(i) ? null : DateTime.SpecifyKind(r.GetDateTime(i), DateTimeKind.Utc);

    private static string Date(SqlDataReader r, int i)
        => r.IsDBNull(i) ? "?" : r.GetDateTime(i).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);

    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(s))]
    private static string? Couper(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}

/// <summary>
/// Les echecs de numerotation de l'ancien RIG (NUM-007 CA05), lus par Base\v2\echecs.sql -- le script que l'exploitation
/// lance a la main avec sqlcmd, embarque tel quel dans le serveur (RigV3.Serveur.csproj) : le serveur ne lit pas une copie.
/// Sa seule variable sqlcmd, $(SessionEchecs), est remplacee par Supervision:SessionEchecs (lettres, chiffres, _).
/// Une instance par groupe de greffes : la session d'evenements est celle de l'instance (posee par bascule.sql).
/// </summary>
internal static class LectureEchecs
{
    /// <summary>Le nom de la ressource embarquee.</summary>
    internal const string Ressource = "RigV3.Serveur.echecs.sql";

    /// <summary>Les echecs d'un greffe ; <see cref="Nombre"/> null s'ils n'ont pas pu etre lus (la raison dans <see cref="Detail"/>).</summary>
    internal sealed record Echecs(int? Nombre, DateTime? Premier, DateTime? Dernier, string? Detail);

    private static readonly Lazy<string> Script = new(() =>
    {
        using var flux = typeof(LectureEchecs).Assembly.GetManifestResourceStream(Ressource)
            ?? throw new InvalidOperationException($"Ressource {Ressource} absente du serveur (Base\\v2\\echecs.sql, RigV3.Serveur.csproj).");
        using var lecteur = new StreamReader(flux, System.Text.Encoding.UTF8);
        var texte = lecteur.ReadToEnd();
        // Le serveur ne joue qu'un lot, sans commande sqlcmd : si le script change de forme, on le dit au lieu de mal le jouer.
        var lignes = texte.Replace("\r\n", "\n").Split('\n');
        if (lignes.Any(l => l.TrimStart().StartsWith(':') || l.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Base\\v2\\echecs.sql contient une commande sqlcmd ou un GO : la supervision ne sait plus le jouer.");
        return texte;
    });

    /// <summary>La requete prete a jouer pour la session <paramref name="session"/>.</summary>
    internal static string Requete(string session)
    {
        var requete = Script.Value.Replace("$(SessionEchecs)", session, StringComparison.Ordinal);
        if (requete.Contains("$(", StringComparison.Ordinal))
            throw new InvalidOperationException("Base\\v2\\echecs.sql attend une variable sqlcmd que la supervision ne connait pas.");
        return requete;
    }

    internal static async Task<Dictionary<string, Echecs>> LireAsync(CatalogueGreffes catalogue, IReadOnlyList<FicheGreffe> fiches,
                                                                     string session, CancellationToken ct)
    {
        var resultat = new Dictionary<string, Echecs>(StringComparer.OrdinalIgnoreCase);
        await Task.WhenAll(fiches.GroupBy(f => f.Serveur, StringComparer.OrdinalIgnoreCase).Select(async instance =>
        {
            var parBase = new Dictionary<string, Echecs>(StringComparer.OrdinalIgnoreCase);
            string? panne = null;
            try
            {
                await using var cn = new SqlConnection(catalogue.ConnexionBase(instance.Key, "master"));
                await cn.OpenAsync(ct);
                await using (var cmd = new SqlCommand("SELECT COUNT(*) FROM sys.dm_xe_sessions WHERE name = @s;", cn))
                {
                    cmd.Parameters.Add(new SqlParameter("@s", SqlDbType.NVarChar, 128) { Value = session });
                    if ((int)(await cmd.ExecuteScalarAsync(ct))! == 0)
                        panne = $"Échecs non relevés : la session {session} n'existe pas sur {instance.Key} (Base\\v2\\bascule.sql n'y est pas joué).";
                }
                if (panne is null)
                {
                    await using var cmd = new SqlCommand(Requete(session), cn) { CommandTimeout = 60 };
                    await using var r = await cmd.ExecuteReaderAsync(ct);
                    while (await r.ReadAsync(ct))
                        if (!r.IsDBNull(0))
                            parBase[r.GetString(0)] = new Echecs(r.GetInt32(1),
                                r.IsDBNull(2) ? null : DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc),
                                r.IsDBNull(3) ? null : DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc),
                                r.IsDBNull(4) ? null : r.GetString(4));
                }
            }
            catch (SqlException e)
            {
                panne = $"Échecs non relevés sur {instance.Key} : {e.Message}";
            }
            lock (resultat)
                foreach (var f in instance)
                    resultat[f.Code] = panne is not null ? new Echecs(null, null, null, panne)
                                     : parBase.GetValueOrDefault(f.BaseV2) ?? new Echecs(0, null, null, null);
        }));
        return resultat;
    }
}
