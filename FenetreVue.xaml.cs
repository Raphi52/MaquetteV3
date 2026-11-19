using System.Linq;
using System.Windows;

namespace RigV3Desktop;

/// <summary>
/// Un ONGLET SORTI de la fenetre principale, dans sa propre fenetre, comme
/// dans un navigateur (demande du 17/09/2026). Elle porte le meme ecran que
/// l'onglet, et revient a l'endroit ou elle a ete laissee POUR CETTE VUE :
/// c'est ce qui fait que l'agencement se retrouve d'une session a l'autre.
/// </summary>
public partial class FenetreVue : Window
{
    /// <summary>Code de l'ecran montre (« PROC_XAFF »). C'est aussi la clef de
    /// la position memorisee : la place suit la VUE, pas l'ordre d'arrachage.</summary>
    public string Code { get; }

    /// <summary>Rang d'ouverture, utilise seulement pour decaler les fenetres
    /// entre elles la toute premiere fois.</summary>
    public int Rang { get; }

    /// <summary>Vrai quand la fenetre nait d un onglet qu on est EN TRAIN de
    /// glisser : elle apparait sous le pointeur et le suit jusqu au lacher,
    /// comme un onglet de navigateur arrache.</summary>
    private readonly bool SuivreCurseur;

    public FenetreVue(string code, string libelle, int rang = 1, bool suivreCurseur = false)
    {
        Code = code ?? string.Empty;
        Rang = rang;
        SuivreCurseur = suivreCurseur;
        InitializeComponent();

        // Le code de l'ecran est passe a la racine de la page : sans lui, la
        // fenetre sortie afficherait l'accueil au lieu de l'onglet qu'on vient
        // d'arracher.
        Racine = new Pont.RacinePlanTravail(App.Fenetre, App.Partage, modeFenetreVue: true, vueInitiale: Code);
        Title = string.IsNullOrWhiteSpace(libelle) ? "RIG V3" : "RIG V3 - " + libelle;

        // L'affaire montree ici vient d'y etre fermee (sa croix, Echap) : la fenetre
        // s'en va. Apres le geste en cours, pour ne pas fermer la page sous lui.
        Racine.Plan.FermerFenetre += () => Dispatcher.BeginInvoke(Close);
        Loaded += async (_, _) => await Pont.PageWeb.Brancher(Vue, Racine);
        Closed += async (_, _) => await Racine.DisposeAsync();
    }

    /// <summary>Ce que la page de cette fenetre monte : l'application en mode
    /// « onglet sorti », ouverte sur l'ecran <see cref="Code"/>.</summary>
    public Pont.RacinePlanTravail Racine { get; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (SuivreCurseur)
        {
            // La taille retenue pour cette vue est gardee, la PLACE non : la
            // fenetre nait sous le pointeur, la ou l utilisateur l emmene.
            var pose = AccueilEtat.PoseVue(Code);
            if (pose is not null && pose.Largeur >= 200 && pose.Hauteur >= 150)
            {
                Width = pose.Largeur;
                Height = pose.Hauteur;
            }
            CollerAuCurseur();
            DemarrerSuivi();
            return;
        }
        if (!RestaurerPose()) PoserSurAutreEcran();
    }

    // ============ SUIVI DU POINTEUR (onglet arrache) ============
    // Le bouton de la souris est encore enfonce, mais c est la page de la
    // fenetre PRINCIPALE qui le tient : cette fenetre ne recoit aucun
    // mouvement. On lit donc la position ECRAN du curseur et l etat du bouton
    // directement, a chaque image, jusqu au lacher.

    /// <summary>Ou le curseur « tient » la fenetre, en pixels physiques depuis
    /// son coin haut-gauche : dans la barre de titre, vers la gauche, la ou
    /// serait l onglet.</summary>
    private const int PriseX = 110, PriseY = 16;

    private System.Windows.Threading.DispatcherTimer? Suivi;

    /// <summary>Coin haut-gauche (pixels physiques) de la fenetre arrachee pour
    /// un curseur en (x, y) : la fenetre tombe sur l ecran ou le curseur est lache.</summary>
    public static (int X, int Y) PoseSousCurseur(int x, int y) => (x - PriseX, y - PriseY);

    private void CollerAuCurseur()
    {
        if (!GetCursorPos(out var p)) return;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        // Coordonnees PHYSIQUES des deux cotes (curseur et SetWindowPos) : aucune
        // conversion d echelle, donc juste sur deux ecrans de DPI differents.
        var (x, y) = PoseSousCurseur(p.X, p.Y);
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private void DemarrerSuivi()
    {
        Suivi = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Render)
        { Interval = TimeSpan.FromMilliseconds(8) };
        Suivi.Tick += (_, _) =>
        {
            if (BoutonGaucheEnfonce()) { CollerAuCurseur(); return; }
            Suivi?.Stop();
            Suivi = null;
            Activate();
        };
        Suivi.Start();
    }

    private static bool BoutonGaucheEnfonce()
    {
        // Boutons inverses (gaucher) : le bouton « principal » est le droit.
        var vk = GetSystemMetrics(SM_SWAPBUTTON) != 0 ? VK_RBUTTON : VK_LBUTTON;
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT p);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, SM_SWAPBUTTON = 23;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    /// <summary>Remet la fenetre la ou elle avait ete laissee POUR CETTE VUE.
    /// Rend false si rien n'est retenu ou si la place ne tombe plus sur un
    /// ecran present (portable debranche du dock).</summary>
    private bool RestaurerPose()
    {
        var pose = AccueilEtat.PoseVue(Code);
        var zones = EcransWindows.Zones(this, null).Zones;
        if (!AccueilEtat.PoseUtilisable(pose,
                zones.Select(z => (z.X, z.Y, z.Largeur, z.Hauteur)))) return false;

        Left = pose!.Gauche;
        Top = pose.Haut;
        Width = pose.Largeur;
        Height = pose.Hauteur;
        return true;
    }

    /// <summary>Premiere sortie de cette vue : elle se pose sur un ecran qui ne
    /// porte PAS la fenetre principale quand il en existe un -- c'est tout
    /// l'interet d'arracher l'onglet. Le calcul est celui, deja eprouve, des
    /// fenetres de document.</summary>
    private void PoserSurAutreEcran()
    {
        var (zones, indexPrincipal) = EcransWindows.Zones(this, Application.Current?.MainWindow);
        var pose = PlacementDocument.Calculer(zones, indexPrincipal, Rang, Width, Height);
        if (pose is null) return;
        Left = pose.Gauche;
        Top = pose.Haut;
        Width = pose.Largeur;
        Height = pose.Hauteur;
    }

    /// <summary>A la fermeture, la place occupee est retenue POUR CETTE VUE.</summary>
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
            AccueilEtat.MemoriserFenetreVue(Code, pose);
    }
}
