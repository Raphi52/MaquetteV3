// fix-ok: fichier NEUF du lot 14, edite pour une seule cause mesuree : SocleSupervisionCheck CA03 (recherche statique des catch sans
// journal) rougissait sur le catch du passage, qui deleguait l'ecriture a une methode ; l'ecriture est maintenant DANS le catch.
using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.Supervision;

// ============================================================
// LE PLANIFICATEUR DES TRAITEMENTS DE NUIT (SOC-018 CA05 T02, lot 14 de la phase 2).
//
// La V2 planifie avec le planificateur de taches Windows (Job_Exec_Hash_Totaux_FactEnc : tous les jours 22h, un .bat par
// greffe) : ni rattrapage si le serveur etait eteint, ni lien avec la supervision. La V3 : UN planificateur pour toute
// la partie serveur. Un travail de nuit est deux classes qu'un module inscrit :
//   - une ITachePlanifiee : « pour CE greffe, a CET instant, qu'y a-t-il a faire ? » (une charge par chose, dans
//     l'ordre ou il faut les faire) ; elle ne fait rien d'elle-meme ;
//   - l'ITraitementDeFond du meme nom (Traitement), qui recoit chaque charge dans la file de la supervision (SOC-011)
//     et la fait.
// Le planificateur depose, greffe par greffe, ce qui est du et n'est pas deja en file ni tente depuis moins de
// <see cref="ReglagesPlanificateur.Pause"/> ; un greffe qui ne repond pas n'arrete pas les autres, son echec va au
// journal technique avec son code (SOC-017), au plus une fois par pause. Il ne sait rien des chaines signees : la
// comparaison nocturne (REC-006, lot 8) s'inscrit de la meme facon, en une ITachePlanifiee.
// ============================================================

/// <summary>Ce qu'un travail de nuit a a faire dans un greffe.</summary>
public interface ITachePlanifiee
{
    /// <summary>Le nom de l'<see cref="ITraitementDeFond"/> qui fait le travail (celui de sa file).</summary>
    string Traitement { get; }

    /// <summary>
    /// Les charges a deposer pour ce greffe a cet instant, dans l'ordre ou le traitement doit les faire ; vide : rien
    /// n'est du. Elle peut ouvrir la base du greffe ; une exception est un echec du greffe (journal technique), pas du passage.
    /// </summary>
    Task<IReadOnlyList<string>> ChargesDuesAsync(FicheGreffe greffe, DateTime maintenantUtc, CancellationToken ct);
}

/// <summary>Ce qu'un passage du planificateur a fait pour un greffe et un travail.</summary>
public sealed record DepotPlanifie(string Traitement, string Greffe, int Deposes, string? Erreur);

/// <summary>Les reglages du planificateur (section « Planificateur » de la configuration du serveur).</summary>
public sealed record ReglagesPlanificateur(TimeSpan Intervalle, TimeSpan Pause)
{
    /// <summary>Intervalle des passages : 60 s ; 0 arrete le planificateur (un banc le joue lui-meme).</summary>
    public const string CleIntervalle = "Planificateur:IntervalleSecondes";

    /// <summary>Attente avant de redeposer un travail deja tente, ou de redire l'echec d'un greffe : 30 min.</summary>
    public const string ClePause = "Planificateur:PauseMinutes";

    public static ReglagesPlanificateur Depuis(IConfiguration configuration)
    {
        static TimeSpan Lire(IConfiguration c, string cle, double defaut, Func<double, TimeSpan> unite)
        {
            var brut = c[cle];
            if (string.IsNullOrWhiteSpace(brut)) return unite(defaut);
            if (!double.TryParse(brut, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) || n < 0)
                throw new InvalidOperationException($"{cle} = « {brut} » : un nombre positif ou nul est attendu.");
            return unite(n);
        }
        return new(Lire(configuration, CleIntervalle, 60, TimeSpan.FromSeconds), Lire(configuration, ClePause, 30, TimeSpan.FromMinutes));
    }
}

/// <summary>Le planificateur : il depose dans la file des traitements ce que les taches inscrites disent du.</summary>
public sealed class PlanificateurNocturne(IEnumerable<ITachePlanifiee> taches, CatalogueGreffes catalogue, MoteurTraitements moteur,
                                          BaseRig commune, JournalErreurs journal, ReglagesSupervision supervision,
                                          ReglagesPlanificateur reglages, ILogger<PlanificateurNocturne> log) : BackgroundService
{
    private readonly ITachePlanifiee[] taches = [.. taches];
    private readonly ConcurrentDictionary<(string Traitement, string Greffe), DateTime> dernierEchec = new();

    /// <summary>Un passage a l'instant <paramref name="maintenantUtc"/> : pour chaque greffe du catalogue et chaque tache, depose ce qui est du.</summary>
    public async Task<IReadOnlyList<DepotPlanifie>> PasserAsync(DateTime maintenantUtc, CancellationToken ct = default)
    {
        var bilan = new List<DepotPlanifie>();
        if (taches.Length == 0) return bilan;
        foreach (var fiche in await catalogue.ListerAsync(ct))
            foreach (var tache in taches)
            {
                try
                {
                    var deposes = 0;
                    foreach (var charge in await tache.ChargesDuesAsync(fiche, maintenantUtc, ct))
                    {
                        if (await DejaEnFileAsync(tache.Traitement, fiche.Code, charge, ct)) continue;
                        await moteur.DeposerAsync(tache.Traitement, fiche.Code, charge, ct);
                        deposes++;
                    }
                    dernierEchec.TryRemove((tache.Traitement, fiche.Code), out _);
                    bilan.Add(new DepotPlanifie(tache.Traitement, fiche.Code, deposes, null));
                }
                catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    bilan.Add(new DepotPlanifie(tache.Traitement, fiche.Code, 0, e.Message));
                    if (!AEteDit(tache.Traitement, fiche.Code, maintenantUtc)) continue;
                    log.LogWarning(e, "Planificateur : {Traitement} impossible a planifier pour le greffe {Greffe} : {Cause}", tache.Traitement, fiche.Code, e.Message);
                    await journal.EcrireAsync(ErreurAEcrire.Depuis(e, Suivis.Nouveau(), NiveauxErreur.Erreur, OriginesErreur.Traitement, fiche.Code, null,
                        supervision.Site, $"planification {tache.Traitement}"), CancellationToken.None);
                }
            }
        return bilan;
    }

    /// <summary>L'echec d'un greffe va au journal technique (SOC-017) avec son code, une fois par pause : un greffe qui reste muet toute la nuit n'inonde pas le journal.</summary>
    /// <summary>Faut-il dire cet echec ? Faux si le meme a ete dit depuis moins d'une pause ; sinon, le note comme dit (vrai).</summary>
    private bool AEteDit(string traitement, string greffe, DateTime maintenantUtc)
    {
        var cle = (traitement, greffe);
        if (dernierEchec.TryGetValue(cle, out var le) && maintenantUtc - le < reglages.Pause) return false;
        dernierEchec[cle] = maintenantUtc;
        return true;
    }

    /// <summary>La meme charge attend deja, est en cours, ou a ete traitee depuis moins d'une pause (soc.FileTraitement).</summary>
    private async Task<bool> DejaEnFileAsync(string traitement, string greffe, string charge, CancellationToken ct)
    {
        await using var cn = await commune.OuvrirAsync(ct);
        await using var cmd = new SqlCommand("""
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM soc.FileTraitement
                WHERE Traitement = @t AND CodeGreffe = @g AND Charge = @c
                  AND (Etat <> 'fait' OR DeposeLe > DATEADD(SECOND, -@pause, SYSUTCDATETIME()))) THEN 1 ELSE 0 END;
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@t", SqlDbType.VarChar, 100) { Value = traitement });
        cmd.Parameters.Add(new SqlParameter("@g", SqlDbType.VarChar, 20) { Value = greffe });
        cmd.Parameters.Add(new SqlParameter("@c", SqlDbType.NVarChar, -1) { Value = charge });
        cmd.Parameters.Add(new SqlParameter("@pause", SqlDbType.Int) { Value = (int)reglages.Pause.TotalSeconds });
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }

    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        if (!supervision.Automatique || taches.Length == 0 || commune.Connexion is null || reglages.Intervalle <= TimeSpan.Zero) return;
        while (!arret.IsCancellationRequested)
        {
            try { await PasserAsync(DateTime.UtcNow, arret); }
            catch (Exception e) when (e is not OperationCanceledException || !arret.IsCancellationRequested)
            {
                // Le catalogue ou la file est injoignable (base commune) : on le journalise, le passage suivant reessaie.
                log.LogError(e, "Planificateur : passage impossible (base commune) : {Cause}", e.Message);
            }
            try { await Task.Delay(reglages.Intervalle, arret); }
            catch (OperationCanceledException) when (arret.IsCancellationRequested) { return; }
        }
    }
}
