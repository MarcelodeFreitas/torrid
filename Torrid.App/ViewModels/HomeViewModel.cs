namespace Torrid.App.ViewModels;

using System.Collections.ObjectModel;
using Torrid.App.Drivers;
using Torrid.Core.Config;
using Torrid.Core.Drivers;
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
    private readonly PawnIoStatusTracker _pawnIo;
    private string? _statusMessage;
    private string? _driverMessage;
    private bool _isEmpty = true;
    private string _emptyMessage = NoReadingsMessage;

    public HomeViewModel(ISensorSource sensors, IHardwareStatusSource hardware, ConfigManager config, PawnIoStatusTracker pawnIo)
    {
        _sensors = sensors;
        _hardware = hardware;
        _config = config;
        _pawnIo = pawnIo;

        // Sensor events arrive on the poll thread and config changes on whichever thread edited it:
        // always hop to the UI thread (non-blocking) before touching bound collections.
        _sensors.ReadingsUpdated += (_, _) => UiThread.Post(Apply);
        _hardware.StatusChanged += (_, _) => UiThread.Post(UpdateStatus);
        _config.Changed += (_, _) => UiThread.Post(Apply);
        _pawnIo.Changed += (_, _) => UiThread.Post(UpdateStatus);

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

    public const string PawnIoMissingMessage = "CPU temperature needs the PawnIO driver — see Settings.";

    /// <summary>
    /// A quiet second banner line, only when PawnIO is not installed and monitoring is not paused. Deliberately silent for
    /// Outdated/Unknown/Current (Settings explains those) and separate from the "not elevated" CPU-unavailable case.
    /// </summary>
    public string? DriverMessage
    {
        get => _driverMessage;
        private set
        {
            if (Set(ref _driverMessage, value)) Raise(nameof(HasDriverMessage));
        }
    }

    public bool HasDriverMessage => _driverMessage is not null;

    public bool IsEmpty { get => _isEmpty; private set => Set(ref _isEmpty, value); }

    public string EmptyMessage { get => _emptyMessage; private set => Set(ref _emptyMessage, value); }

    public static string? DescribeStatus(HardwareStatus status)
    {
        if (status.AntiCheat is not { } match) return null;

        var name = match.DisplayName;
        return status.Level switch
        {
            HardwareAccessLevel.Paused =>
                $"Monitoring paused while {name} is running. Readings resume automatically when it closes.",
            HardwareAccessLevel.GpuOnly =>
                $"CPU temperature is hidden while {name} is running. GPU temperatures keep updating.",
            _ => null,
        };
    }

    private void UpdateStatus()
    {
        var status = _hardware.Status;
        StatusMessage = DescribeStatus(status);
        DriverMessage = DescribeDriver(_pawnIo.Status, status);
    }

    public static string? DescribeDriver(PawnIoStatus pawnIo, HardwareStatus hardware) =>
        pawnIo.State == PawnIoState.NotInstalled && hardware.Level != HardwareAccessLevel.Paused
            ? PawnIoMissingMessage
            : null;

    private void Apply()
    {
        UpdateStatus();

        var config = _config.Config;
        var readings = _sensors.CurrentReadings ?? Array.Empty<SensorReading>();

        // Built-in sensors group by GPU index (CPU otherwise); added sensors by kind ("Motherboard", "Storage", ...).
        var gpuNames = DeviceGroups.GpuNames(readings);
        var shownGroups = readings
            .DistinctBy(r => r.Id)
            .Where(r => SensorDisplay.IsShownInWindow(config, r))
            .GroupBy(r => DeviceGroups.For(r.Kind, r.GpuIndex, r.HardwareName, gpuNames))
            .OrderBy(g => g.Key.Rank)
            .ThenBy(g => g.Key.Index)
            .ToList();

        var desiredGroups = new List<DeviceGroupViewModel>();
        var liveCardIds = new HashSet<string>();

        foreach (var readingsInGroup in shownGroups)
        {
            var key = readingsInGroup.Key.Key;
            if (!_groupsByKey.TryGetValue(key, out var group))
            {
                group = new DeviceGroupViewModel(key, readingsInGroup.Key.Title);
                _groupsByKey[key] = group;
            }

            var ordered = readingsInGroup
                .OrderBy(r => r.Kind)
                .ThenBy(r => r.HardwareName ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
            group.Subtitle = DeviceGroups.Subtitle(ordered.Select(r => r.HardwareName));

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

    private static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> desired) where T : class =>
        CollectionSync.Sync(target, desired);
}
