namespace Torrid.App.ViewModels;

using System.Collections.ObjectModel;
using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Overlay;
using Torrid.Core.Sensors;

public sealed record OverlayColorChoice(string Name, string Hex);

/// <summary>One checkbox of the Settings page's "Sensors to show" list. Writes through <see cref="ConfigManager.Update"/>.</summary>
public sealed class OverlaySensorItem : ObservableObject
{
    private readonly ConfigManager _config;
    private string _label;
    private bool _isShown;

    internal OverlaySensorItem(SensorIdentity identity, string deviceTitle, ConfigManager config)
    {
        Identity = identity;
        DeviceTitle = deviceTitle;
        _config = config;
        _label = identity.DefaultLabel;
    }

    internal SensorIdentity Identity { get; }

    public string Id => Identity.Id;

    public string DefaultLabel => Identity.DefaultLabel;

    /// <summary>The resolved name. A blank value or the default name clears the custom label.</summary>
    public string Label
    {
        get => _label;
        set
        {
            var trimmed = value?.Trim() ?? "";
            var custom = trimmed.Length == 0 || trimmed == DefaultLabel ? null : trimmed;

            // Name boxes commit on every focus loss; only write (and save) when the stored label really changes.
            var stored = _config.Config.Sensors.TryGetValue(Id, out var preference) && !string.IsNullOrWhiteSpace(preference.Label)
                ? preference.Label
                : null;
            if (stored != custom) _config.Update(c => SensorPreferences.Update(c, Id, p => p.Label = custom));

            // Always raise: the edit box may hold "   " while the resolved value is unchanged.
            _label = custom ?? DefaultLabel;
            Raise(nameof(Label));
        }
    }

    public string DeviceTitle { get; private set; }

    /// <summary>The real sensor name, kept visible while the editable name above can change.</summary>
    public string Caption => $"Sensor: {DefaultLabel} · {DeviceTitle}";

    public bool IsShown
    {
        get => _isShown;
        set
        {
            var stored = value == SensorDefaults.ShownInOverlay(Identity.Kind) ? (bool?)null : value;
            _config.Update(c => SensorPreferences.Update(c, Id, p => p.ShowInOverlay = stored));
            Set(ref _isShown, value);
        }
    }

    /// <summary>Re-reads the label and the checkbox from the config (no writes).</summary>
    internal void RefreshFromConfig()
    {
        Set(ref _label, SensorDisplay.ResolveLabel(_config.Config, Identity), nameof(Label));
        Set(ref _isShown, SensorDisplay.IsShownInOverlay(_config.Config, Identity), nameof(IsShown));
    }
}

/// <summary>
/// The Overlay page: the shortcut, text size and colour, and the sensors (and their names) the overlay lists.
/// Writes through <see cref="ConfigManager.Update"/> and reads back from the config. App-lifetime singleton, so its
/// event subscriptions are never removed (same note as <see cref="SettingsPageViewModel"/>).
/// </summary>
public sealed class OverlaySettingsViewModel : ObservableObject
{
    private readonly ConfigManager _config;
    private readonly ISensorSource _sensors;
    private string? _hotkeyError;

    public OverlaySettingsViewModel(ConfigManager config, ISensorSource sensors, ISensorCatalogSource? catalog = null)
    {
        _config = config;
        _sensors = sensors;
        ColorChoices = OverlayAppearance.Presets.Select(p => new OverlayColorChoice(p.Name, p.Hex)).ToList();

        // Always open: ScanAsync drops the results of a closed panel, and this card has no Close button.
        Sync = new AddSensorViewModel(catalog, config, () => sensors.CurrentReadings);
        Sync.OpenCommand.Execute(null);
        Sync.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AddSensorViewModel.IsScanning)) Raise(nameof(SyncButtonText));
        };

        // Readings arrive on the poll thread; always hop to the UI thread before touching bound collections.
        _config.Changed += (_, _) => UiThread.Post(Refresh);
        _sensors.ReadingsUpdated += (_, _) => UiThread.Post(Refresh);

        Refresh();
    }

    /// <summary>The "Sync sensors" card: scans the hardware and lists every sensor that can be added to the overlay.</summary>
    public AddSensorViewModel Sync { get; }

    public string SyncButtonText => Sync.IsScanning ? "Syncing…" : "Sync sensors";

    public string HotkeyText =>
        (Hotkey.TryParse(_config.Config.OverlayHotkey, out var hotkey) ? hotkey : Hotkey.Default).Format();

    /// <summary>False (nothing changes) when the hotkey has no modifier or an unsupported key.</summary>
    public bool TrySetHotkey(Hotkey hotkey)
    {
        if (!hotkey.IsValid) return false;

        _config.Update(c => c.OverlayHotkey = hotkey.Format());
        Raise(nameof(HotkeyText));
        return true;
    }

    public string? HotkeyError => _hotkeyError;

    public bool HasHotkeyError => _hotkeyError is not null;

    /// <summary>Called through AppHost when the hotkey registration succeeds (null) or fails (a message).</summary>
    public void SetHotkeyError(string? message)
    {
        if (Set(ref _hotkeyError, message, nameof(HotkeyError))) Raise(nameof(HasHotkeyError));
    }

    public int TextSize
    {
        get => _config.Config.OverlayTextSize;
        set
        {
            var clamped = OverlayAppearance.ClampTextSize(value);
            if (clamped != _config.Config.OverlayTextSize) _config.Update(c => c.OverlayTextSize = clamped);
            Raise(nameof(TextSize));
        }
    }

    /// <summary>An invalid colour is ignored; the re-raise makes the control show the stored colour again.</summary>
    public string TextColor
    {
        get => _config.Config.OverlayTextColor;
        set
        {
            if (OverlayAppearance.IsValidColor(value) && value != _config.Config.OverlayTextColor)
            {
                _config.Update(c => c.OverlayTextColor = value);
            }

            Raise(nameof(TextColor));
        }
    }

    public bool HiddenFromCapture
    {
        get => _config.Config.OverlayHiddenFromCapture;
        set
        {
            if (value != _config.Config.OverlayHiddenFromCapture) _config.Update(c => c.OverlayHiddenFromCapture = value);
            Raise(nameof(HiddenFromCapture));
        }
    }

    public IReadOnlyList<OverlayColorChoice> ColorChoices { get; }

    public ObservableCollection<OverlaySensorItem> Sensors { get; } = new();

    private void Refresh()
    {
        Raise(nameof(HotkeyText));
        Raise(nameof(TextSize));
        Raise(nameof(TextColor));
        Raise(nameof(HiddenFromCapture));

        var existing = Sensors.ToDictionary(i => i.Id);
        var desired = new List<OverlaySensorItem>();
        foreach (var row in SensorRows.Build(_config.Config, _sensors.CurrentReadings))
        {
            // Reuse an item while its identity and device title are unchanged, so the bound checkboxes keep their instances.
            if (!existing.TryGetValue(row.Identity.Id, out var item) || item.Identity != row.Identity || item.DeviceTitle != row.Group.Title)
            {
                item = new OverlaySensorItem(row.Identity, row.Group.Title, _config);
            }

            item.RefreshFromConfig();
            desired.Add(item);
        }

        CollectionSync.Sync(Sensors, desired);
    }
}
