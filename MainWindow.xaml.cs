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
