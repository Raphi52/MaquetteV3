using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RigV3Desktop;

/// <summary>
/// Fenetre hote. Son contenu est rendu par une vue Blazor (voir MainWindow.xaml).
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // La liste deroulante d'un <select> n'est PAS peinte par la page : c'est
        // une fenetre du systeme. Elle suit la preference de couleur du
        // navigateur hote, dont le defaut (Auto) copie celle de Windows -- ici
        // le mode clair. D'ou la popup blanche sur texte clair, illisible,
        // constatee le 2026-09-10 : `color-scheme` dans la feuille de style ne
        // pouvait pas y arriver. On fixe donc la preference du WebView lui-meme.
        Vue.BlazorWebViewInitialized += (_, args) =>
        {
            args.WebView.CoreWebView2.Profile.PreferredColorScheme =
                Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark;
        };
    }

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
    // Reprise a chaud des fichiers statiques (CSS, JS, images).
    //
    // BlazorWebView ne reprend pas wwwroot a chaud (dotnet/aspnetcore#37496)
    // et il sert les fichiers COPIES dans le dossier de sortie, pas les
    // sources. On surveille donc les sources : au moindre changement, le
    // fichier est recopie dans la sortie et la page est rechargee. Aucun
    // redemarrage de l'application. Le code C# et les .razor, eux, sont
    // repris par « dotnet watch run » (dev.ps1).
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
        Attente.Tick += (_, _) =>
        {
            Attente!.Stop();
            RecopierWwwroot(source, sortie);
            Vue.WebView?.Reload();
        };

        Guetteur = new FileSystemWatcher(source)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        void Change(object _, FileSystemEventArgs __) =>
            Dispatcher.Invoke(() => { Attente!.Stop(); Attente.Start(); });
        Guetteur.Changed += Change;
        Guetteur.Created += Change;
        Guetteur.Renamed += (o, e) => Change(o, e);
    }

    /// <summary>
    /// Retrouve le wwwroot des SOURCES en remontant depuis le dossier
    /// d'execution (bin/Debug/net9.0-windows). Rend null hors developpement.
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
