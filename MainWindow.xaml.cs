using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RigV3Desktop;

/// <summary>
/// Fenetre hote. Son contenu est une page React servie par un controle WebView2
/// (voir MainWindow.xaml) ; sa racine est l'application entiere, accueil et plan
/// de travail (Pont/Racines.cs, RacinePlanTravail).
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Ce que la page de cette fenetre monte. Les commandes de fenetre
    /// et le passe-plat des documents sont ceux de l'application (App).</summary>
    public Pont.RacinePlanTravail Racine { get; } = new(App.Fenetre, App.Partage);

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            // Le selecteur de couleur des listes deroulantes (theme sombre) est
            // fixe par PageWeb. Cela ne suffit PAS pour les lignes : sans fond
            // OPAQUE, elles restent blanches sous un texte clair (regle
            // `select option`, rig-charte.css).
#if DEBUG
            await ViderCacheAuDemarrage();
#endif
            // SOC-001 : branchee au serveur, la page attend de savoir QUI ouvre RIG
            // (sa session Windows) ; un agent refuse voit l'ecran de refus a la place
            // de l'application, qui n'est jamais ouverte (Pont/SessionAgent.cs).
            var identite = await Pont.SessionAgent.OuvrirAsync(() => Pont.ClientServeur.Actif);
            if (identite is { Admis: false }) Refus = new Pont.RacineRefus(identite, App.Fenetre);
            await Pont.PageWeb.Brancher(Vue, Refus ?? (Pont.RacineWeb)Racine);
        };
        Closed += async (_, _) =>
        {
            await Racine.DisposeAsync();
            if (Refus is not null) await Refus.DisposeAsync();
        };
    }

    /// <summary>L'ecran de refus, quand le serveur n'admet pas l'agent (SOC-001) ; null sinon.</summary>
    private Pont.RacineRefus? Refus;

#if DEBUG
    /// <summary>
    /// En developpement, on repart d'un cache VIDE : sinon une feuille de style
    /// modifiee reste invisible apres un redemarrage complet (index.html
    /// reference les CSS sans numero de version). Fait AVANT le premier
    /// chargement, pour qu'aucune feuille perimee ne soit servie.
    /// </summary>
    private async Task ViderCacheAuDemarrage()
    {
        await Vue.EnsureCoreWebView2Async(await Pont.PageWeb.EnvironnementPartage());
        await ViderCacheAsync();
    }
#endif

    /// <summary>
    /// Place la fenetre a la main, sur le moniteur ou se trouve le pointeur.
    ///
    /// Pourquoi pas WindowStartupLocation="CenterScreen" : WPF l'applique a
    /// l'affichage, APRES le constructeur, et il centre la taille demandee
    /// (1500x900) meme quand l'ecran est plus petit. Sur un poste distant dont
    /// la zone de travail ne fait que 540 de large, cela donne une abscisse de
    /// (540-1500)/2 = -480 : la fenetre demarre hors de l'ecran, a gauche.
    ///
    /// On attend donc que le handle existe (le DPI de la fenetre est alors
    /// connu), on lit la zone de travail REELLE du moniteur concerne, et on y
    /// borne la fenetre.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

#if DEBUG
        SurveillerWwwroot();
#endif

        var source = PresentationSource.FromVisual(this);
        var versUnitesWpf = source?.CompositionTarget?.TransformFromDevice
                            ?? System.Windows.Media.Matrix.Identity;

        var handle = new WindowInteropHelper(this).Handle;

        // Sans barre de titre native (WindowStyle=None), Windows agrandit la
        // fenetre a la taille TOTALE de l'ecran (rcMonitor) et non a sa zone
        // de travail : le bas de l'application passe alors SOUS la barre des
        // taches. On repond donc nous-memes a WM_GETMINMAXINFO en bornant la
        // taille maximale a rcWork du moniteur concerne.
        (source as HwndSource)?.AddHook(BornerAgrandissement);

        var moniteur = MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST);

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(moniteur, ref info)) return;

        // rcWork est en pixels physiques : on repasse en unites WPF.
        var coin = versUnitesWpf.Transform(
            new Point(info.rcWork.left, info.rcWork.top));
        var taille = versUnitesWpf.Transform(
            new Point(info.rcWork.right - info.rcWork.left,
                      info.rcWork.bottom - info.rcWork.top));

        var largeur = Math.Min(Width, taille.X);
        var hauteur = Math.Min(Height, taille.Y);

        // Sous cette taille l'accueil n'a plus de place utile : on prend toute
        // la zone de travail plutot que d'afficher une fenetre amputee.
        if (largeur < 900 || hauteur < 600)
        {
            largeur = taille.X;
            hauteur = taille.Y;
        }

        Width = largeur;
        Height = hauteur;
        Left = coin.X + (taille.X - largeur) / 2;
        Top = coin.Y + (taille.Y - hauteur) / 2;
    }

#if DEBUG
    // ---------------------------------------------------------------
    // Reprise a chaud des fichiers statiques (CSS, images, JS construit).
    //
    // La page est servie depuis le wwwroot COPIE dans le dossier de sortie
    // (hote virtuel, Pont/PageWeb.cs), pas depuis les sources. On surveille
    // donc les sources : au moindre changement, le fichier est recopie dans
    // la sortie et la page est rechargee. Aucun redemarrage de l'application.
    // Le code C# est repris par « dotnet watch run », les .tsx par le serveur
    // Vite (dev.ps1) : sous Vite, wwwroot/app (le JS construit) n'est pas lu
    // et ses changements ne rechargent rien.
    // ---------------------------------------------------------------
    private FileSystemWatcher? Guetteur;
    private System.Windows.Threading.DispatcherTimer? Attente;

    private void SurveillerWwwroot()
    {
        var sortie = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var source = TrouverWwwrootSource();
        if (source is null) return;

        // Un changement en rafale (enregistrement d'un editeur = plusieurs
        // evenements) ne doit declencher qu'un seul rechargement.
        Attente = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        Attente.Tick += async (_, _) =>
        {
            Attente!.Stop();
            RecopierWwwroot(source, sortie);
            await ViderCacheAsync();
            Vue.CoreWebView2?.Reload();
        };
        var sousVite = Pont.PageWeb.AdresseVite() is not null;
        var dossierApp = Path.Combine(source, "app") + Path.DirectorySeparatorChar;

        Guetteur = new FileSystemWatcher(source)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        void Change(object _, FileSystemEventArgs e)
        {
            // Sous Vite, le JS construit (wwwroot/app) n'est pas celui que la
            // page charge : une construction (dotnet build, npm run build) ne
            // doit pas recharger la page et lui faire perdre son etat.
            if (sousVite && e.FullPath.StartsWith(dossierApp, StringComparison.OrdinalIgnoreCase)) return;
            Dispatcher.Invoke(() => { Attente!.Stop(); Attente.Start(); });
        }
        Guetteur.Changed += Change;
        Guetteur.Created += Change;
        Guetteur.Renamed += (o, e) => Change(o, e);
    }

    /// <summary>
    /// Vide le cache disque du WebView2 AVANT de recharger.
    ///
    /// Cause racine mesuree le 2026-09-17 : « Reload() » recharge le HTML mais
    /// ressert les sous-ressources (rig-charte.css, rig-accueil.css, le JS)
    /// depuis le cache, car index.html les reference sans numero de version.
    /// Une couleur modifiee dans la charte restait donc invisible, meme apres
    /// avoir ferme et rouvert l'application -- le cache vit dans le profil.
    /// </summary>
    private async Task ViderCacheAsync()
    {
        var coeur = Vue.CoreWebView2;
        if (coeur is null) return;
        try
        {
            await coeur.Profile.ClearBrowsingDataAsync(
                Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.DiskCache);
        }
        catch (Exception)
        {
            // Profil indisponible : le rechargement simple reste tente.
        }
    }

    /// <summary>
    /// Retrouve le wwwroot des SOURCES en remontant depuis le dossier
    /// d'execution (bin/Debug/net10.0-windows). Rend null hors developpement.
    /// </summary>
    private static string? TrouverWwwrootSource()
    {
        var dossier = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 5 && dossier is not null; i++, dossier = dossier.Parent)
        {
            var candidat = Path.Combine(dossier.FullName, "RigV3Desktop.csproj");
            if (File.Exists(candidat))
            {
                var w = Path.Combine(dossier.FullName, "wwwroot");
                return Directory.Exists(w) ? w : null;
            }
        }
        return null;
    }

    private static void RecopierWwwroot(string source, string sortie)
    {
        try
        {
            foreach (var fichier in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var cible = Path.Combine(sortie, Path.GetRelativePath(source, fichier));
                var dossierCible = Path.GetDirectoryName(cible);
                if (dossierCible is not null) Directory.CreateDirectory(dossierCible);
                if (File.Exists(cible) &&
                    File.GetLastWriteTimeUtc(cible) >= File.GetLastWriteTimeUtc(fichier)) continue;
                File.Copy(fichier, cible, overwrite: true);
            }
        }
        catch (IOException)
        {
            // Fichier encore tenu par l'editeur : le prochain evenement repassera.
        }
    }
#endif

    private const int WM_GETMINMAXINFO = 0x0024;

    /// <summary>
    /// Borne l'agrandissement a la zone de travail (hors barre des taches) du
    /// moniteur qui porte la fenetre. Tout est en pixels physiques ici.
    /// </summary>
    private IntPtr BornerAgrandissement(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;

        var moniteur = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (moniteur == IntPtr.Zero) return IntPtr.Zero;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(moniteur, ref info)) return IntPtr.Zero;

        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        mmi.ptMaxPosition.x = info.rcWork.left - info.rcMonitor.left;
        mmi.ptMaxPosition.y = info.rcWork.top - info.rcMonitor.top;
        mmi.ptMaxSize.x = info.rcWork.right - info.rcWork.left;
        mmi.ptMaxSize.y = info.rcWork.bottom - info.rcWork.top;
        Marshal.StructureToPtr(mmi, lParam, true);

        handled = true;
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    private const int MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }
}
