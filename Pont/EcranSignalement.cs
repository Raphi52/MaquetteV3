using System.Net.Http;
using System.Reflection;
using RigV3.Contrats.Socle;

namespace RigV3Desktop.Pont;

/// <summary>Ce que le signalement emporte sans saisie (SOC-022 CA06), montre a l'agent avant qu'il ecrive sa phrase.</summary>
/// <param name="Mode">« serveur » (le defaut part a Amitel) ou « demo » (sans RIGV3_SERVEUR : rien ne part, et l'ecran le dit).</param>
/// <param name="Ecran">L'ecran ouvert, tel que l'agent le voit.</param>
/// <param name="CodeEcran">Son code dans la V3 ; null pour l'accueil.</param>
/// <param name="Greffe">Le greffe ouvert par la session (« Le Puy (4302) ») ; null s'il n'y en a pas.</param>
/// <param name="Agent">L'agent de la session (son nom, sinon son compte Windows).</param>
/// <param name="Version">La version de la V3 (Directory.Build.props).</param>
/// <param name="Heure">L'heure du poste, lisible (« 21/11/2026 14:32 »).</param>
/// <param name="DerniereErreur">L'identifiant de suivi de la derniere erreur montree dans cette session, s'il y en a une.</param>
public sealed record ContexteSignalement(string Mode, string Ecran, string? CodeEcran, string? CodeGreffe, string? Greffe, string Agent,
                                         string Version, string Heure, string? DerniereErreur);

/// <summary>Le geste de l'agent : sa phrase, et l'ecran d'ou il signale (le reste, le pont le prend lui-meme).</summary>
public sealed record DemandeSignalement(string Phrase, string Ecran, string? CodeEcran);

/// <summary>Ce que l'agent voit apres l'envoi.</summary>
/// <param name="Envoye">Le defaut est enregistre chez Amitel.</param>
/// <param name="Suivi">Son identifiant de suivi, a garder ; null si rien n'est parti.</param>
/// <param name="Message">La phrase a montrer (l'identifiant et a quoi il sert, ou pourquoi rien n'est parti).</param>
public sealed record ResultatSignalement(bool Envoye, string? Suivi, string Message, ContexteSignalement Contexte);

/// <summary>
/// Pont du signalement d'un defaut (web/src/composants/SignalementDefaut.tsx), SOC-022 CA06 et T03 : NEUF dans la V3,
/// « a maquetter » dans le carnet (la V2 n'a pas de signalement : un agent appelle l'assistance). Un agent d'un greffe
/// beta signale un defaut en UN geste -- le bouton « Signaler un défaut » de l'en-tete de l'accueil, ou Ctrl Maj D de
/// partout (fiche « Ancienne touche, nouvelle touche », SOC-027). Le pont prend sans saisie l'ecran ouvert, le greffe,
/// l'agent, la version, l'heure du poste et la derniere erreur suivie ; l'agent ajoute une phrase. Le serveur y ajoute
/// le compte de la session et l'identifiant de suivi (le mecanisme des erreurs du lot 2 : RoutesSupervision.Defauts,
/// SuiviErreur, X-Rig-Suivi) ; Amitel le lit dans l'ecran Supervision et « RigV3.Passage beta defauts » le classe.
/// Sans RIGV3_SERVEUR, rien ne part : le pont le dit, il n'invente pas d'identifiant.
/// </summary>
public static class EcranSignalement
{
    /// <summary>Le raccourci clavier du signalement, tel que la fiche des touches l'ecrit (web/src/composants/SignalementDefaut.tsx l'ecoute).</summary>
    public const string Raccourci = EcranCorrespondances.ToucheSignalement;

    /// <summary>La version de cet ecran de la V3 : celle de Directory.Build.props, sans le suffixe de compilation.</summary>
    public static readonly string Version = VersionV3.Normaliser(
        typeof(EcranSignalement).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(EcranSignalement).Assembly.GetName().Version?.ToString() ?? "0.0.0");

    [JSInvokable("EcranSignalement.Preparer")]
    public static ContexteSignalement Preparer(string ecran, string? code) => Contexte(ServeurOuNull(), ecran, code, DateTimeOffset.Now);

    [JSInvokable("EcranSignalement.Envoyer")]
    public static Task<ResultatSignalement> Envoyer(DemandeSignalement demande) => EnvoyerAsync(ServeurOuNull(), demande, DateTimeOffset.Now);

    /// <summary>Ce que le signalement emporte, pris dans la session de l'agent (rien n'est saisi).</summary>
    public static ContexteSignalement Contexte(ClientServeur? serveur, string ecran, string? code, DateTimeOffset heure)
        => new(serveur is null ? "demo" : "serveur",
               string.IsNullOrWhiteSpace(ecran) ? "Accueil" : ecran.Trim(), string.IsNullOrWhiteSpace(code) ? null : code.Trim(),
               SessionAgent.CodeGreffe, SessionAgent.Greffe?.Libelle,
               SessionAgent.NomAgent ?? SessionAgent.Identite?.Compte ?? Environment.UserName,
               Version, heure.ToString("dd/MM/yyyy HH:mm"), EcranSupervision.DerniereErreur?.Suivi);

    /// <summary>
    /// Envoie le defaut a <paramref name="serveur"/> ; rend l'identifiant de suivi que le serveur a donne. Une phrase vide ou
    /// trop longue ne part pas (la meme regle que le serveur, qui la revoit). Sans serveur, ou s'il ne repond pas, rien ne
    /// part et le message le dit : l'agent garde sa phrase et peut reessayer ou appeler l'assistance.
    /// </summary>
    public static async Task<ResultatSignalement> EnvoyerAsync(ClientServeur? serveur, DemandeSignalement demande, DateTimeOffset heure)
    {
        var contexte = Contexte(serveur, demande.Ecran, demande.CodeEcran, heure);
        var phrase = (demande.Phrase ?? string.Empty).Trim();
        if (phrase.Length == 0)
            return new(false, null, "Décrivez le défaut en une phrase : c'est la seule chose à écrire, le reste part avec.", contexte);
        if (phrase.Length > RoutesSupervision.PhraseAuPlus)
            return new(false, null, $"La phrase dépasse {RoutesSupervision.PhraseAuPlus} signes : gardez l'essentiel.", contexte);
        if (serveur is null)
            return new(false, null, "Démonstration : sans serveur de RIG, le défaut ne part pas chez Amitel (aucun identifiant de suivi).", contexte);
        try
        {
            var suivi = await serveur.EnvoyerAsync<SignalementDefaut, SuiviErreur>(RoutesSupervision.Defauts,
                new SignalementDefaut(phrase, contexte.Ecran, contexte.CodeEcran, contexte.CodeGreffe, contexte.Version, heure,
                                      contexte.DerniereErreur, Environment.MachineName));
            return new(true, suivi.Suivi, suivi.Message, contexte);
        }
        catch (ErreurServeur e) when (e.CodeHttp == 400)
        {
            return new(false, null, Motif(e) ?? "Le serveur de RIG a refusé le signalement.", contexte);
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            var code = (e as ErreurServeur)?.CodeHttp.ToString() ?? e.GetType().Name;
            return new(false, null, $"Le serveur de RIG ne répond pas ({serveur.Adresse}, {code}) : le défaut n'est pas parti. Réessayez, ou appelez l'assistance d'Amitel.", contexte);
        }
    }

    /// <summary>Une adresse RIGV3_SERVEUR illisible : le signalement le dit comme une demonstration, sans lever une seconde erreur.</summary>
    private static ClientServeur? ServeurOuNull()
    {
        try { return ClientServeur.Actif; }
        catch (InvalidOperationException) { return null; }
    }

    private static string? Motif(ErreurServeur e)
    {
        if (e.Corps is null) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<RefusGreffe>(e.Corps, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))?.Motif; }
        catch (System.Text.Json.JsonException) { return null; }
    }
}
