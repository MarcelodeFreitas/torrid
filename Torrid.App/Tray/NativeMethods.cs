namespace Torrid.App.Tray;

using System.Runtime.InteropServices;

internal static class NativeMethods
{
    // Icon.FromHandle / Bitmap.GetHicon() hand back a native HICON that .NET does not
    // own or free automatically — every icon this renderer creates must be destroyed
    // explicitly or the process leaks a GDI handle on every redraw (multiple times a second).
    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(nint handle);
}
