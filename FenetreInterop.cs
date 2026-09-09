using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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

    [JSInvokable]
    public void Fermer() => Fenetre?.Close();
}
