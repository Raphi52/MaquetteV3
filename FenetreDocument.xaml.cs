using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RigV3Desktop;

/// <summary>
/// Le document de l'ecran VDossier, SORTI dans sa propre fenetre.
///
/// Elle se pose d'emblee sur un AUTRE moniteur que la fenetre principale
/// quand il en existe un, et se redimensionne independamment : c'est tout
/// l'interet de « Detacher » (demande du 2026-09-14).
/// </summary>
public partial class FenetreDocument : Window
{
    /// <summary>Rang d'ouverture (1re fenetre, 2e...). C'est la clef de la
    /// position memorisee ET l'emplacement du document dans le passe-plat :
    /// plusieurs documents peuvent donc etre sortis en meme temps.</summary>
    public int Rang { get; }

    public FenetreDocument(int rang = 1)
    {
        Rang = rang;
        InitializeComponent();

        // Le rang est passe a la racine de la page : sans lui, toutes les
        // fenetres liraient le meme emplacement et montreraient le meme document.
        Racine = new Pont.RacineDocumentDetache(App.Partage, rang);
        Title = rang > 1 ? $"RIG V3 - Document {rang}" : "RIG V3 - Document";

        // Meme reglages que la fenetre principale (PageWeb) : la liste
        // deroulante d'un <select> suit la preference de couleur du navigateur hote.
        Loaded += async (_, _) => await Pont.PageWeb.Brancher(Vue, Racine);
        Closed += async (_, _) => await Racine.DisposeAsync();
    }

    /// <summary>Ce que la page de cette fenetre monte : l'apercu du document de
    /// l'emplacement <see cref="Rang"/>.</summary>
    public Pont.RacineDocumentDetache Racine { get; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (!RestaurerPose()) PoserSurAutreEcran();
    }

    /// <summary>
    /// Remet la fenetre la ou l'utilisateur l'avait laissee, pour ce rang.
    /// Rend false quand rien n'est memorise OU quand la pose retenue ne tombe
    /// plus sur un ecran present (portable debranche du dock) : sinon la
    /// fenetre s'ouvrirait hors de vue.
    /// </summary>
    private bool RestaurerPose()
    {
        var pose = AccueilEtat.PoseDocument(Rang);
        // Le verdict vit dans AccueilEtat.PoseUtilisable : fonction pure, donc
        // verifiable a l'EXECUTION par les controles (FenetresCheck), ce qu'une
        // methode de Window ne permet pas.
        var zones = ZonesDeTravail()
            .Select(z => (X: z.X, Y: z.Y, Largeur: z.Width, Hauteur: z.Height));
        if (!AccueilEtat.PoseUtilisable(pose, zones)) return false;

        Left = pose!.Gauche;
        Top = pose.Haut;
        Width = pose.Largeur;
        Height = pose.Hauteur;
        return true;
    }

    /// <summary>Les zones de travail de TOUS les ecrans, en unites WPF.</summary>
    private List<Rect> ZonesDeTravail()
    {
        var source = PresentationSource.FromVisual(this);
        var versUnitesWpf = source?.CompositionTarget?.TransformFromDevice
                            ?? System.Windows.Media.Matrix.Identity;
        var zones = new List<Rect>();
        bool Visiter(IntPtr moniteur, IntPtr _, ref RECT __, IntPtr ___)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(moniteur, ref info))
            {
                var coin = versUnitesWpf.Transform(new Point(info.rcWork.left, info.rcWork.top));
                var bas = versUnitesWpf.Transform(new Point(info.rcWork.right, info.rcWork.bottom));
                zones.Add(new Rect(coin, bas));
            }
            return true;
        }
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Visiter, IntPtr.Zero);
        return zones;
    }

    /// <summary>
    /// A la fermeture, la place occupee est retenue POUR CE RANG : la
    /// prochaine fenetre de meme rang reviendra exactement la.
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        var pose = WindowState == WindowState.Normal
            ? new PoseFenetre { Gauche = Left, Haut = Top, Largeur = Width, Hauteur = Height }
            : new PoseFenetre
            {
                Gauche = RestoreBounds.Left, Haut = RestoreBounds.Top,
                Largeur = RestoreBounds.Width, Hauteur = RestoreBounds.Height
            };
        if (pose.Largeur >= 200 && pose.Hauteur >= 150)
            AccueilEtat.MemoriserDocument(Rang, pose);
    }

    /// <summary>
    /// Cherche un moniteur qui ne porte PAS la fenetre principale et s'y
    /// installe (moitie droite de sa zone de travail). S'il n'y en a qu'un,
    /// la fenetre se pose a cote, dans la zone de travail de celui-la.
    /// Meme methode que MainWindow : MonitorFromWindow + rcWork, en pixels
    /// physiques repasses en unites WPF.
    /// </summary>
    private void PoserSurAutreEcran()
    {
        var source = PresentationSource.FromVisual(this);
        var versUnitesWpf = source?.CompositionTarget?.TransformFromDevice
                            ?? System.Windows.Media.Matrix.Identity;

        var handle = new WindowInteropHelper(this).Handle;
        var principal = Application.Current?.MainWindow is { } m
            ? new WindowInteropHelper(m).Handle
            : IntPtr.Zero;
        var moniteurPrincipal = principal != IntPtr.Zero
            ? MonitorFromWindow(principal, MONITOR_DEFAULTTONEAREST)
            : IntPtr.Zero;

        // La fenetre ne decide PAS elle-meme : elle collecte les ecrans, puis
        // delegue le calcul a PlacementDocument (fonction pure, rejouable par
        // les controles avec deux ecrans simules -- voir FenetresCheck).
        var zones = new List<PlacementDocument.Zone>();
        var indexPrincipal = -1;
        bool Visiter(IntPtr moniteur, IntPtr _, ref RECT __, IntPtr ___)
        {
            var detail = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(moniteur, ref detail)) return true;

            var origine = versUnitesWpf.Transform(new Point(detail.rcWork.left, detail.rcWork.top));
            var etendue = versUnitesWpf.Transform(
                new Point(detail.rcWork.right - detail.rcWork.left,
                          detail.rcWork.bottom - detail.rcWork.top));

            if (moniteur == moniteurPrincipal) indexPrincipal = zones.Count;
            zones.Add(new PlacementDocument.Zone(origine.X, origine.Y, etendue.X, etendue.Y));
            return true;
        }
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Visiter, IntPtr.Zero);

        var pose = PlacementDocument.Calculer(zones, indexPrincipal, Rang, Width, Height);
        if (pose is null) return;

        Left = pose.Gauche;
        Top = pose.Haut;
        Width = pose.Largeur;
        Height = pose.Hauteur;
    }

    private const int MONITOR_DEFAULTTONEAREST = 2;

    private delegate bool MonitorEnumProc(IntPtr moniteur, IntPtr hdc, ref RECT rect, IntPtr donnee);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr rect, MonitorEnumProc rappel, IntPtr donnee);

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
