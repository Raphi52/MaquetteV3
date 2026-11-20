// fix-ok: CS0246 HttpRequestException -- un projet WPF n'importe pas System.Net.Http d'office.
using System.Net.Http;
using RigV3.Contrats.Socle;

namespace RigV3Desktop.Pont;

// ============================================================
// L'AGENT DE CETTE SESSION (SOC-001, tache T03 cote ecran), 2026-11-20.
//
// A l'ouverture de la fenetre principale (MainWindow.Loaded), AVANT toute donnee :
//   - sans RIGV3_SERVEUR : rien ne change, l'accueil reste sur ses comptes de
//     demonstration (Metier/JeuDemo.cs) et son selecteur de login ;
//   - branchee au serveur : on lui demande qui est l'agent. Sa session Windows
//     part avec la demande (ClientServeur, credentials de la session) : aucune
//     saisie. Admis -> l'entete de l'accueil le salue par son nom ; refuse (ou
//     serveur injoignable) -> l'ecran de refus (Pont/EcranRefus.cs) remplace
//     l'application entiere : aucune donnee n'est montree (CA03).
//
// Ce fichier ne depend que de ClientServeur et de Contrats/ : le banc
// tests/SocleIdentiteCheck le compile seul, contre le vrai serveur.
// ============================================================

public static class SessionAgent
{
    /// <summary>Statut pose par l'ecran quand le serveur n'a pas pu repondre (il n'est pas
    /// un refus du serveur : <see cref="StatutsIdentite"/> n'en a pas).</summary>
    public const string StatutServeurInjoignable = "serveur-injoignable";

    /// <summary>L'agent de cette session, tel que le serveur l'a juge ; null = demonstration
    /// (aucun serveur), ou pas encore ouverte.</summary>
    public static IdentiteAgent? Identite { get; private set; }

    /// <summary>Le nom que l'entete de l'accueil affiche (« Prenom Nom ») ; null hors serveur.</summary>
    public static string? NomAgent => Identite is { Admis: true, Agent: { } agent } ? agent.NomAffiche : null;

    /// <summary>
    /// Demande au serveur qui est l'agent. <paramref name="serveur"/> rend le client de
    /// la session (<see cref="ClientServeur.Actif"/>) ; null = demonstration, rien n'est demande.
    /// Une adresse illisible ou un serveur qui ne repond pas n'est PAS un retour discret a la
    /// demonstration : c'est un refus d'ouvrir, qui dit la cause.
    /// </summary>
    public static async Task<IdentiteAgent?> OuvrirAsync(Func<ClientServeur?> serveur, CancellationToken ct = default)
    {
        ClientServeur? client = null;
        try
        {
            client = serveur();
            Identite = client is null ? null : await client.LireAsync<IdentiteAgent>(RoutesIdentite.Identite, ct);
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or InvalidOperationException
                                       or TaskCanceledException)
        {
            var ou = client is null ? $"variable {ClientServeur.Variable}" : client.Adresse.AbsoluteUri;
            Identite = new IdentiteAgent(StatutServeurInjoignable, Environment.UserName, null,
                e is ErreurServeur { CodeHttp: 401 }
                    ? $"Le serveur de RIG ({ou}) n'a pas reconnu la session Windows '{Environment.UserName}'."
                    : $"RIG n'a pas pu joindre son serveur ({ou}) : {e.Message}",
                "Merci de contacter la hotline d'Amitel pour signaler le problème.");
        }
        return Identite;
    }
}
