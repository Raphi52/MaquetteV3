using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.Courriels;

/// <summary>Ce qu'un passage du menage a fait dans un greffe : traces effacees, ou la cause d'un echec.</summary>
public sealed record MenageGreffe(string Greffe, int Effacees, string? Erreur);

/// <summary>
/// LE MENAGE QUOTIDIEN DES TRACES D'ENVOI (SOC-006 T03, CA04). Chaque jour a <see cref="OptionsCourriels.HeureMenage"/>
/// (heure du serveur), il efface dans la base V3 de chaque greffe du catalogue les traces plus anciennes que
/// <see cref="OptionsCourriels.ConservationJours"/> (90 jours, comme la V2 : EmailEnvoiTrace.RETENTION_JOURS_DEFAUT).
/// La V2 purgeait au lancement de RIG_Send_Email, greffe par greffe ; le serveur de la V3 n'a pas de lancement par
/// envoi, il passe une fois par jour sur tous les greffes. Un greffe dont la base ne repond pas est note au journal et
/// n'arrete pas les autres. La duree reste un reglage : elle est a valider par le delegue a la protection des donnees
/// d'Amitel (la table garde des adresses et des objets de courriers judiciaires).
/// </summary>
public sealed class MenageCourriels(OptionsCourriels options, CatalogueGreffes catalogue, ILogger<MenageCourriels> journal) : BackgroundService
{
    /// <summary>L'evenement de journal d'un greffe que le menage n'a pas pu nettoyer.</summary>
    public static readonly EventId EvenementMenageImpossible = new(51063, "MenageCourrielsImpossible");

    /// <summary>Un passage du menage sur tous les greffes du catalogue ; rend ce qu'il a fait dans chacun.</summary>
    public async Task<IReadOnlyList<MenageGreffe>> PasserAsync(CancellationToken ct = default)
    {
        var bilan = new List<MenageGreffe>();
        foreach (var fiche in await catalogue.ListerAsync(ct))
        {
            try
            {
                await using var cn = await catalogue.OuvrirBaseV3Async(fiche.Code, ct);
                var effacees = await TracesCourriels.PurgerAsync(cn, options.ConservationJours, ct);
                bilan.Add(new MenageGreffe(fiche.Code, effacees, null));
                if (effacees > 0)
                    journal.LogInformation("Menage des courriels du greffe {Greffe} : {Nombre} trace(s) de plus de {Jours} jours effacee(s).",
                        fiche.Code, effacees, options.ConservationJours);
            }
            catch (Exception e) when (e is BaseGreffeInjoignable or Microsoft.Data.SqlClient.SqlException)
            {
                bilan.Add(new MenageGreffe(fiche.Code, 0, e.Message));
                journal.LogError(EvenementMenageImpossible, e, "Menage des courriels du greffe {Greffe} impossible : {Cause}", fiche.Code, e.Message);
            }
        }
        return bilan;
    }

    /// <summary>Le delai jusqu'au prochain passage, a l'heure du menage (aujourd'hui si elle n'est pas passee, sinon demain).</summary>
    public static TimeSpan AttenteJusquA(TimeSpan heure, DateTime maintenant)
    {
        var prochain = maintenant.Date + heure;
        if (prochain <= maintenant) prochain = prochain.AddDays(1);
        return prochain - maintenant;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(AttenteJusquA(options.HeureMenage, DateTime.Now), stoppingToken); }
            catch (OperationCanceledException) { return; }
            try { await PasserAsync(stoppingToken); }
            catch (OperationCanceledException) { return; }
            catch (Exception e) when (e is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
            {
                // La base commune (le catalogue) ne repond pas : le passage de demain reessaiera ; la cause est au journal.
                journal.LogError(EvenementMenageImpossible, e, "Menage des courriels impossible (catalogue des greffes) : {Cause}", e.Message);
            }
        }
    }
}
