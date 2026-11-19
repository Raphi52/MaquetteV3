using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace RigV3Desktop;

/// <summary>
/// Commandes de la fenetre principale, menees pour la page : la barre de titre native
/// est supprimee (WindowStyle=None), l'interface porte donc elle-meme
/// deplacement, reduction, agrandissement et fermeture.
/// </summary>
public sealed class FenetreInterop
{
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;

    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    private static Window? Fenetre => Application.Current?.MainWindow;

    public void Deplacer()
    {
        var fenetre = Fenetre;
        if (fenetre is null) return;
        var handle = new WindowInteropHelper(fenetre).Handle;
        if (handle == IntPtr.Zero) return;
        ReleaseCapture();
        SendMessage(handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
    }

    public void Reduire()
    {
        if (Fenetre is { } f) f.WindowState = WindowState.Minimized;
    }

    public void BasculerAgrandissement()
    {
        if (Fenetre is not { } f) return;
        f.WindowState = f.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    /// <summary>
    /// Aligne le fond de la fenetre WPF sur celui de la page. Il ne se voit
    /// plus en temps normal (le WebView occupe toute la fenetre), mais il
    /// apparait au chargement et sur la bande qui s'ouvre pendant un
    /// redimensionnement, avant que le WebView ne repeigne. La teinte est le
    /// fond du theme dans <see cref="ThemesRig"/> ; CharteCheck verifie qu'il
    /// suit --rig-fond de rig-charte.css (les anciennes constantes avaient
    /// derive et peignaient un cadre gris autour de la page ivoire).
    /// </summary>
    public void AppliquerFond(string theme)
    {
        if (Fenetre is not { } f) return;
        var teinte = (Color)ColorConverter.ConvertFromString(ThemesRig.De(theme).Fond);
        f.Background = new SolidColorBrush(teinte);
    }

    /// <summary>
    /// Redimensionne la fenetre depuis un bord de la page. Le WebView couvre
    /// toute la fenetre (plus de marge de 6 px, qui dessinait un cadre) : il
    /// avale donc le pointeur sur les bords de WindowChrome. Les bandes de
    /// bord de la page (web/src/fenetre/Bords.tsx) appellent ceci, et Windows mene le redimensionnement
    /// lui-meme, comme pour Deplacer -- aimantation comprise.
    /// </summary>
    /// <summary>Faux une fois la fenetre agrandie : ses bords ne se tirent plus,
    /// et la page retire ses bandes (sinon le curseur de redimensionnement
    /// s'afficherait sur les bords d'une fenetre qui ne bouge pas).</summary>
    public bool Redimensionnable => Fenetre?.WindowState == WindowState.Normal;

    /// <summary>Signale un changement normal / agrandie / reduite.</summary>
    public event Action? EtatChange;

    private Window? suivie;

    /// <summary>Branche <see cref="EtatChange"/> sur la fenetre principale,
    /// une seule fois.</summary>
    public void SuivreEtat()
    {
        if (suivie is not null || Fenetre is not { } f) return;
        suivie = f;
        f.StateChanged += (_, _) => EtatChange?.Invoke();
    }

    public void Redimensionner(BordFenetre bord)
    {
        var fenetre = Fenetre;
        if (fenetre is null || fenetre.WindowState != WindowState.Normal) return;
        var handle = new WindowInteropHelper(fenetre).Handle;
        if (handle == IntPtr.Zero) return;
        ReleaseCapture();
        SendMessage(handle, WM_NCLBUTTONDOWN, (int)bord, 0);
    }

    public void Fermer() => Fenetre?.Close();
}

/// <summary>Bords de la fenetre, valeurs HT* de WM_NCHITTEST (winuser.h).</summary>
public enum BordFenetre
{
    Gauche = 10,
    Droite = 11,
    Haut = 12,
    HautGauche = 13,
    HautDroite = 14,
    Bas = 15,
    BasGauche = 16,
    BasDroite = 17
}
