using System.IO;
using System.Net.Http;
using Microsoft.Win32;
using RigV3.Contrats.Socle;
using RigV3Desktop.Pont;

namespace RigV3.Lanceur;

// ============================================================
// CE QUE LE LANCEUR VERIFIE AVANT D'OUVRIR L'ECRAN (SOC-012 T02, T03 ; SOC-009 CA05 ;
// lot 12 du plan, 2026-11-21).
//
//   1. une version COMPLETE de l'ecran est installee sur ce serveur RDS (Installation) ;
//   2. le composant Microsoft Edge WebView2 Runtime est present (CA04) : sans lui l'ecran
//      ne peut rien afficher, d'ou un lanceur NATIF (WPF seul) qui peut encore le dire ;
//   3. branche au serveur (RIGV3_SERVEUR, comme l'ecran) : la version installee est celle
//      de la partie serveur (CA03, CA06 ; SOC-009 CA05), comparee comme la V2 (#716) ;
//   4. greffe connu au lancement (« /g3801 », « greffe=3801 », ou un seul code dans
//      RIGV3_GREFFES : les regles de l'ecran, SessionAgent) : la base du greffe porte la
//      version du serveur (SOC-009 CA05), message du serveur a l'appui.
// Un refus nomme les versions en jeu et ce qu'il faut faire, et remonte a l'exploitation
// (POST /socle/version/erreurs-demarrage) ; l'agent n'a qu'a fermer ou reessayer.
//
// Une fois l'ecran ouvert, la VEILLE (CA03) redemande la version du serveur a intervalle
// regulier : plus recente que l'ecran en cours -> l'agent est averti, comme l'accueil de la
// V2 (FORM_ACCUEIL.cs:3710-3724), et RIEN n'est ferme a sa place : sa saisie reste.
//
// Ce fichier ne depend ni de WPF ni de l'ecran : le banc SocleLivraisonCheck le joue contre le
// vrai serveur en memoire.
// ============================================================

/// <summary>La decision du lanceur.</summary>
/// <param name="Ouvrir">Vrai : l'ecran s'ouvre. Faux : <see cref="Titre"/> et <see cref="Message"/> s'affichent.</param>
/// <param name="Remonter">Le refus est remonte a l'exploitation.</param>
/// <param name="Reessayer">Un nouvel essai a un sens (serveur momentanement injoignable).</param>
/// <param name="Ecart">Quelle ligne de versions est en cause : « ecran », « base », « composant », « serveur » ; null sinon.</param>
public sealed record Verdict(bool Ouvrir, string Titre, string Message, string? VersionEcran, string? VersionServeur,
                             string? VersionBase, string? Greffe, bool Remonter, bool Reessayer, string? Ecart)
{
    public static Verdict Accorde(string? ecran, string? serveur, string? baseGreffe, string? greffe)
        => new(true, "", "", ecran, serveur, baseGreffe, greffe, false, false, null);
}

public static class Verification
{
    /// <summary>La cle de registre du WebView2 Runtime (valeur « pv »), Microsoft : « Detect if a WebView2 Runtime is
    /// already installed », https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution.</summary>
    private const string CleWebView2 = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    /// <summary>
    /// La version du WebView2 Runtime installee (par machine, puis par utilisateur) ; null s'il manque. Regle de
    /// Microsoft : une valeur absente, vide ou « 0.0.0.0 » veut dire « non installe ».
    /// </summary>
    public static string? VersionWebView2()
    {
        foreach (var (racine, chemin) in new[]
                 {
                     (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\" + CleWebView2),
                     (Registry.LocalMachine, @"SOFTWARE\" + CleWebView2),
                     (Registry.CurrentUser, @"Software\" + CleWebView2),
                 })
        {
            using var cle = racine.OpenSubKey(chemin);
            if (cle?.GetValue("pv") is string pv && !string.IsNullOrWhiteSpace(pv) && pv != "0.0.0.0") return pv;
        }
        return null;
    }

    /// <summary>
    /// Juge le lancement. <paramref name="serveur"/> null = pas de RIGV3_SERVEUR : l'ecran s'ouvre en demonstration,
    /// sans controle de version (comme l'ecran lui-meme). <paramref name="sondeWebView2"/> rend la version du composant
    /// ou null (<see cref="VersionWebView2"/> en vrai, une valeur fixe dans le banc).
    /// </summary>
    public static async Task<Verdict> JugerAsync(string racine, Func<string?> sondeWebView2, ClientServeur? serveur,
                                                 IReadOnlyList<string> arguments, string? greffesDuPoste, CancellationToken ct = default)
    {
        var installee = Installation.Courante(racine);
        var greffe = SessionAgent.GreffeDeLancement(arguments)
                     ?? (SessionAgent.CodesDuPoste(greffesDuPoste) is [var seul] ? seul : null);
        if (installee is null)
            return new Verdict(false, "RIG V3 n'est pas installé sur ce serveur",
                $"Aucune version complète de RIG V3 dans {Path.Combine(racine, Installation.DossierVersions)}. " +
                "L'exploitation est prévenue : elle doit poser la version de l'écran sur ce serveur.",
                null, null, null, greffe, Remonter: true, Reessayer: false, Ecart: "ecran");

        if (sondeWebView2() is null)
            return new Verdict(false, "Il manque un composant sur ce serveur",
                "Le composant Microsoft Edge WebView2 Runtime n'est pas installé sur ce serveur : RIG V3 ne peut pas afficher ses écrans. " +
                "L'exploitation est prévenue ; en attendant, continuez dans RIG V2.",
                installee.Version, null, null, greffe, Remonter: true, Reessayer: false, Ecart: "composant");

        if (serveur is null) return Verdict.Accorde(installee.Version, null, null, greffe);

        VersionServeur versionServeur;
        try { versionServeur = await serveur.LireAsync<VersionServeur>(RoutesVersions.Serveur, ct); }
        catch (Exception e) when (e is HttpRequestException or ErreurServeur or TaskCanceledException)
        {
            return new Verdict(false, "La partie serveur de RIG V3 ne répond pas",
                $"Le serveur {serveur.Adresse} ne répond pas ({e.Message}). Réessayez dans un instant ; si cela dure, prévenez l'exploitation.",
                installee.Version, null, null, greffe, Remonter: false, Reessayer: true, Ecart: "serveur");
        }

        if (!VersionV3.Egales(installee.Version, versionServeur.Version))
            return VersionEcranDifferente(installee.Version, versionServeur, greffe);

        if (greffe is not null)
        {
            try
            {
                var versions = await serveur.LireAsync<VersionsGreffe>(RoutesVersions.Pour(greffe), ct);
                if (!versions.Concordent)
                    return new Verdict(false, "La base de votre greffe n'est pas à la bonne version",
                        versions.Message ?? $"Partie serveur {versions.Serveur}, base du greffe {versions.Base}.",
                        installee.Version, versions.Serveur, versions.Base, greffe, Remonter: true, Reessayer: false, Ecart: "base");
                return Verdict.Accorde(installee.Version, versions.Serveur, versions.Base, greffe);
            }
            catch (ErreurServeur e) when (e.CodeHttp is 404 or 503)
            {
                // Greffe inconnu ou base muette : l'ecran a ses propres messages, ceux de la V2 (SessionAgent).
            }
        }
        return Verdict.Accorde(installee.Version, versionServeur.Version, null, greffe);
    }

    /// <summary>SOC-012 CA03 / CA06 : l'ecran installe n'est pas a la version du serveur de son site.</summary>
    public static Verdict VersionEcranDifferente(string ecran, VersionServeur serveur, string? greffe)
        => VersionV3.Comparer(ecran, serveur.Version) < 0
            ? new Verdict(false, "Votre RIG V3 n'est pas à jour",
                $"RIG V3 est installé en version {ecran} sur ce serveur, la partie serveur ({serveur.Site}) est en version {serveur.Version}. " +
                $"RIG V3 ne s'ouvre pas dans une version périmée : l'exploitation est prévenue et doit poser la version {serveur.Version} sur ce serveur.",
                ecran, serveur.Version, null, greffe, Remonter: true, Reessayer: false, Ecart: "ecran")
            : new Verdict(false, "RIG V3 est plus récent que sa partie serveur",
                $"RIG V3 est installé en version {ecran} sur ce serveur, la partie serveur ({serveur.Site}) est encore en version {serveur.Version}. " +
                $"L'exploitation est prévenue : elle doit mettre la partie serveur en version {ecran}.",
                ecran, serveur.Version, null, greffe, Remonter: true, Reessayer: true, Ecart: "serveur");

    /// <summary>
    /// SOC-012 CA03 : la VEILLE, une fois l'ecran ouvert. Une partie serveur plus recente que l'ecran en cours ->
    /// l'avertissement de la V2 (FORM_ACCUEIL.cs:3721-3722, « votre accueil Rig est obsolete... veuillez le
    /// redemarrer »). Rien n'est ferme a la place de l'agent : null quand il n'y a rien a dire.
    /// </summary>
    public static Verdict? Veiller(string versionEnCours, VersionServeur serveur, string? greffe)
        => VersionV3.Comparer(versionEnCours, serveur.Version) < 0
            ? new Verdict(false, "Une nouvelle version de RIG V3 est disponible",
                $"ATTENTION... la version de votre RIG V3 est obsolète ({versionEnCours}). " +
                $"Veuillez le redémarrer pour être à jour ({serveur.Version}). " +
                "Terminez d'abord votre saisie en cours : RIG V3 ne se ferme pas tout seul.",
                versionEnCours, serveur.Version, null, greffe, Remonter: false, Reessayer: false, Ecart: "ecran")
            : null;

    /// <summary>Remonte un refus a l'exploitation ; sans serveur ou s'il ne repond pas, rend la cause pour le journal local.</summary>
    public static async Task<string?> RemonterAsync(ClientServeur? serveur, Verdict verdict, CancellationToken ct = default)
    {
        if (serveur is null) return "pas de serveur (RIGV3_SERVEUR absente)";
        try
        {
            await serveur.EnvoyerAsync<ErreurDemarrage, ErreurDemarrage>(RoutesVersions.ErreursDemarrage,
                new ErreurDemarrage(Environment.MachineName, Environment.UserName, verdict.Greffe, verdict.VersionEcran,
                    $"{verdict.Titre} : {verdict.Message}"), ct);
            return null;
        }
        catch (Exception e) when (e is HttpRequestException or ErreurServeur or TaskCanceledException or System.Text.Json.JsonException)
        {
            return e.Message;
        }
    }
}
