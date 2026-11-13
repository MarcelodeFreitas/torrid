namespace Torrid.App.Overlay;

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Torrid.Core.Overlay;

/// <summary>Click-through, never-focused sensor overlay in the top-right of the primary screen. Only UI thread.</summary>
public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const nint WsExTransparent = 0x20;
    private const nint WsExLayered = 0x80000;
    private const nint WsExNoActivate = 0x08000000;
    private const nint WsExToolWindow = 0x80;

    public OverlayWindow()
    {
        InitializeComponent();
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

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtrW(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(nint hWnd, uint dwAffinity);
}
