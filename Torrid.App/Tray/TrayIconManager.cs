namespace Torrid.App.Tray;

using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using Torrid.Core.Config;
using Torrid.Core.Sensors;

public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly TrayBitmapRenderer _renderer = new();
    private readonly DispatcherTimer _blinkTimer;
    private readonly ISensorSource _sensorSource;
    private readonly AppConfig _config;
    private bool _blinkOn = true;
    private nint _previousIconHandle;

    public TrayIconManager(ISensorSource sensorSource, AppConfig config)
    {
        _sensorSource = sensorSource;
        _config = config;

        _notifyIcon = new NotifyIcon { Visible = true };
        _notifyIcon.Click += (_, _) => TrayIconClicked?.Invoke(this, EventArgs.Empty);

        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _blinkTimer.Tick += (_, _) => { _blinkOn = !_blinkOn; Redraw(); };
        _blinkTimer.Start();

        _sensorSource.ReadingsUpdated += (_, _) => System.Windows.Application.Current?.Dispatcher.Invoke(Redraw);

        Redraw();
    }

    public event EventHandler? TrayIconClicked;

    private void Redraw()
    {
        var readings = _sensorSource.CurrentReadings;
        var icon = _renderer.Render(readings, _config.Thresholds, _blinkOn);

        _notifyIcon.Icon = icon;
        _notifyIcon.Text = TooltipTextBuilder.Build(readings);

        if (_previousIconHandle != nint.Zero) NativeMethods.DestroyIcon(_previousIconHandle);
        _previousIconHandle = icon.Handle;
    }

    public void Dispose()
    {
        _blinkTimer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        if (_previousIconHandle != nint.Zero) NativeMethods.DestroyIcon(_previousIconHandle);
    }
}
