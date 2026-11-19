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
#if DEBUG
        // F12 ouvre les outils de developpement (console, elements) en Debug.
        coeur.Settings.AreDevToolsEnabled = true;
#else
        coeur.Settings.AreDevToolsEnabled = false;
#endif
        coeur.SetVirtualHostNameToFolderMapping(Hote, DossierWwwroot, CoreWebView2HostResourceAccessKind.Allow);

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
