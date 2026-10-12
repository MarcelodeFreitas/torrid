namespace Torrid.App.ViewModels;

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

/// <summary>
/// The inline "Add a sensor" panel on the Sensors page. It never scans by itself: the user presses Scan, the scan runs
/// off the UI thread through <see cref="ISensorCatalogSource"/>, and the result is applied back on the UI thread.
/// Adding / removing writes through <see cref="ConfigManager.Update"/>, always REPLACING <see cref="AppConfig.AddedSensors"/>
/// (the poll thread reads that dictionary by reference). App-lifetime (owned by <see cref="SensorsViewModel"/>), so the
/// config subscription is never removed.
/// </summary>
public sealed class AddSensorViewModel : ObservableObject
{
    public const string NothingFoundMessage = "No temperature sensors found.";
    public const string CancelledMessage = "The scan was cancelled.";
    public const string NoCatalogMessage = "Scanning for sensors isn't available.";

    /// <summary>The kinds of the built-in cards; a catalog entry they already read is shown as "Already shown".</summary>
    private static readonly SensorKind[] BuiltInKinds =
    {
        SensorKind.CpuPackage, SensorKind.GpuCore, SensorKind.GpuHotSpot, SensorKind.GpuVram,
    };

    private readonly ISensorCatalogSource? _catalog;
    private readonly ConfigManager _config;
    private readonly Func<IReadOnlyList<SensorReading>?> _currentReadings;
    private readonly RelayCommand _scanCommand;
    private bool _isOpen;
    private bool _isScanning;
    private string? _message;

    /// <param name="currentReadings">The live readings, used to recognise sensors the built-in cards already show.</param>
    public AddSensorViewModel(
        ISensorCatalogSource? catalog, ConfigManager config, Func<IReadOnlyList<SensorReading>?>? currentReadings = null)
    {
        _catalog = catalog;
        _config = config;
        _currentReadings = currentReadings ?? (() => Array.Empty<SensorReading>());

        OpenCommand = new RelayCommand(_ => IsOpen = true);
        CloseCommand = new RelayCommand(_ => Close());
        _scanCommand = new RelayCommand(_ => _ = ScanAsync(), _ => !IsScanning);

        // Removing a sensor on the list (or any other config edit) must flip the matching result back to "Add".
        _config.Changed += (_, _) => UiThread.Post(RefreshAddedState);
    }

    public bool IsOpen
    {
        get => _isOpen;
        private set => Set(ref _isOpen, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (!Set(ref _isScanning, value)) return;
            Raise(nameof(ScanButtonText));
            _scanCommand.RaiseCanExecuteChanged();
        }
    }

    public string ScanButtonText => IsScanning ? "Scanning…" : "Scan for sensors";

    /// <summary>Why there are no results (scan refused or failed, or nothing found); null otherwise.</summary>
    public string? Message
    {
        get => _message;
        private set
        {
            if (Set(ref _message, value)) Raise(nameof(HasMessage));
        }
    }

    public bool HasMessage => _message is not null;

    public ObservableCollection<AddSensorGroupViewModel> Groups { get; } = new();

    public ICommand OpenCommand { get; }

    public ICommand CloseCommand { get; }

    public ICommand ScanCommand => _scanCommand;

    /// <summary>Scans once. Ignored while a scan is already running; a result that arrives after Close is dropped.</summary>
    public async Task ScanAsync()
    {
        if (IsScanning) return;

        IsScanning = true;
        SensorCatalogResult? result;
        string? error = null;
        try
        {
            // Task.Run guarantees the UI thread never runs any of the scan, whatever the implementation does; the
            // await resumes on the UI thread (WPF's synchronisation context), where the results are applied.
            var catalog = _catalog;
            result = catalog is null
                ? SensorCatalogResult.Unavailable(NoCatalogMessage)
                : await Task.Run(() => catalog.ScanAsync());
        }
        catch (OperationCanceledException)
        {
            result = null;
            error = CancelledMessage;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AddSensorViewModel: scan failed: {ex}");
            result = null;
            error = $"Couldn't scan hardware: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }

        // Closing the panel discards results, including one still in flight.
        if (!IsOpen) return;

        Groups.Clear();
        if (result is null)
        {
            Message = error;
            return;
        }

        var added = _config.Config.AddedSensors ?? new Dictionary<string, AddedSensor>();
        var builtInSources = BuiltInSources();
        var groups = (result.Sensors ?? Array.Empty<CatalogSensor>())
            .Where(s => s is not null && (SensorIdentity.IsGenericId(s.Id) || SensorIdentity.TryParse(s.Id) is not null))
            .GroupBy(s => s.HardwareName ?? "")
            .Select(g => new AddSensorGroupViewModel(
                string.IsNullOrWhiteSpace(g.Key) ? "Unknown hardware" : g.Key,
                g.Select(s => new AddSensorItemViewModel(s, added.ContainsKey(s.Id), Add, IsBuiltIn(s, builtInSources)))))
            .ToList();
        foreach (var group in groups) Groups.Add(group);

        Message = groups.Count > 0 ? null : result.UnavailableReason ?? NothingFoundMessage;
    }

    /// <summary>Remembers the sensor and shows it in the window. Adding an already-added sensor changes nothing.</summary>
    public void Add(AddSensorItemViewModel item)
    {
        var sensor = item.Sensor;
        if (item.IsBuiltIn || !SensorIdentity.IsGenericId(sensor.Id)) return;

        if (_config.Config.AddedSensors?.ContainsKey(sensor.Id) == true)
        {
            item.IsAdded = true;
            return;
        }

        _config.Update(c =>
        {
            // Replace, never mutate: the poll thread holds the previous dictionary by reference.
            c.AddedSensors = new Dictionary<string, AddedSensor>(c.AddedSensors ?? new Dictionary<string, AddedSensor>())
            {
                [sensor.Id] = new AddedSensor { Name = sensor.Name, HardwareName = sensor.HardwareName, Kind = sensor.Kind },
            };
            SensorPreferences.Update(c, sensor.Id, p => p.ShowInWindow = true);
        });
        item.IsAdded = true;
    }

    /// <summary>Forgets an added sensor and clears its preferences (name, switches). Unknown ids change nothing.</summary>
    public void Remove(string id)
    {
        if (!SensorIdentity.IsGenericId(id)) return;

        var config = _config.Config;
        var isAdded = config.AddedSensors?.ContainsKey(id) == true;
        if (!isAdded && !config.Sensors.ContainsKey(id)) return;

        _config.Update(c =>
        {
            if (c.AddedSensors is { } current && current.ContainsKey(id))
            {
                var replacement = new Dictionary<string, AddedSensor>(current);
                replacement.Remove(id);
                c.AddedSensors = replacement;
            }

            c.Sensors.Remove(id);
        });
        RefreshAddedState();
    }

    /// <summary>(hardware name, source sensor name) of every built-in card reading right now.</summary>
    private HashSet<(string Hardware, string Sensor)> BuiltInSources()
    {
        var sources = new HashSet<(string, string)>();
        IReadOnlyList<SensorReading>? readings;
        try
        {
            readings = _currentReadings();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AddSensorViewModel: reading the current sensors failed: {ex}");
            readings = null;
        }

        foreach (var reading in readings ?? Array.Empty<SensorReading>())
        {
            if (reading is null || SensorIdentity.IsGenericId(reading.Id) || !BuiltInKinds.Contains(reading.Kind)) continue;
            if (reading.HardwareName is { } hardware && reading.SourceSensorName is { } source) sources.Add((hardware, source));
        }

        return sources;
    }

    /// <summary>A built-in id, or the very sensor a built-in card reads (same hardware and sensor name).</summary>
    private static bool IsBuiltIn(CatalogSensor sensor, HashSet<(string Hardware, string Sensor)> builtInSources) =>
        !SensorIdentity.IsGenericId(sensor.Id) || builtInSources.Contains((sensor.HardwareName ?? "", sensor.Name ?? ""));

    private void Close()
    {
        IsOpen = false;
        Groups.Clear();
        Message = null;
    }

    private void RefreshAddedState()
    {
        var added = _config.Config.AddedSensors ?? new Dictionary<string, AddedSensor>();
        foreach (var item in Groups.SelectMany(g => g.Items)) item.IsAdded = added.ContainsKey(item.Id);
    }
}
