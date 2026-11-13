namespace Torrid.App.Overlay;

using System.Runtime.InteropServices;
using System.Windows.Interop;
using Torrid.Core.Overlay;

/// <summary>
/// One global hotkey via RegisterHotKey on a message-only window. Never a keyboard hook (it looks like cheat software).
/// </summary>
internal sealed class HotkeyHost : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 1;
    private const uint ModNoRepeat = 0x4000;

    private readonly HwndSource _source;
    private readonly nint _hwnd;
    private bool _disposed;

    public HotkeyHost()
    {
        // -3 = HWND_MESSAGE: a window that receives messages only and is never shown.
        _source = new HwndSource(new HwndSourceParameters("TorridHotkey") { ParentWindow = new nint(-3), WindowStyle = 0 });
        _hwnd = _source.Handle;
        _source.AddHook(WndProc);
    }

    public event EventHandler? Pressed;

    public bool TryRegister(Hotkey hotkey)
    {
        if (_disposed) return false;

        Unregister();
        return RegisterHotKey(_hwnd, HotkeyId, (uint)hotkey.Modifiers | ModNoRepeat, (uint)hotkey.VirtualKey);
    }

    public void Unregister() => UnregisterHotKey(_hwnd, HotkeyId);

    public void Dispose()
    {
        if (_disposed) return;

        Unregister();
        _disposed = true;
        _source.Dispose();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return nint.Zero;
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}
