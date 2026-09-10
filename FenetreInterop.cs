using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.JSInterop;

namespace RigV3Desktop;

/// <summary>
/// Commandes de fenetre exposees a la page Blazor : la barre de titre native
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

    [JSInvokable]
    public void Deplacer()
    {
        var fenetre = Fenetre;
        if (fenetre is null) return;
        var handle = new WindowInteropHelper(fenetre).Handle;
        if (handle == IntPtr.Zero) return;
        ReleaseCapture();
        SendMessage(handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
    }

    [JSInvokable]
    public void Reduire()
    {
        if (Fenetre is { } f) f.WindowState = WindowState.Minimized;
    }

    [JSInvokable]
    public void BasculerAgrandissement()
    {
        if (Fenetre is not { } f) return;
        f.WindowState = f.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    /// <summary>
    /// Aligne le fond de la fenetre WPF sur le theme de la page : la marge de
    /// 6 px autour du WebView est peinte par WPF, un fond fige sombre
    /// dessinerait un bord noir en mode clair.
    /// </summary>
    public void AppliquerFond(bool sombre)
    {
        if (Fenetre is not { } f) return;
        var teinte = sombre
            ? Color.FromRgb(0x0C, 0x0F, 0x16)
            : Color.FromRgb(0xD8, 0xDD, 0xE6);
        f.Background = new SolidColorBrush(teinte);
    }

    [JSInvokable]
    public void Fermer() => Fenetre?.Close();
}
