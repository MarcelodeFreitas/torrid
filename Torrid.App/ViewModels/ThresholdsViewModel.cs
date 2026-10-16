namespace Torrid.App.ViewModels;

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

/// <summary>
/// The Thresholds page: every sensor of the Sensors page's row set, grouped by device the same way. Each sensor has its own
/// limits (Default until customised); sensors that are shown nowhere sit in a collapsed "Not shown" section at the end.
/// Below them, the defaults by sensor type. App-lifetime singleton, so the subscriptions are never removed.
/// </summary>
public sealed class ThresholdsViewModel : ObservableObject
{
    public const string NotShownTitle = "Not shown";

    private const string NotShownKey = "not-shown";

    private readonly ISensorSource _sensors;
    private readonly ConfigManager _config;
    private readonly ISensorCatalogSource? _catalog;
    private readonly RelayCommand _scanCommand;
    private readonly Dictionary<string, ThresholdGroupViewModel> _groupsByKey = new();
    private readonly Dictionary<string, SensorThresholdViewModel> _itemsById = new();
    private readonly Dictionary<string, SensorThresholdViewModel> _advancedById = new();
    private readonly Dictionary<string, ThresholdGroupViewModel> _advancedGroupsByKey = new();
    private List<string> _layoutKeys = new();
    private List<CatalogSensor> _catalogSensors = new();
    private string _searchText = "";
    private string? _focusedSensorId;
    private string? _pendingFocusId;
    private bool _isAdvancedExpanded;
    private bool _isScanning;
    private string? _scanMessage;

    public ThresholdsViewModel(ISensorSource sensors, ConfigManager config, ISensorCatalogSource? catalog = null)
    {
        _sensors = sensors;
        _config = config;
        _catalog = catalog;
        _scanCommand = new RelayCommand(_ => _ = ScanAsync(), _ => !IsScanning);
        DefaultRows = new ObservableCollection<ThresholdRowViewModel>(
            Enum.GetValues<SensorKind>().Select(kind => new ThresholdRowViewModel(kind, Title(kind), config)));

        // Readings arrive on the poll thread; always hop to the UI thread before touching bound collections.
        _sensors.ReadingsUpdated += (_, _) => UiThread.Post(OnReadingsUpdated);
        _config.Changed += (_, _) => UiThread.Post(OnConfigChanged);

        Sync(forceRebuild: true);
    }

    /// <summary>The device sections (in the Sensors page order), then the "Not shown" section when it has sensors.</summary>
    public ObservableCollection<ThresholdGroupViewModel> Groups { get; } = new();

    public const string AllListedMessage = "Every temperature sensor found is already on your Sensors page.";

    /// <summary>Device sections of catalog sensors that have no row above (empty until a scan).</summary>
    public ObservableCollection<ThresholdGroupViewModel> AdvancedGroups { get; } = new();

    /// <summary>The Advanced section is collapsed at first; a search that matches inside it opens it. In-memory only.</summary>
    public bool IsAdvancedExpanded { get => _isAdvancedExpanded; set => Set(ref _isAdvancedExpanded, value); }

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

    public bool HasScanned { get; private set; }

    /// <summary>Why the last scan gave no new list (refused, failed or nothing found); null otherwise.</summary>
    public string? ScanMessage
    {
        get => _scanMessage;
        private set
        {
            if (!Set(ref _scanMessage, value)) return;
            Raise(nameof(HasScanMessage));
            Raise(nameof(ShowAllListed));
        }
    }

    public bool HasScanMessage => ScanMessage is not null;

    public string ScanButtonText => IsScanning ? "Scanning…" : HasScanned ? "Scan again" : "Scan for sensors";

    /// <summary>True when a scan succeeded but every sensor it found already has a row above.</summary>
    public bool ShowAllListed => HasScanned && ScanMessage is null && AdvancedGroups.Count == 0;

    public ICommand ScanCommand => _scanCommand;

    /// <summary>The ten defaults by sensor type: what sensors follow until they are customised.</summary>
    public ObservableCollection<ThresholdRowViewModel> DefaultRows { get; }

    /// <summary>The compact list view instead of the cards; persisted as <see cref="AppConfig.ThresholdsListView"/>.</summary>
    public bool IsListView
    {
        get => _config.Config.ThresholdsListView;
        set
        {
            if (_config.Config.ThresholdsListView == value) return;

            _config.Update(c => c.ThresholdsListView = value);
            Raise(nameof(IsListView));
        }
    }

    /// <summary>
    /// Case-insensitive match on the sensor's name, its default name, caption, hardware name and device; blank shows
    /// everything. Never recreates rows.
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!Set(ref _searchText, value ?? "")) return;

            ApplyFilter(expandGroups: true);
        }
    }

    /// <summary>The sensor the page was last asked to focus (the view scrolls to it and focuses its first box).</summary>
    public string? FocusedSensorId
    {
        get => _focusedSensorId;
        private set => _focusedSensorId = value;
    }

    /// <summary>
    /// Expands the section holding the sensor, clears a search that would hide it and raises <see cref="FocusedSensorId"/>
    /// (raised even when asked for the same sensor again, so the view can focus it again). Unknown ids change nothing.
    /// </summary>
    public void Focus(string sensorId)
    {
        if (!_itemsById.TryGetValue(sensorId, out var target)) return;

        if (!target.IsVisible) SearchText = "";

        if (Groups.FirstOrDefault(g => g.Items.Contains(target)) is { } group) group.IsExpanded = true;

        FocusedSensorId = sensorId;
        _pendingFocusId = sensorId;
        Raise(nameof(FocusedSensorId));
    }

    /// <summary>
    /// The sensor a <see cref="Focus"/> request is waiting for the page to show, returned once. A page created after the
    /// request (navigated to from the Sensors page) takes it on load; a page already showing it takes it on the change.
    /// </summary>
    public string? TakeFocusRequest()
    {
        var id = _pendingFocusId;
        _pendingFocusId = null;
        return id;
    }

    /// <summary>
    /// Scans once for the Advanced section; only the button calls this. A refused or failed scan keeps the previous list and
    /// shows why. A scan that is already running is not started again.
    /// </summary>
    public async Task ScanAsync()
    {
        if (IsScanning) return;

        IsScanning = true;
        SensorCatalogResult? result;
        string? error = null;
        try
        {
            // Task.Run keeps the hardware work off the UI thread; the await resumes on the UI thread.
            var catalog = _catalog;
            result = catalog is null
                ? SensorCatalogResult.Unavailable(AddSensorViewModel.NoCatalogMessage)
                : await Task.Run(() => catalog.ScanAsync());
        }
        catch (OperationCanceledException)
        {
            result = null;
            error = AddSensorViewModel.CancelledMessage;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ThresholdsViewModel: scan failed: {ex}");
            result = null;
            error = $"Couldn't scan hardware: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }

        if (result is null)
        {
            ScanMessage = error;
            return;
        }

        var found = (result.Sensors ?? Array.Empty<CatalogSensor>())
            .Where(s => s is not null && SensorIdentity.IsGenericId(s.Id))
            .ToList();
        if (found.Count == 0)
        {
            ScanMessage = result.UnavailableReason ?? AddSensorViewModel.NothingFoundMessage;
            return;
        }

        _catalogSensors = found;
        HasScanned = true;
        ScanMessage = null;
        Raise(nameof(ScanButtonText));
        SyncAdvanced();
    }

    public static string Title(SensorKind kind) => kind switch
    {
        SensorKind.CpuPackage => "CPU temperature",
        SensorKind.GpuCore => "GPU core",
        SensorKind.GpuHotSpot => "GPU hot spot",
        SensorKind.GpuVram => "GPU memory junction (VRAM)",
        SensorKind.CpuOther => "Other CPU sensors",
        SensorKind.GpuOther => "Other GPU sensors",
        SensorKind.Motherboard => "Motherboard / VRM",
        SensorKind.Storage => "Storage",
        SensorKind.Memory => "Memory",
        SensorKind.OtherTemp => "Other sensors",
        _ => kind.ToString(),
    };

    private void OnReadingsUpdated() => Sync(forceRebuild: false);

    private void OnConfigChanged()
    {
        Raise(nameof(IsListView));
        foreach (var row in DefaultRows) row.Refresh();
        Sync(forceRebuild: true);
    }

    /// <summary>
    /// Brings the sections and rows in line with the readings and config. A tick that leaves the layout unchanged only
    /// refreshes the live values; otherwise the rows are placed again (existing instances are kept and moved).
    /// </summary>
    private void Sync(bool forceRebuild)
    {
        var config = _config.Config;
        var readings = CurrentReadings();
        var rows = SensorRows.Build(config, readings);
        var layout = LayoutKeys(config, rows);
        if (!forceRebuild && layout.SequenceEqual(_layoutKeys))
        {
            UpdateLiveValues(readings);
            return;
        }

        var placed = new List<(ThresholdGroupViewModel Group, List<SensorThresholdViewModel> Items)>();
        var membersByKey = new Dictionary<string, List<SensorThresholdViewModel>>();
        foreach (var row in rows)
        {
            var item = ItemFor(row);
            item.DeviceTitle = row.Group.Title;
            item.RefreshFromConfig();

            var shown = IsShownAnywhere(config, row.Identity);
            var key = shown ? row.Group.Key : NotShownKey;
            if (!membersByKey.TryGetValue(key, out var members))
            {
                members = new List<SensorThresholdViewModel>();
                membersByKey[key] = members;
                placed.Add((GroupFor(key, shown ? row.Group.Title : NotShownTitle, isNotShown: !shown), members));
            }

            members.Add(item);
        }

        // The "Not shown" section always goes last; the stable sort keeps the device order.
        placed = placed.OrderBy(p => p.Group.IsNotShown).ToList();

        foreach (var (group, items) in placed) CollectionSync.Sync(group.Items, items);
        CollectionSync.Sync(Groups, placed.Select(p => p.Group).ToList());

        // Sections with no sensors any more keep their state but leave the page.
        foreach (var (key, group) in _groupsByKey)
        {
            if (!membersByKey.ContainsKey(key)) group.Items.Clear();
        }

        foreach (var stale in _itemsById.Keys.Except(rows.Select(r => r.Identity.Id)).ToList()) _itemsById.Remove(stale);

        _layoutKeys = layout;
        SyncAdvanced();
        UpdateLiveValues(readings);
    }

    /// <summary>
    /// Builds the Advanced sections from the last scan: the catalog sensors that have no row above, grouped by device. Their
    /// scan values are shown as they were read; only the next scan changes them. Instances are kept across rebuilds.
    /// </summary>
    private void SyncAdvanced()
    {
        var readings = CurrentReadings().Where(r => r is not null).ToList();
        var builtIn = SensorRows.BuiltInSources(readings);
        var gpuNames = DeviceGroups.GpuNames(readings);
        var wanted = _catalogSensors
            .Where(s => !_itemsById.ContainsKey(s.Id) && !SensorRows.IsBuiltIn(s, builtIn))
            .Select(s => (Sensor: s, Group: DeviceGroups.For(s.Kind, null, s.HardwareName, gpuNames)))
            .OrderBy(w => w.Group.Rank)
            .ThenBy(w => w.Group.Index)
            .ToList();

        var items = new List<(DeviceGroup Group, SensorThresholdViewModel Item)>();
        foreach (var (sensor, group) in wanted)
        {
            var identity = new SensorIdentity(sensor.Id, sensor.Name, sensor.Kind, null);
            if (!_advancedById.TryGetValue(sensor.Id, out var item) || item.Identity != identity)
            {
                item = new SensorThresholdViewModel(identity, _config);
                _advancedById[sensor.Id] = item;
            }

            item.DeviceTitle = group.Title;
            item.RefreshFromConfig();
            item.UpdateReading(new SensorReading(sensor.Id, sensor.Name, sensor.Kind, null, sensor.ValueCelsius, sensor.HardwareName, sensor.Name));
            items.Add((group, item));
        }

        var placed = new List<ThresholdGroupViewModel>();
        foreach (var byDevice in items.GroupBy(i => i.Group.Key))
        {
            if (!_advancedGroupsByKey.TryGetValue(byDevice.Key, out var group))
            {
                group = new ThresholdGroupViewModel(byDevice.Key, byDevice.First().Group.Title);
                _advancedGroupsByKey[byDevice.Key] = group;
            }

            var members = byDevice.Select(i => i.Item).ToList();
            CollectionSync.Sync(group.Items, members);
            group.Subtitle = DeviceGroups.Subtitle(members.Select(i => i.HardwareName));
            placed.Add(group);
        }

        CollectionSync.Sync(AdvancedGroups, placed);
        foreach (var group in _advancedGroupsByKey.Values)
        {
            if (!placed.Contains(group)) group.Items.Clear();
        }

        foreach (var stale in _advancedById.Keys.Except(wanted.Select(w => w.Sensor.Id)).ToList()) _advancedById.Remove(stale);

        ApplyFilter(expandGroups: false);
        Raise(nameof(ShowAllListed));
    }

    /// <summary>The instance for this sensor, kept while its identity is unchanged.</summary>
    private SensorThresholdViewModel ItemFor(SensorRow row)
    {
        if (!_itemsById.TryGetValue(row.Identity.Id, out var item) || item.Identity != row.Identity)
        {
            item = new SensorThresholdViewModel(row.Identity, _config);
            _itemsById[row.Identity.Id] = item;
        }

        return item;
    }

    private ThresholdGroupViewModel GroupFor(string key, string title, bool isNotShown)
    {
        if (!_groupsByKey.TryGetValue(key, out var group))
        {
            group = new ThresholdGroupViewModel(key, title, isNotShown);
            _groupsByKey[key] = group;
        }

        return group;
    }

    private void UpdateLiveValues(IReadOnlyList<SensorReading> readings)
    {
        var config = _config.Config;
        var byId = SensorRows.ById(readings);

        foreach (var item in _itemsById.Values) item.UpdateReading(byId.GetValueOrDefault(item.Id));

        foreach (var group in Groups)
        {
            if (!group.IsNotShown) group.Subtitle = SensorRows.Subtitle(config, group.Items.Select(i => i.Id), byId);
        }

        // Caption and value can change the match, so the filter is re-applied; it never expands or recreates anything.
        ApplyFilter(expandGroups: false);
    }

    /// <summary>Only changes visibility flags. With a search active, a section with matches is opened so they can be seen.</summary>
    private void ApplyFilter(bool expandGroups)
    {
        var query = _searchText.Trim();
        foreach (var item in _itemsById.Values.Concat(_advancedById.Values)) item.IsVisible = Matches(item, query);

        foreach (var group in Groups.Concat(AdvancedGroups))
        {
            var any = group.Items.Any(i => i.IsVisible);
            group.IsVisible = query.Length == 0 || any;
            if (expandGroups && query.Length > 0 && any) group.IsExpanded = true;
        }

        // A match inside the Advanced section opens it, so the match can be seen.
        if (expandGroups && query.Length > 0 && AdvancedGroups.Any(g => g.Items.Any(i => i.IsVisible))) IsAdvancedExpanded = true;
    }

    private static bool Matches(SensorThresholdViewModel item, string query) =>
        query.Length == 0
        || item.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
        || item.Identity.DefaultLabel.Contains(query, StringComparison.OrdinalIgnoreCase)
        || item.Caption.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (item.HardwareName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        || item.DeviceTitle.Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>A sensor is shown when any of Window, Tray or Alerts is on; otherwise it sits in "Not shown".</summary>
    private static bool IsShownAnywhere(AppConfig config, SensorIdentity sensor) =>
        SensorDisplay.IsShownInWindow(config, sensor)
        || SensorDisplay.IsShownInTray(config, sensor)
        || SensorDisplay.NotificationsEnabled(config, sensor);

    /// <summary>Identifies each row and its section, so a change in either means the rows must be placed again.</summary>
    private static List<string> LayoutKeys(AppConfig config, IEnumerable<SensorRow> rows) =>
        rows.Select(r => r.Identity.Id + "|" + (IsShownAnywhere(config, r.Identity) ? r.Group.Key : NotShownKey)).ToList();

    private IReadOnlyList<SensorReading> CurrentReadings() => _sensors.CurrentReadings ?? Array.Empty<SensorReading>();
}
