namespace Torrid.App.Tray;

using System.Windows.Forms;
using Torrid.Core.Config;
using Torrid.Core.Models;

public sealed class SensorTrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly SingleValueBitmapRenderer _renderer = new();
    private nint _previousIconHandle;

    public SensorTrayIcon()
    {
        _notifyIcon = new NotifyIcon { Visible = true };
        _notifyIcon.Click += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Clicked;

    public void AttachContextMenu(System.Windows.Forms.ContextMenuStrip menu) => _notifyIcon.ContextMenuStrip = menu;

    public void UpdateVisual(SensorReading reading, string label, ThresholdBand thresholds, bool blinkOn)
    {
        var icon = _renderer.Render(reading.ValueCelsius, thresholds, blinkOn);
        _notifyIcon.Icon = icon;
        _notifyIcon.Text = TooltipTextBuilder.Build(new[] { reading with { Label = label } });

        if (_previousIconHandle != nint.Zero) NativeMethods.DestroyIcon(_previousIconHandle);
        _previousIconHandle = icon.Handle;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        if (_previousIconHandle != nint.Zero) NativeMethods.DestroyIcon(_previousIconHandle);
    }
}
