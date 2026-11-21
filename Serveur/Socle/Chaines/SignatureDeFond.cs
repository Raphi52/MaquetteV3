using Microsoft.Data.SqlClient;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.Chaines;

/// <summary>Ce qu'un passage de la signature de fond a fait dans un greffe.</summary>
public sealed record SignatureGreffe(string Greffe, int Signes, string? Erreur);

/// <summary>
/// LA SIGNATURE DE FOND DES EVENEMENTS (SOC-018 T02). Les evenements que les autres programmes ecrivent sans signature
/// (droits 130, reprise 210, livraison 250/255, declencheur des series 10) sont signes a chaque geste des chaines ; ce
/// service les signe aussi sans attendre de geste, toutes les <see cref="Reglage"/> secondes (300 par defaut, 0 l'arrete),
/// dans chaque greffe du catalogue dont les chaines sont ouvertes. Meme role que le job de la V2
/// (CertifExpressoApp48\Jobs\Job_Exec_Hash_JET.bat). Une base muette n'arrete pas les autres : la supervision (SOC-011)
/// la signale deja ; une erreur SQL est notee au journal technique.
/// </summary>
public sealed class SignatureDeFond(ServiceChaines chaines, CatalogueGreffes catalogue, IConfiguration configuration,
                                    ILogger<SignatureDeFond> journal) : BackgroundService
{
    public const string Reglage = "Chaines:SignatureDeFondSecondes";
    public const int ParDefautSecondes = 300;

    /// <summary>L'evenement de journal d'un greffe dont les evenements n'ont pas pu etre signes.</summary>
    public static readonly EventId EvenementSignatureImpossible = new(51080, "SignatureChainesImpossible");

    /// <summary>Un passage sur tous les greffes du catalogue ; rend ce qu'il a fait dans chacun.</summary>
    public async Task<IReadOnlyList<SignatureGreffe>> PasserAsync(CancellationToken ct = default)
    {
        var bilan = new List<SignatureGreffe>();
        foreach (var fiche in await catalogue.ListerAsync(ct))
        {
            try
            {
                await using var cn = await catalogue.OuvrirBaseV3Async(fiche.Code, ct);
                bilan.Add(new SignatureGreffe(fiche.Code, await chaines.SignerEnAttenteAsync(cn, ct), null));
            }
            catch (BaseGreffeInjoignable e)
            {
                bilan.Add(new SignatureGreffe(fiche.Code, 0, e.Message));
                journal.LogInformation("Signature des evenements du greffe {Greffe} remise au passage suivant : {Cause}", fiche.Code, e.Message);
            }
            catch (SqlException e)
            {
                bilan.Add(new SignatureGreffe(fiche.Code, 0, e.Message));
                journal.LogWarning(EvenementSignatureImpossible, e, "Signature des evenements du greffe {Greffe} impossible : {Cause}", fiche.Code, e.Message);
            }
        }
        return bilan;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var secondes = configuration.GetValue(Reglage, ParDefautSecondes);
        if (secondes <= 0) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(secondes), stoppingToken); }
            catch (OperationCanceledException) { return; }
            try { await PasserAsync(stoppingToken); }
            catch (SqlException e)
            {
                // La base commune (le catalogue) ne repond pas : le passage suivant reessaiera.
                journal.LogWarning(EvenementSignatureImpossible, e, "Signature des evenements : catalogue des greffes illisible ({Cause})", e.Message);
            }
        }
    }
}
