namespace Torrid.App.Overlay;

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Torrid.Core.Overlay;

/// <summary>Click-through, never-focused sensor overlay in the top-right of the primary screen, re-raised above other topmost windows while visible. Only UI thread.</summary>
public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const nint WsExTransparent = 0x20;
    private const nint WsExLayered = 0x80000;
    private const nint WsExNoActivate = 0x08000000;
    private const nint WsExToolWindow = 0x80;
    private static readonly nint HwndTopmost = -1;
    private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10;
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0, WinEventSkipOwnProcess = 0x2;

    private readonly DispatcherTimer _raiseTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private WinEventProc? _onForeground; // kept in a field: Windows holds the pointer, the GC must not collect the delegate
    private nint _foregroundHook;

    public OverlayWindow()
    {
        InitializeComponent();
        _raiseTimer.Tick += (_, _) => RaiseToTop();
        IsVisibleChanged += (_, _) => KeepOnTop(IsVisible);
    }

    public void Apply(IReadOnlyList<string> lines, int textSize, string colorHex)
    {
        LinesText.Text = string.Join("\n", lines);
        LinesText.FontSize = OverlayAppearance.ClampTextSize(textSize);

        var color = OverlayAppearance.IsValidColor(colorHex)
            ? (System.Windows.Media.Color)ColorConverter.ConvertFromString(colorHex)
            : Colors.White;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        LinesText.Foreground = brush;

        Reposition();
    }

    /// <summary>Creates the handle if needed, so it also applies before the first Show. The result is ignored: Windows older than 10 2004 does not support it.</summary>
    public void SetHiddenFromCapture(bool hidden) =>
        SetWindowDisplayAffinity(new WindowInteropHelper(this).EnsureHandle(), OverlayCapture.DisplayAffinity(hidden));

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtrW(hwnd, GwlExStyle);
        SetWindowLongPtrW(hwnd, GwlExStyle, style | WsExTransparent | WsExLayered | WsExNoActivate | WsExToolWindow);

        SizeChanged += (_, _) => Reposition();
    }

    private void Reposition()
    {
        if (PresentationSource.FromVisual(this) is null) return;
        if (System.Windows.Forms.Screen.PrimaryScreen is not { } screen) return;
        var area = screen.WorkingArea;

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var (left, top) = OverlayPlacement.TopRight(area.Left, area.Top, area.Width, ActualWidth * scale, 16 * scale);
        Left = left / scale;
        Top = top / scale;
    }

    private void KeepOnTop(bool on)
    {
        if (on)
        {
            RaiseToTop();
            _raiseTimer.Start();
            if (_foregroundHook == 0)
            {
                _onForeground ??= (_, _, _, _, _, _, _) => RaiseToTop();
                _foregroundHook = SetWinEventHook(EventSystemForeground, EventSystemForeground, 0, _onForeground, 0, 0, WinEventOutOfContext | WinEventSkipOwnProcess);
            }
        }
        else
        {
            _raiseTimer.Stop();
            if (_foregroundHook != 0)
            {
                UnhookWinEvent(_foregroundHook);
                _foregroundHook = 0;
            }
        }
    }

    // Topmost alone loses to topmost windows activated later; re-raise to stay above them.
    private void RaiseToTop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    protected override void OnClosed(EventArgs e)
    {
        KeepOnTop(false);
        base.OnClosed(e);
    }

    private delegate void WinEventProc(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtrW(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(nint hWnd, uint dwAffinity);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint hmodWinEventProc, WinEventProc pfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(nint hWinEventHook);
}
