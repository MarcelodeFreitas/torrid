namespace Torrid.App.ViewModels;

using System.Collections.ObjectModel;
using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

public sealed class HomeViewModel : ObservableObject
{
    public const string NothingSwitchedOnMessage = "No sensors are switched on. Open Sensors to choose what to show.";
    public const string NoReadingsMessage = "No temperature readings are available right now.";

    private readonly ISensorSource _sensors;
    private readonly IHardwareStatusSource _hardware;
    private readonly ConfigManager _config;
    private readonly Dictionary<string, DeviceGroupViewModel> _groupsByKey = new();
    private readonly Dictionary<string, SensorCardViewModel> _cardsById = new();
    private string? _statusMessage;
    private bool _isEmpty = true;
    private string _emptyMessage = NoReadingsMessage;

    public HomeViewModel(ISensorSource sensors, IHardwareStatusSource hardware, ConfigManager config)
    {
        _sensors = sensors;
        _hardware = hardware;
        _config = config;

        // Sensor events arrive on the poll thread and config changes on whichever thread edited it:
        // always hop to the UI thread (non-blocking) before touching bound collections.
        _sensors.ReadingsUpdated += (_, _) => UiThread.Post(Apply);
        _hardware.StatusChanged += (_, _) => UiThread.Post(UpdateStatus);
        _config.Changed += (_, _) => UiThread.Post(Apply);

        Apply();
    }

    public ObservableCollection<DeviceGroupViewModel> Groups { get; } = new();

    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (Set(ref _statusMessage, value)) Raise(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => _statusMessage is not null;

    public bool IsEmpty { get => _isEmpty; private set => Set(ref _isEmpty, value); }

    public string EmptyMessage { get => _emptyMessage; private set => Set(ref _emptyMessage, value); }

    public static string? DescribeStatus(HardwareStatus status)
    {
        if (status.AntiCheat is not { } match) return null;

        var name = match.Product == "Custom" ? match.ProcessName : match.Product;
        return status.Level switch
        {
            HardwareAccessLevel.Paused =>
                $"Monitoring paused while {name} is running. Readings resume automatically when it closes.",
            HardwareAccessLevel.GpuOnly =>
                $"CPU temperature is hidden while {name} is running. GPU temperatures keep updating.",
            _ => null,
        };
    }

    private void UpdateStatus() => StatusMessage = DescribeStatus(_hardware.Status);

    private void Apply()
    {
        UpdateStatus();

        var config = _config.Config;
        var readings = _sensors.CurrentReadings ?? Array.Empty<SensorReading>();

        var shownGroups = readings
            .DistinctBy(r => r.Id)
            .Where(r => SensorDisplay.IsShownInWindow(config, r))
            .GroupBy(r => r.GpuIndex)
            .OrderBy(g => g.Key ?? -1)
            .ToList();

        var desiredGroups = new List<DeviceGroupViewModel>();
        var liveCardIds = new HashSet<string>();

        foreach (var readingsInGroup in shownGroups)
        {
            var key = readingsInGroup.Key is int index ? $"gpu{index}" : "cpu";
            if (!_groupsByKey.TryGetValue(key, out var group))
            {
                var title = readingsInGroup.Key is int i ? $"GPU {i}" : "CPU";
                group = new DeviceGroupViewModel(key, title);
                _groupsByKey[key] = group;
            }

            var ordered = readingsInGroup.OrderBy(r => r.Kind).ToList();
            group.Subtitle = ordered.Select(r => r.HardwareName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

            var desiredCards = new List<SensorCardViewModel>();
            foreach (var reading in ordered)
            {
                if (!_cardsById.TryGetValue(reading.Id, out var card))
                {
                    card = new SensorCardViewModel(reading.Id);
                    _cardsById[reading.Id] = card;
                }

                config.Thresholds.TryGetValue(reading.Kind, out var band);
                card.Update(SensorDisplay.ResolveLabel(config, reading), reading.SourceSensorName, reading.ValueCelsius, band);
                desiredCards.Add(card);
                liveCardIds.Add(reading.Id);
            }

            Sync(group.Cards, desiredCards);
            desiredGroups.Add(group);
        }

        Sync(Groups, desiredGroups);

        foreach (var staleKey in _groupsByKey.Keys.Where(k => !desiredGroups.Any(g => g.Key == k)).ToList())
        {
            _groupsByKey.Remove(staleKey);
        }

        foreach (var staleId in _cardsById.Keys.Where(id => !liveCardIds.Contains(id)).ToList())
        {
            _cardsById.Remove(staleId);
        }

        IsEmpty = desiredGroups.Count == 0;
        EmptyMessage = readings.Count == 0 ? NoReadingsMessage : NothingSwitchedOnMessage;
    }

    /// <summary>Makes <paramref name="target"/> equal to <paramref name="desired"/> with minimal moves, keeping instances.</summary>
    private static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> desired) where T : class
    {
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(target[i])) target.RemoveAt(i);
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var existing = target.IndexOf(desired[i]);
            if (existing < 0)
            {
                target.Insert(i, desired[i]);
            }
            else if (existing != i)
            {
                target.Move(existing, i);
            }
        }
    }
}
