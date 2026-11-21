using System.Net.Http;
using RigV3.Contrats.Socle;

namespace RigV3Desktop.Pont;

/// <summary>
/// Ce que l'ecran « Comptes des agents » affiche.
/// </summary>
/// <param name="Mode">« serveur » (base du greffe), « demo » (sans RIGV3_SERVEUR), « refuse » (pas le droit
/// de gerer les droits, SOC-002 CA04) ou « erreur » (serveur injoignable).</param>
/// <param name="Message">Le compte rendu du dernier geste, ou la raison d'un refus ; null sinon.</param>
/// <param name="Echec">Le message est un refus ou une erreur (filet rouge), pas un geste fait.</param>
/// <param name="Selection">Le compte a montrer dans la fiche apres le geste ; null = aucun.</param>
/// <param name="ProposerDesactivation">Une suppression vient d'etre refusee : l'ecran propose de desactiver (SOC-004 CA04).</param>
/// <param name="ToutLeGreffe">Le journal des gestes montre tout le greffe (chef de greffe) ou les seuls gestes de l'agent.</param>
public sealed record VueEcranComptes(
    string Mode,
    string? Message,
    bool Echec,
    string Agent,
    CompteAgent[] Comptes,
    ServiceGreffe[] Services,
    RoleVisible[] Roles,
    LigneJournal[] Journal,
    LigneJournalDroits[] JournalDroits,
    bool ToutLeGreffe,
    int? Selection,
    bool ProposerDesactivation);

/// <summary>
/// Pont de l'ecran « Comptes des agents » (web/src/ecrans/EcranComptes.tsx ; procedure V2 PROC_UTILISATEUR,
/// SOC-002, SOC-004, SOC-005, SOC-016). Chaque geste part au serveur (Serveur/Socle/Comptes) avec le nom de
/// l'ecran pour le journal, puis l'ecran est relu : comptes, roles attribuables, journal des gestes et
/// journal des droits. Les refus du serveur sont des reponses (message), pas des pannes ; un 403 dit que
/// l'agent n'a pas le droit de gerer les droits.
/// Sans RIGV3_SERVEUR, l'ecran montre les comptes de demonstration (Metier/JeuDemo.cs) en lecture seule.
/// Avec lui, les adresses portent le code du greffe (lot 9) : les comptes sont ceux de la base de CE greffe.
/// </summary>
public static class EcranComptes
{
    /// <summary>Le nom de l'ecran tel que le journal des gestes l'ecrit (SOC-016 CA01).</summary>
    public const string NomEcran = "Comptes des agents";

    /// <summary>Le code du greffe de la session (ex. 3801), en attendant que RIG le choisisse a l'ouverture comme
    /// la V2 (lot 10 du plan, RigFormLogin.cs:282-319) : celui-ci remplacera cette variable.</summary>
    public const string VariableGreffe = "RIGV3_GREFFE";
    // fix-ok: les adresses des comptes, droits et journal portent {greffe} depuis le lot 9 (Contrats\Socle\Comptes.cs) ; sans code de greffe, l'ecran appellerait « /socle/{greffe}/comptes » en clair.

    private static string? Greffe
        => Environment.GetEnvironmentVariable(VariableGreffe)?.Trim() is { Length: > 0 } greffe ? greffe : null;

    [JSInvokable("EcranComptes.Ouvrir")]
    public static Task<VueEcranComptes> Ouvrir() => OuvrirAsync(ClientServeur.Actif);

    [JSInvokable("EcranComptes.Creer")]
    public static Task<VueEcranComptes> Creer(DemandeCompte demande)
        => GesteAsync(ClientServeur.Actif, RoutesComptes.Creer, demande with { Id = null, Ecran = NomEcran }, "Compte créé");

    [JSInvokable("EcranComptes.Modifier")]
    public static Task<VueEcranComptes> Modifier(DemandeCompte demande)
        => GesteAsync(ClientServeur.Actif, RoutesComptes.Modifier, demande with { Ecran = NomEcran }, "Compte modifié");

    [JSInvokable("EcranComptes.Desactiver")]
    public static Task<VueEcranComptes> Desactiver(int id)
        => GesteAsync(ClientServeur.Actif, RoutesComptes.Desactiver, new DemandeSurCompte(id, NomEcran), "Compte désactivé");

    [JSInvokable("EcranComptes.Supprimer")]
    public static Task<VueEcranComptes> Supprimer(int id)
        => GesteAsync(ClientServeur.Actif, RoutesComptes.Supprimer, new DemandeSurCompte(id, NomEcran), "Compte supprimé");

    [JSInvokable("EcranComptes.Role")]
    public static Task<VueEcranComptes> Role(int id, string role, bool ajouter)
        => GesteAsync(ClientServeur.Actif, RoutesComptes.Role, new DemandeRole(id, role, ajouter, NomEcran), ajouter ? "Rôle ajouté" : "Rôle retiré");

    /// <summary>L'ecran lu sur <paramref name="serveur"/> (null : la demonstration).</summary>
    public static async Task<VueEcranComptes> OuvrirAsync(ClientServeur? serveur, string? message = null, bool echec = false,
                                                          int? selection = null, bool proposerDesactivation = false)
    {
        if (serveur is null) return Demonstration();
        if (Greffe is not { } greffe) return SansGreffe();
        try
        {
            var vue = await serveur.LireAsync<VueComptes>(RoutesComptes.Pour(RoutesComptes.Comptes, greffe));
            var journal = await serveur.LireAsync<VueJournal>(RoutesJournal.Pour(RoutesJournal.Gestes, greffe));
            var droits = await serveur.LireAsync<LigneJournalDroits[]>(RoutesDroits.Pour(RoutesDroits.Journal, greffe));
            return new VueEcranComptes("serveur", message, echec, vue.Agent, vue.Comptes, vue.Services, vue.Roles,
                journal.Lignes, droits, journal.ToutLeGreffe, selection, proposerDesactivation);
        }
        catch (ErreurServeur e) when (e.CodeHttp == 403)
        {
            return Vide("refuse",
                "Vous n'avez pas le droit de gérer les comptes des agents : il faut le rôle « Gestion des habilitations ».");
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            return Vide("erreur", $"Le serveur de RIG ne répond pas ({serveur.Adresse}) : {e.Message}");
        }
    }

    private static async Task<VueEcranComptes> GesteAsync<TDemande>(ClientServeur? serveur, string route, TDemande demande, string fait)
    {
        if (serveur is null)
            return Demonstration() with { Message = "Démonstration : sans serveur, les comptes ne se modifient pas.", Echec = true };
        if (Greffe is not { } greffe) return SansGreffe();
        ResultatCompte resultat;
        try
        {
            resultat = await serveur.EnvoyerAsync<TDemande, ResultatCompte>(RoutesComptes.Pour(route, greffe), demande);
        }
        catch (ErreurServeur e) when (e.CodeHttp == 403)
        {
            return await OuvrirAsync(serveur);
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            return await OuvrirAsync(serveur, $"Le geste n'a pas été enregistré : {e.Message}", echec: true);
        }
        return await OuvrirAsync(serveur,
            resultat.Fait ? resultat.Message is { } m ? $"{fait} : {m}" : $"{fait}." : resultat.Message,
            echec: !resultat.Fait,
            selection: resultat.Compte?.Id,
            proposerDesactivation: resultat.ProposerDesactivation);
    }

    private static VueEcranComptes Vide(string mode, string message)
        => new(mode, message, true, "", [], [], [], [], [], false, null, false);

    private static VueEcranComptes SansGreffe()
        => Vide("erreur", $"Aucun greffe n'est choisi pour cette session : la variable {VariableGreffe} donne son code (ex. 3801).");

    /// <summary>Sans serveur : les comptes de la maquette, en lecture seule.</summary>
    private static VueEcranComptes Demonstration()
        => new("demo", null, false, "",
            [.. JeuDemo.Comptes.Select((c, i) => new CompteAgent(i + 1, c.Login, c.Nom, "", "", c.Login.Length > 15 ? c.Login[..16] : c.Login,
                null, null, c.DomainesLisibles, false, false, false, [c.Profil]))],
            [], [], [], [], false, null, false);
}
