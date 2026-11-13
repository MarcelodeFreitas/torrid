namespace Torrid.App.Overlay;

using System.Diagnostics;
using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Overlay;
using Torrid.Core.Sensors;

/// <summary>
/// Owns the overlay window and its hotkey. Shows or hides the overlay and keeps its lines, size and colour current.
/// Only UI thread: every event is marshalled with <see cref="UiThread.Post"/>.
/// </summary>
public sealed class OverlayController : IDisposable
{
    private readonly ISensorSource _sensors;
    private readonly IHardwareStatusSource _hardware;
    private readonly ConfigManager _config;
    private readonly Action<string?> _reportHotkeyError;
    private readonly HotkeyHost _host = new();
    private OverlayWindow? _window;
    private string? _registeredHotkeyText;
    private bool _disposed;

    public OverlayController(ISensorSource sensors, IHardwareStatusSource hardware, ConfigManager config, Action<string?> reportHotkeyError)
    {
        _sensors = sensors;
        _hardware = hardware;
        _config = config;
        _reportHotkeyError = reportHotkeyError;
        _host.Pressed += (_, _) => Toggle();
    }

    public void Start()
    {
        _sensors.ReadingsUpdated += OnReadingsUpdated;
        _hardware.StatusChanged += OnStatusChanged;
        _config.Changed += OnConfigEvent;
        RegisterFromConfig();
    }

    public void Toggle()
    {
        if (_disposed) return;

        try
        {
            _window ??= new OverlayWindow();
            if (_window.IsVisible)
            {
                _window.Hide();
                return;
            }

            Refresh();
            _window.Show();
        }
        catch (Exception ex)
        {
            // A hotkey press must never crash the app from inside the message loop.
            Debug.WriteLine($"Overlay toggle failed: {ex}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _sensors.ReadingsUpdated -= OnReadingsUpdated;
        _hardware.StatusChanged -= OnStatusChanged;
        _config.Changed -= OnConfigEvent;
        _host.Dispose();
        _window?.Close();
        _window = null;
    }

    private void OnReadingsUpdated(object? sender, IReadOnlyList<SensorReading> readings) => UiThread.Post(Refresh);

    private void OnStatusChanged(object? sender, EventArgs e) => UiThread.Post(Refresh);

    private void OnConfigEvent(object? sender, EventArgs e) => UiThread.Post(OnConfigChanged);

    private void OnConfigChanged()
    {
        if (_disposed) return;

        if (_config.Config.OverlayHotkey != _registeredHotkeyText) RegisterFromConfig();
        Refresh();
    }

    private void RegisterFromConfig()
    {
        var text = _config.Config.OverlayHotkey;
        _registeredHotkeyText = text;
        var hotkey = Hotkey.TryParse(text, out var parsed) ? parsed : Hotkey.Default;

        _reportHotkeyError(_host.TryRegister(hotkey)
            ? null
            : $"{hotkey.Format()} is already used by another program. Choose a different shortcut.");
    }

    /// <summary>
    /// Applies the current lines, size and colour. Runs before the first Show too (Toggle calls it first), so it only
    /// skips when there is no window yet; the window itself ignores the placement until it has a source.
    /// </summary>
    private void Refresh()
    {
        if (_disposed || _window is null) return;

        try
        {
            var config = _config.Config;
            _window.SetHiddenFromCapture(config.OverlayHiddenFromCapture);
            _window.Apply(OverlayLines.Build(config, _sensors.CurrentReadings, _hardware.Status), config.OverlayTextSize, config.OverlayTextColor);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Refreshing the overlay failed: {ex}");
        }
    }
}
