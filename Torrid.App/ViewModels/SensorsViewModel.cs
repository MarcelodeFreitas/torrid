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
    private readonly Action<string>? _showLimits;
    private List<string> _rowKeys = new();

    /// <param name="showLimits">Called with a sensor's id when its row's Limits link is used.</param>
    public SensorsViewModel(
        ISensorSource sensors, ConfigManager config, ISensorCatalogSource? catalog = null, Action<string>? showLimits = null)
    {
        _sensors = sensors;
        _config = config;
        _showLimits = showLimits;

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
    /// A sensor's own limits (<see cref="SensorPreference.Thresholds"/>) are kept; they are reset on the Thresholds page.
    /// </summary>
    private static void ResetToDefaults(AppConfig config)
    {
        var kept = new Dictionary<string, SensorPreference>();
        foreach (var (id, preference) in config.Sensors)
        {
            if (preference?.Thresholds is not null) kept[id] = new SensorPreference { Thresholds = preference.Thresholds };
        }

        config.Sensors = kept;
        foreach (var (id, info) in config.AddedSensors ?? new Dictionary<string, AddedSensor>())
        {
            if (info is not null && SensorIdentity.IsGenericId(id)) SensorPreferences.Update(config, id, p => p.ShowInWindow = true);
        }
    }

    private void OnReadingsUpdated()
    {
        var readings = CurrentReadings();
        if (SensorRows.Build(_config.Config, readings).Select(r => r.Key).SequenceEqual(_rowKeys))
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
        var rows = SensorRows.Build(config, readings);

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
                    item = new SensorListItemViewModel(identity, _config, AddSensor.Remove, _showLimits);
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
        var byId = SensorRows.ById(readings);

        foreach (var group in Groups)
        {
            group.Subtitle = SensorRows.Subtitle(config, group.Items.Select(i => i.Id), byId);

            foreach (var item in group.Items)
            {
                item.UpdateReading(byId.GetValueOrDefault(item.Id), ThresholdResolver.For(config, item.Identity));
            }
        }
    }

    private IReadOnlyList<SensorReading> CurrentReadings() => _sensors.CurrentReadings ?? Array.Empty<SensorReading>();
}
