namespace Torrid.App.Tray;

using System.Windows.Forms;
using System.Windows.Threading;
using Torrid.Core.Config;
using Torrid.Core.Sensors;

/// <summary>
/// One tray icon per sensor whose <see cref="SensorDisplay.IsShownInTray"/> is true. Redraws on every reading, on every
/// config change (so the Tray switches on the Sensors page add/remove icons immediately) and on hardware status changes.
/// Always reads <see cref="ConfigManager.Config"/> at redraw time; never caches the config. Created and used on the UI thread;
/// every event is marshalled with <see cref="UiThread.Post"/>.
/// </summary>
public sealed class TrayIconGroupManager : IDisposable
{
    private readonly ISensorSource _sensorSource;
    private readonly ConfigManager _config;
    private readonly IHardwareStatusSource _hardware;
    private readonly Dictionary<string, SensorTrayIcon> _icons = new();
    private readonly DispatcherTimer _blinkTimer;
    private readonly ContextMenuStrip _contextMenu;
    private bool _blinkOn = true;
    private bool _anyRed;
    private bool _disposed;

    public TrayIconGroupManager(ISensorSource sensors, ConfigManager config, IHardwareStatusSource hardware)
    {
        _sensorSource = sensors;
        _config = config;
        _hardware = hardware;

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add("Settings", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        _contextMenu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _blinkTimer.Tick += OnBlinkTick;
        _blinkTimer.Start();

        _sensorSource.ReadingsUpdated += OnReadingsUpdated;
        _config.Changed += OnSomethingChanged;
        _hardware.StatusChanged += OnSomethingChanged;

        Redraw();
    }

    /// <summary>Left click on any icon.</summary>
    public event EventHandler? TrayIconClicked;

    /// <summary>Context menu "Settings".</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Context menu "Exit".</summary>
    public event EventHandler? ExitRequested;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _sensorSource.ReadingsUpdated -= OnReadingsUpdated;
        _config.Changed -= OnSomethingChanged;
        _hardware.StatusChanged -= OnSomethingChanged;

        _blinkTimer.Stop();
        _blinkTimer.Tick -= OnBlinkTick;
        foreach (var icon in _icons.Values) icon.Dispose();
        _icons.Clear();
        _contextMenu.Dispose();
    }

    private void OnReadingsUpdated(object? sender, IReadOnlyList<Torrid.Core.Models.SensorReading> readings) => UiThread.Post(Redraw);

    private void OnSomethingChanged(object? sender, EventArgs e) => UiThread.Post(Redraw);

    private void OnBlinkTick(object? sender, EventArgs e)
    {
        _blinkOn = !_blinkOn;
        if (_anyRed)
        {
            Redraw();
        }
    }

    private void Redraw()
    {
        // A redraw posted from the poll thread can arrive after Dispose during shutdown.
        if (_disposed) return;

        var config = _config.Config;
        var status = _hardware.Status;
        var visibleReadings = _sensorSource.CurrentReadings
            .Where(r => SensorDisplay.IsShownInTray(config, r))
            .ToList();
        var visibleIds = visibleReadings.Select(r => r.Id).ToHashSet();

        _anyRed = visibleReadings.Any(r =>
            r.ValueCelsius is double v &&
            config.Thresholds.TryGetValue(r.Kind, out var band) &&
            ColorThresholdEvaluator.GetColor(v, band) == ColorZone.Red);

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

            if (config.Thresholds.TryGetValue(reading.Kind, out var band))
            {
                icon.UpdateVisual(reading, SensorDisplay.ResolveLabel(config, reading), band, _blinkOn, status);
            }
        }
    }
}
