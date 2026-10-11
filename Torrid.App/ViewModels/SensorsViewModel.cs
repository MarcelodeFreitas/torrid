namespace Torrid.App.ViewModels;

using System.Collections.ObjectModel;
using System.Windows.Input;
using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

/// <summary>
/// The Sensors page: every sensor that is detected now or has saved preferences, grouped by device, with live-applying
/// name / Window / Tray / Alerts edits. Lives as long as the app (held by <see cref="AppHost"/>), so the event
/// subscriptions below are never removed.
/// </summary>
public sealed class SensorsViewModel : ObservableObject
{
    private readonly ISensorSource _sensors;
    private readonly ConfigManager _config;
    private readonly Dictionary<string, SensorGroupViewModel> _groupsByKey = new();
    private readonly Dictionary<string, SensorListItemViewModel> _itemsById = new();
    private List<string> _rowIds = new();

    public SensorsViewModel(ISensorSource sensors, ConfigManager config)
    {
        _sensors = sensors;
        _config = config;

        ResetToDefaultsCommand = new RelayCommand(_ => _config.Update(c => c.Sensors = new Dictionary<string, SensorPreference>()));

        // Readings arrive on the poll thread; always hop to the UI thread before touching bound collections.
        _sensors.ReadingsUpdated += (_, _) => UiThread.Post(OnReadingsUpdated);
        _config.Changed += (_, _) => UiThread.Post(Rebuild);

        Rebuild();
    }

    public ObservableCollection<SensorGroupViewModel> Groups { get; } = new();

    public ICommand ResetToDefaultsCommand { get; }

    private void OnReadingsUpdated()
    {
        var readings = CurrentReadings();
        if (DesiredRows(readings).Select(i => i.Id).SequenceEqual(_rowIds))
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
        var ids = rows.Select(i => i.Id).ToList();

        var desiredGroups = new List<SensorGroupViewModel>();
        foreach (var device in rows.GroupBy(identity => identity.GpuIndex))
        {
            var key = device.Key is int index ? $"gpu{index}" : "cpu";
            if (!_groupsByKey.TryGetValue(key, out var group))
            {
                group = new SensorGroupViewModel(key, device.Key is int i ? $"GPU {i}" : "CPU");
                _groupsByKey[key] = group;
            }

            var desiredItems = new List<SensorListItemViewModel>();
            foreach (var identity in device)
            {
                if (!_itemsById.TryGetValue(identity.Id, out var item))
                {
                    item = new SensorListItemViewModel(identity, _config);
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
        foreach (var stale in _itemsById.Keys.Except(ids).ToList()) _itemsById.Remove(stale);

        _rowIds = ids;
        UpdateLiveValues(readings);
    }

    private void UpdateLiveValues(IReadOnlyList<SensorReading> readings)
    {
        var config = _config.Config;
        var byId = readings.DistinctBy(r => r.Id).ToDictionary(r => r.Id);

        foreach (var group in Groups)
        {
            group.Subtitle = readings.Where(r => r.GpuIndex == group.Items.FirstOrDefault()?.Identity.GpuIndex)
                .Select(r => r.HardwareName)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

            foreach (var item in group.Items)
            {
                config.Thresholds.TryGetValue(item.Identity.Kind, out var band);
                item.UpdateReading(byId.GetValueOrDefault(item.Id), band);
            }
        }
    }

    /// <summary>Detected ids plus every parsable configured id; CPU first, then GPUs by index, then kind order.</summary>
    private List<SensorIdentity> DesiredRows(IReadOnlyList<SensorReading> readings)
    {
        var identities = new Dictionary<string, SensorIdentity>();
        foreach (var reading in readings) identities.TryAdd(reading.Id, SensorIdentity.FromReading(reading));
        foreach (var key in _config.Config.Sensors.Keys)
        {
            if (!identities.ContainsKey(key) && SensorIdentity.TryParse(key) is { } parsed) identities[key] = parsed;
        }

        return identities.Values
            .OrderBy(i => i.GpuIndex ?? -1)
            .ThenBy(i => i.Kind)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .ToList();
    }

    private IReadOnlyList<SensorReading> CurrentReadings() => _sensors.CurrentReadings ?? Array.Empty<SensorReading>();
}
