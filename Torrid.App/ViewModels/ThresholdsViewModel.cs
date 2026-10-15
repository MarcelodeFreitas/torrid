namespace Torrid.App.ViewModels;

using System.Collections.ObjectModel;
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
    private readonly Dictionary<string, ThresholdGroupViewModel> _groupsByKey = new();
    private readonly Dictionary<string, SensorThresholdViewModel> _itemsById = new();
    private List<string> _layoutKeys = new();
    private string _searchText = "";
    private string? _focusedSensorId;
    private string? _pendingFocusId;

    public ThresholdsViewModel(ISensorSource sensors, ConfigManager config)
    {
        _sensors = sensors;
        _config = config;
        DefaultRows = new ObservableCollection<ThresholdRowViewModel>(
            Enum.GetValues<SensorKind>().Select(kind => new ThresholdRowViewModel(kind, Title(kind), config)));

        // Readings arrive on the poll thread; always hop to the UI thread before touching bound collections.
        _sensors.ReadingsUpdated += (_, _) => UiThread.Post(OnReadingsUpdated);
        _config.Changed += (_, _) => UiThread.Post(OnConfigChanged);

        Sync(forceRebuild: true);
    }

    /// <summary>The device sections (in the Sensors page order), then the "Not shown" section when it has sensors.</summary>
    public ObservableCollection<ThresholdGroupViewModel> Groups { get; } = new();

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
        UpdateLiveValues(readings);
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
        foreach (var item in _itemsById.Values) item.IsVisible = Matches(item, query);

        foreach (var group in Groups)
        {
            var any = group.Items.Any(i => i.IsVisible);
            group.IsVisible = query.Length == 0 || any;
            if (expandGroups && query.Length > 0 && any) group.IsExpanded = true;
        }
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
