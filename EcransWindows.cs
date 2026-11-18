using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace RigV3Desktop;

/// <summary>
/// Les ECRANS physiques, lus aupres de Windows : zone de travail de chacun
/// (barre des taches deduite) et lequel porte une fenetre donnee. Regroupe ici
/// pour que la fenetre d'une vue arrachee n'ait pas a redeclarer les appels
/// systeme.
/// </summary>
internal static class EcransWindows
{
    /// <summary>Zones de travail de TOUS les ecrans, en unites WPF, et l'index
    /// de celui qui porte <paramref name="fenetre"/> (-1 si inconnu).</summary>
    public static (List<PlacementDocument.Zone> Zones, int IndexPorteur) Zones(Visual? repere, Window? fenetre)
    {
        var source = repere is null ? null : PresentationSource.FromVisual(repere);
        var versUnitesWpf = source?.CompositionTarget?.TransformFromDevice
                            ?? System.Windows.Media.Matrix.Identity;

        var porteur = fenetre is null
            ? IntPtr.Zero
            : MonitorFromWindow(new WindowInteropHelper(fenetre).Handle, MONITOR_DEFAULTTONEAREST);

        var zones = new List<PlacementDocument.Zone>();
        var index = -1;
        bool Visiter(IntPtr moniteur, IntPtr _, ref RECT __, IntPtr ___)
        {
            var detail = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(moniteur, ref detail)) return true;
            var origine = versUnitesWpf.Transform(new Point(detail.rcWork.left, detail.rcWork.top));
            var etendue = versUnitesWpf.Transform(
                new Point(detail.rcWork.right - detail.rcWork.left,
                          detail.rcWork.bottom - detail.rcWork.top));
            if (porteur != IntPtr.Zero && moniteur == porteur) index = zones.Count;
            zones.Add(new PlacementDocument.Zone(origine.X, origine.Y, etendue.X, etendue.Y));
            return true;
        }
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Visiter, IntPtr.Zero);
        return (zones, index);
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
