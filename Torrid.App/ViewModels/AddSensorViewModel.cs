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

    private readonly ISensorCatalogSource? _catalog;
    private readonly ConfigManager _config;
    private readonly RelayCommand _scanCommand;
    private bool _isOpen;
    private bool _isScanning;
    private string? _message;

    public AddSensorViewModel(ISensorCatalogSource? catalog, ConfigManager config)
    {
        _catalog = catalog;
        _config = config;

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
        var groups = (result.Sensors ?? Array.Empty<CatalogSensor>())
            .Where(s => s is not null && SensorIdentity.IsGenericId(s.Id))
            .GroupBy(s => s.HardwareName ?? "")
            .Select(g => new AddSensorGroupViewModel(
                string.IsNullOrWhiteSpace(g.Key) ? "Unknown hardware" : g.Key,
                g.Select(s => new AddSensorItemViewModel(s, added.ContainsKey(s.Id), Add))))
            .ToList();
        foreach (var group in groups) Groups.Add(group);

        Message = groups.Count > 0 ? null : result.UnavailableReason ?? NothingFoundMessage;
    }

    /// <summary>Remembers the sensor and shows it in the window. Adding an already-added sensor changes nothing.</summary>
    public void Add(AddSensorItemViewModel item)
    {
        var sensor = item.Sensor;
        if (!SensorIdentity.IsGenericId(sensor.Id)) return;

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
