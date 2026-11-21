using System.IO;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace RigV3Desktop.Pont;

// ============================================================
// LA PAGE D'UNE FENETRE : branche un controle WebView2 (le controle direct,
// paquet Microsoft.Web.WebView2) sur sa racine, depuis le retrait de Blazor
// (19/11/2026). Les trois fenetres (MainWindow, FenetreVue, FenetreDocument)
// passent toutes par ici.
//
// - wwwroot/ est servi par un NOM D'HOTE VIRTUEL (https://rigv3.example/), sans
//   serveur : WebView2 lit les fichiers du dossier de l'application.
// - En developpement (dev.ps1), la page vient du serveur Vite, qui reprend un
//   .tsx a chaud (RIGV3_VITE, Debug seulement, localhost seulement).
// - Le pont passe par la messagerie de WebView2 (Repartiteur) ; une page
//   d'une autre origine n'y a pas acces, et la fenetre ne la charge pas.
// ============================================================

public static class PageWeb
{
    /// <summary>Le nom d'hote virtuel de wwwroot. « .example » est reserve
    /// (RFC 2606) : il ne designe jamais un vrai site.</summary>
    public const string Hote = "rigv3.example";

    /// <summary>La variable que dev.ps1 pose pour charger la page depuis Vite.</summary>
    public const string VariableVite = "RIGV3_VITE";

    private static Task<CoreWebView2Environment>? environnement;

    /// <summary>
    /// UN environnement pour toutes les fenetres, dans le dossier de donnees que
    /// BlazorWebView utilisait deja (%LOCALAPPDATA%\RigV3Desktop.WebView2) et
    /// sans option : une instance encore sous Blazor et celle-ci partagent ce
    /// dossier sans conflit d'options, et le cache n'est pas perdu.
    /// </summary>
    public static Task<CoreWebView2Environment> EnvironnementPartage()
        => environnement ??= CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RigV3Desktop.WebView2"));

    /// <summary>Le wwwroot servi : celui du dossier de l'application.</summary>
    public static string DossierWwwroot => Path.Combine(AppContext.BaseDirectory, "wwwroot");

    /// <summary>L'adresse du serveur Vite si dev.ps1 l'a donnee (Debug, localhost), sinon null.</summary>
    public static Uri? AdresseVite()
    {
#if DEBUG
        var valeur = Environment.GetEnvironmentVariable(VariableVite);
        if (Uri.TryCreate(valeur, UriKind.Absolute, out var adresse)
            && adresse.Scheme == Uri.UriSchemeHttp
            && (adresse.IsLoopback || adresse.Host == "localhost"))
        {
            return adresse;
        }
#endif
        return null;
    }

    /// <summary>L'adresse de la page : Vite en developpement, sinon wwwroot par l'hote virtuel.</summary>
    public static Uri AdressePage() => AdresseVite() ?? new Uri($"https://{Hote}/index.html");

    /// <summary>Vrai pour une adresse de la page elle-meme (meme origine).</summary>
    public static bool EstDeLaPage(string adresse)
        => Uri.TryCreate(adresse, UriKind.Absolute, out var u)
           && Uri.Compare(u, AdressePage(), UriComponents.SchemeAndServer,
                          UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;

    /// <summary>
    /// Branche la vue sur sa racine et charge la page. Rend le repartiteur
    /// (la fenetre principale s'en sert pour recharger la page).
    /// </summary>
    public static async Task<Repartiteur> Brancher(WebView2 vue, RacineWeb racine)
    {
        await vue.EnsureCoreWebView2Async(await EnvironnementPartage());
        var coeur = vue.CoreWebView2;

        // La liste deroulante d'un <select> n'est PAS peinte par la page : c'est
        // une fenetre du systeme, qui suit la preference de couleur du
        // navigateur hote (defaut : celle de Windows, ici le mode clair). D'ou
        // la popup blanche sur texte clair, illisible, constatee le 2026-09-10 :
        // `color-scheme` dans la feuille de style ne pouvait pas y arriver.
        coeur.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;
        // Les touches du NAVIGATEUR sont coupees (SOC-027, decision du chef du plan « Reconstruction de RIG --
        // phase 2 », lot 4, 21/11/2026) : dans l'ancien RIG, F5 et F7 lancent la recherche ; ici F5 rechargeait
        // toute la V3 -- la saisie en cours etait perdue -- et F7 basculait la navigation au curseur. Elles arrivent
        // a la page, qui en fait ce que la V3 a decide (web/src/plan/clavier.ts, toucheDeRecherche). Le reglage
        // coupe aussi Ctrl R, Ctrl F et F3 (chercher dans la page), Ctrl P (les ecrans ont leur bouton Imprimer),
        // Ctrl + et Ctrl - (Ctrl + molette zoome toujours) et Alt fleche (retour arriere). Le controle WPF ne donne
        // pas le reglage touche par touche (IsBrowserAcceleratorKeyEnabled vit sur CoreWebView2Controller, qu'il
        // garde pour lui) : c'est donc le reglage d'ensemble, documente par Microsoft
        // (learn.microsoft.com/dotnet/api/microsoft.web.webview2.core.corewebview2settings.arebrowseracceleratorkeysenabled).
        // Copier, coller, tout selectionner, annuler et les touches de deplacement restent au navigateur.
        coeur.Settings.AreBrowserAcceleratorKeysEnabled = false;
#if DEBUG
        // F12 ouvre les outils de developpement (console, elements) en Debug. Le reglage ci-dessus coupe la
        // touche, pas les outils : le controle WPF la remonte en KeyDown (comme toute touche de navigateur), et
        // la fenetre s'ouvre APRES l'evenement -- pendant, le navigateur attend la reponse de l'evenement.
        coeur.Settings.AreDevToolsEnabled = true;
        vue.KeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.F12) return;
            e.Handled = true;
            vue.Dispatcher.BeginInvoke(() => coeur.OpenDevToolsWindow());
        };
#else
        coeur.Settings.AreDevToolsEnabled = false;
#endif
        coeur.SetVirtualHostNameToFolderMapping(Hote, DossierWwwroot, CoreWebView2HostResourceAccessKind.Allow);
        // Les fichiers JOINTS aux affaires (Metier/ArbreGed.cs, PiecesJointes) : un second hote, sur
        // le dossier qui va avec le fichier de reglages. Leur adresse est complete (https://pieces...),
        // elle se lit donc aussi quand la page vient de Vite. DenyCors : la colonne du document les
        // ouvre dans son cadre ; aucun script d'une autre origine ne les lit.
        Directory.CreateDirectory(AccueilEtat.DossierPiecesJointes);
        coeur.SetVirtualHostNameToFolderMapping(PiecesJointes.Hote, AccueilEtat.DossierPiecesJointes,
                                                CoreWebView2HostResourceAccessKind.DenyCors);

        // Rien d'autre que la page ne se charge dans la fenetre : une autre
        // origine aurait acces au pont.
        coeur.NavigationStarting += (_, e) => { if (!EstDeLaPage(e.Uri)) e.Cancel = true; };
        coeur.NewWindowRequested += (_, e) => e.Handled = true;

        var repartiteur = new Repartiteur(
            racine,
            json => coeur.PostWebMessageAsJson(json),
            action => vue.Dispatcher.BeginInvoke(action));

        coeur.WebMessageReceived += (_, e) =>
        {
            if (!EstDeLaPage(e.Source)) return;
            var message = e.WebMessageAsJson;
            // Traite APRES l'evenement, sur le fil de la fenetre, comme Blazor :
            // un geste qui ouvre une boucle modale (deplacer, redimensionner la
            // fenetre) ne doit pas la tenir a l'interieur d'un evenement WebView2.
            vue.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Func<Task>(() => repartiteur.Recevoir(message)));
        };

        coeur.Navigate(AdressePage().AbsoluteUri);
        return repartiteur;
    }
}
