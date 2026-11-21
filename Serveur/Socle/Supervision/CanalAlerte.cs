using System.Collections.Concurrent;
using System.Text.Json;
using RigV3.Contrats.Socle;

namespace RigV3.Serveur.Socle.Supervision;

/// <summary>
/// Le canal par lequel une alerte atteint l'exploitation d'Amitel (SOC-011 CA02). La V2 n'en a pas : on lit ses
/// tables d'etat a la main. Le canal reel (courriel a l'exploitation, ou l'outil d'astreinte d'Amitel) reste a
/// choisir par l'exploitation (docs\reconstruction\socle-supervision.md) ; il s'inscrira par son module
/// (services.AddSingleton&lt;ICanalAlerte, …&gt;()), a la place de <see cref="CanalAlerteSimule"/>.
/// Une exception = l'alerte n'est pas partie : elle reste « a envoyer » et repart au passage suivant.
/// </summary>
public interface ICanalAlerte
{
    /// <summary>Le nom du canal, garde avec l'alerte envoyee.</summary>
    string Nom { get; }

    Task EnvoyerAsync(AlerteExploitation alerte, CancellationToken ct);
}

/// <summary>
/// Le canal SIMULE du developpement : il garde les alertes en memoire (le banc les lit), les ecrit, si
/// Supervision:DossierAlertes est regle, une ligne JSON par alerte dans alertes-AAAAMMJJ.jsonl, et les dit au journal
/// du processus. Hors developpement et recette, chaque alerte est en plus journalisee en ERREUR : un serveur de
/// production sans canal reel ne se tait pas.
/// </summary>
public sealed class CanalAlerteSimule(ReglagesSupervision reglages, IHostEnvironment environnement, ILogger<CanalAlerteSimule> log) : ICanalAlerte
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentQueue<AlerteExploitation> recues = new();
    private readonly SemaphoreSlim ecriture = new(1, 1);

    public string Nom => "simule";

    /// <summary>Les alertes recues depuis le demarrage, dans leur ordre.</summary>
    public IReadOnlyList<AlerteExploitation> Recues => [.. recues];

    public async Task EnvoyerAsync(AlerteExploitation alerte, CancellationToken ct)
    {
        recues.Enqueue(alerte);
        if (reglages.DossierAlertes is { } dossier)
        {
            await ecriture.WaitAsync(ct);
            try
            {
                Directory.CreateDirectory(dossier);
                await File.AppendAllTextAsync(Path.Combine(dossier, $"alertes-{alerte.OuverteLe:yyyyMMdd}.jsonl"),
                    JsonSerializer.Serialize(alerte, Json) + Environment.NewLine, ct);
            }
            finally { ecriture.Release(); }
        }
        if (environnement.IsDevelopment() || environnement.IsEnvironment("Recette"))
            log.LogInformation("ALERTE (canal simule) {Regle} greffe {Greffe}, {Traitement} : {Erreur}",
                alerte.Regle, alerte.CodeGreffe, alerte.Traitement, alerte.Erreur);
        else
            log.LogError("ALERTE non transmise (aucun canal reel inscrit, canal simule en {Environnement}) {Regle} greffe {Greffe}, {Traitement} : {Erreur}",
                environnement.EnvironmentName, alerte.Regle, alerte.CodeGreffe, alerte.Traitement, alerte.Erreur);
    }
}
