namespace Torrid.App.ViewModels;

using System.Collections.ObjectModel;
using System.Windows.Input;
using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

/// <summary>
/// The Sensors page: every sensor that is detected now, has saved preferences or was added by the user, grouped by device,
/// with live-applying name / Window / Tray / Alerts edits and the inline Add sensor panel. Lives as long as the app (held by
/// <see cref="AppHost"/>), so the event subscriptions below are never removed.
/// </summary>
public sealed class SensorsViewModel : ObservableObject
{
    private readonly ISensorSource _sensors;
    private readonly ConfigManager _config;
    private readonly Dictionary<string, SensorGroupViewModel> _groupsByKey = new();
    private readonly Dictionary<string, SensorListItemViewModel> _itemsById = new();
    private List<string> _rowKeys = new();

    public SensorsViewModel(ISensorSource sensors, ConfigManager config, ISensorCatalogSource? catalog = null)
    {
        _sensors = sensors;
        _config = config;

        AddSensor = new AddSensorViewModel(catalog, config, () => _sensors.CurrentReadings);
        ResetToDefaultsCommand = new RelayCommand(_ => _config.Update(ResetToDefaults));

        // Readings arrive on the poll thread; always hop to the UI thread before touching bound collections.
        _sensors.ReadingsUpdated += (_, _) => UiThread.Post(OnReadingsUpdated);
        _config.Changed += (_, _) => UiThread.Post(Rebuild);

        Rebuild();
    }

    public ObservableCollection<SensorGroupViewModel> Groups { get; } = new();

    /// <summary>The inline "Add a sensor" panel shown above the list.</summary>
    public AddSensorViewModel AddSensor { get; }

    public ICommand ResetToDefaultsCommand { get; }

    /// <summary>
    /// Clears every name and switch, but keeps added sensors in the window (adding one switches it on), so a reset
    /// never silently hides them from Home. Their names and tray / alert switches are cleared like the rest.
    /// </summary>
    private static void ResetToDefaults(AppConfig config)
    {
        config.Sensors = new Dictionary<string, SensorPreference>();
        foreach (var (id, info) in config.AddedSensors ?? new Dictionary<string, AddedSensor>())
        {
            if (info is not null && SensorIdentity.IsGenericId(id)) SensorPreferences.Update(config, id, p => p.ShowInWindow = true);
        }
    }

    private sealed record Row(SensorIdentity Identity, DeviceGroup Group, string? HardwareName)
    {
        /// <summary>Identifies the row and its placement: a change means a rebuild rather than a value refresh.</summary>
        public string Key => Identity.Id + "|" + Group.Key;
    }

    private void OnReadingsUpdated()
    {
        var readings = CurrentReadings();
        if (DesiredRows(readings).Select(r => r.Key).SequenceEqual(_rowKeys))
        {
            // Same rows: only refresh the live values on the existing instances.
            UpdateLiveValues(readings);
        }
        else
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        var config = _config.Config;
        var readings = CurrentReadings();
        var rows = DesiredRows(readings);

        var desiredGroups = new List<SensorGroupViewModel>();
        foreach (var device in rows.GroupBy(r => r.Group))
        {
            var key = device.Key.Key;
            if (!_groupsByKey.TryGetValue(key, out var group))
            {
                group = new SensorGroupViewModel(key, device.Key.Title);
                _groupsByKey[key] = group;
            }

            var desiredItems = new List<SensorListItemViewModel>();
            foreach (var row in device)
            {
                var identity = row.Identity;
                if (!_itemsById.TryGetValue(identity.Id, out var item) || item.Identity != identity)
                {
                    // A changed identity (e.g. a re-added sensor with another kind) needs a fresh row.
                    item = new SensorListItemViewModel(identity, _config, AddSensor.Remove);
                    _itemsById[identity.Id] = item;
                }

                item.RefreshPreferences(config);
                desiredItems.Add(item);
            }

            CollectionSync.Sync(group.Items, desiredItems);
            desiredGroups.Add(group);
        }

        CollectionSync.Sync(Groups, desiredGroups);

        foreach (var stale in _groupsByKey.Keys.Except(desiredGroups.Select(g => g.Key)).ToList()) _groupsByKey.Remove(stale);
        foreach (var stale in _itemsById.Keys.Except(rows.Select(r => r.Identity.Id)).ToList()) _itemsById.Remove(stale);

        _rowKeys = rows.Select(r => r.Key).ToList();
        UpdateLiveValues(readings);
    }

    private void UpdateLiveValues(IReadOnlyList<SensorReading> readings)
    {
        var config = _config.Config;
        var byId = readings.DistinctBy(r => r.Id).ToDictionary(r => r.Id);

        foreach (var group in Groups)
        {
            group.Subtitle = DeviceGroups.Subtitle(group.Items.Select(i => byId.GetValueOrDefault(i.Id)?.HardwareName ?? StoredHardwareName(config, i.Id)));

            foreach (var item in group.Items)
            {
                config.Thresholds.TryGetValue(item.Identity.Kind, out var band);
                item.UpdateReading(byId.GetValueOrDefault(item.Id), band);
            }
        }
    }

    /// <summary>
    /// Detected ids, configured ids and added ids that <see cref="SensorDisplay.Identity"/> accepts (a <c>hw:</c> id only
    /// while it is in <see cref="AppConfig.AddedSensors"/>, so garbage keys and just-removed sensors get no row);
    /// ordered CPU, GPUs by index, Motherboard, Memory, Storage, Other, then by kind and name.
    /// </summary>
    private List<Row> DesiredRows(IReadOnlyList<SensorReading> readings)
    {
        var config = _config.Config;
        var identities = new Dictionary<string, SensorIdentity>();
        var hardwareNames = new Dictionary<string, string?>();

        foreach (var reading in readings)
        {
            if (reading is null || identities.ContainsKey(reading.Id)) continue;

            var identity = SensorIdentity.IsGenericId(reading.Id)
                ? SensorDisplay.Identity(config, reading.Id)
                : SensorIdentity.FromReading(reading);
            if (identity is null) continue;

            identities[reading.Id] = identity;
            hardwareNames[reading.Id] = reading.HardwareName;
        }

        foreach (var key in config.Sensors.Keys)
        {
            if (!identities.ContainsKey(key) && SensorDisplay.Identity(config, key) is { } parsed) identities[key] = parsed;
        }

        foreach (var key in (config.AddedSensors ?? new Dictionary<string, AddedSensor>()).Keys)
        {
            if (!identities.ContainsKey(key) && SensorIdentity.IsGenericId(key) && SensorDisplay.Identity(config, key) is { } added)
            {
                identities[key] = added;
            }
        }

        var gpuNames = DeviceGroups.GpuNames(readings.Where(r => r is not null));
        return identities.Values
            .Select(identity =>
            {
                var hardware = hardwareNames.GetValueOrDefault(identity.Id) ?? StoredHardwareName(config, identity.Id);
                return new Row(identity, DeviceGroups.For(identity.Kind, identity.GpuIndex, hardware, gpuNames), hardware);
            })
            .OrderBy(r => r.Group.Rank)
            .ThenBy(r => r.Group.Index)
            .ThenBy(r => r.Identity.Kind)
            .ThenBy(r => r.HardwareName ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Identity.DefaultLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Identity.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static string? StoredHardwareName(AppConfig config, string id) =>
        SensorIdentity.IsGenericId(id) ? config.AddedSensors?.GetValueOrDefault(id)?.HardwareName : null;

    private IReadOnlyList<SensorReading> CurrentReadings() => _sensors.CurrentReadings ?? Array.Empty<SensorReading>();
}
