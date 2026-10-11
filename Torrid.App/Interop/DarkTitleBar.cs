namespace Torrid.App.Interop;

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

internal static class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    /// <summary>Asks DWM for a dark caption bar (Windows 10 20H1+ / 11). Silently does nothing on older systems.</summary>
    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var one = 1;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref one, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
