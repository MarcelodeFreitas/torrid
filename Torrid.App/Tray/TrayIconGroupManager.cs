namespace Torrid.App.Tray;

using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using Torrid.Core.Config;
using Torrid.Core.Sensors;

public sealed class TrayIconGroupManager : IDisposable
{
    private readonly ISensorSource _sensorSource;
    private readonly AppConfig _config;
    private readonly Dictionary<string, SensorTrayIcon> _icons = new();
    private readonly DispatcherTimer _blinkTimer;
    private readonly ContextMenuStrip _contextMenu;
    private bool _blinkOn = true;

    public TrayIconGroupManager(ISensorSource sensorSource, AppConfig config)
    {
        _sensorSource = sensorSource;
        _config = config;

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add("Settings", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        _contextMenu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _blinkTimer.Tick += (_, _) => { _blinkOn = !_blinkOn; Redraw(); };
        _blinkTimer.Start();

        _sensorSource.ReadingsUpdated += (_, _) => System.Windows.Application.Current?.Dispatcher.Invoke(Redraw);

        Redraw();
    }

    public event EventHandler? TrayIconClicked;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    private void Redraw()
    {
        var visibleReadings = _sensorSource.CurrentReadings
            .Where(r => SensorDisplay.IsVisible(_config, r.Id))
            .ToList();
        var visibleIds = visibleReadings.Select(r => r.Id).ToHashSet();

        foreach (var staleId in _icons.Keys.Except(visibleIds).ToList())
        {
            _icons[staleId].Dispose();
            _icons.Remove(staleId);
        }

        foreach (var reading in visibleReadings)
        {
            if (!_icons.TryGetValue(reading.Id, out var icon))
            {
                icon = new SensorTrayIcon();
                icon.Clicked += (_, _) => TrayIconClicked?.Invoke(this, EventArgs.Empty);
                icon.AttachContextMenu(_contextMenu);
                _icons[reading.Id] = icon;
            }

            if (_config.Thresholds.TryGetValue(reading.Kind, out var band))
            {
                icon.UpdateVisual(reading, SensorDisplay.ResolveLabel(_config, reading), band, _blinkOn);
            }
        }
    }

    public void Dispose()
    {
        _blinkTimer.Stop();
        foreach (var icon in _icons.Values) icon.Dispose();
        _icons.Clear();
        _contextMenu.Dispose();
    }
}
