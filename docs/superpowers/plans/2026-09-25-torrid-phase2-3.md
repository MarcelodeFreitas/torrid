# Torrid Phase 2+3 (Real Integration, Settings, Per-Sensor Tray, Packaging) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Phase 1's fake sensor data with the real `SensorService`, add a Settings window covering thresholds/poll interval/per-sensor visibility/custom labels/per-sensor notification opt-in, redesign the tray icon to one icon per visible sensor (per user feedback after seeing Phase 1's combined icon), verify config persists across restarts, and produce a self-contained published build that self-elevates without manual "Run as Administrator".

**Architecture:** All new config fields and pure display-decision logic (visibility, label resolution, notification opt-out) live in `Torrid.Core` and are fully unit tested. The tray icon redesign replaces the single `TrayIconManager`/`TrayBitmapRenderer` pair from Phase 1 with a group manager owning one `NotifyIcon` per visible sensor. A single shared, mutable `AppConfig` instance flows from `App`/`MainWindow` startup into every consumer (view model, tray group, notification wiring, Settings window) — Settings mutates that same instance in place so changes are visible immediately without re-wiring subscriptions.

**Tech Stack:** C#/.NET 8, WPF, the existing `Torrid.Core`/`Torrid.App`/`Torrid.Tests` solution from Phase 1.

**Spec:** `PROJECT_PLAN.md` (repo root) sections 3, 6, 8 (Phase 2, Phase 3), 9. This plan also incorporates two decisions made after Phase 1 shipped and the user tried the real app (not in `PROJECT_PLAN.md`'s original text): (1) the tray icon must be one-icon-per-sensor, not a single combined icon (`PROJECT_PLAN.md` section 3 originally described a single icon; superseded); (2) Settings must let the user assign a custom per-sensor label, choose which sensors are visible, and choose which sensors generate notifications, independently of each other.

## Global Constraints

- Solution layout, already committed: `Torrid.Core` (net8.0), `Torrid.App` (net8.0-windows10.0.17763.0, WPF, `UseWindowsForms=true` for `NotifyIcon`), `Torrid.Tests` (net8.0-windows10.0.17763.0, xunit, references both). Full path for all commands: `C:\Program Files\dotnet\dotnet.exe`.
- Existing types you build on, do not redefine (all already committed at the start of this plan):
  - `Torrid.Core.Models.{SensorKind, SensorReading}` — `SensorReading` is `sealed record SensorReading(string Id, string Label, SensorKind Kind, int? GpuIndex, double? ValueCelsius)`.
  - `Torrid.Core.Sensors.{ISensorSource, SensorService, SensorReadingBuilder}`.
  - `Torrid.Core.Config.{ColorZone, ThresholdBand, ColorThresholdEvaluator, AppConfig, IConfigStore, JsonConfigStore}`.
  - `Torrid.Core.Notifications.{IToastNotifier, NotificationService}` — `NotificationService(IToastNotifier notifier, IClock clock, int refireMinutes, double reArmMarginCelsius = 3.0)`, method `Evaluate(IReadOnlyList<SensorReading> readings, IReadOnlyDictionary<SensorKind, ThresholdBand> thresholds)`.
  - `Torrid.Core.Time.{IClock, SystemClock}`.
  - `Torrid.App.ColorPalette.ForZone(ColorZone) -> (byte R, byte G, byte B)`.
  - `Torrid.App.Sensors.FakeSensorSource : ISensorSource` — being replaced by the real `SensorService` in Task 2, but keep the class (do not delete it — useful for future manual UI testing without hardware).
  - `Torrid.App.ViewModels.{SensorRowViewModel, MainWindowViewModel}`.
  - `Torrid.App.Tray.{TooltipTextBuilder, NativeMethods}` — `TooltipTextBuilder.Build(IReadOnlyList<SensorReading>) -> string`, `NativeMethods.DestroyIcon(nint) -> bool`.
  - `Torrid.App.Notifications.WindowsToastNotifier : IToastNotifier`.
  - `Torrid.App.Tray.{TrayIconManager, TrayBitmapRenderer}` — being REPLACED by Task 3 (delete both, replaced by `TrayIconGroupManager`/`SensorTrayIcon`/`SingleValueBitmapRenderer`).
- `AppConfig`'s existing empty-set-means-unrestricted convention (`VisibleSensorIds.Count == 0` → all visible) extends to the two new sets this plan adds — do not invert the convention for consistency.
- `dotnet build Torrid.sln` must succeed after every task's commit. Run tests with `dotnet test Torrid.sln`.
- Config file location (already established): `%AppData%\Torrid\config.json` (via `JsonConfigStore`'s default constructor).
- Known open issue, out of scope for this plan: Windows toast notifications do not currently produce a visible banner or Action Center entry on the target machine (see `tools/ToastProbe/FINDINGS.md`). `NotificationService`/`WindowsToastNotifier` are wired up correctly regardless — this plan does not attempt to fix toast delivery itself.
- The `SensorService` timer-restart/degrade-on-error behavior from Phase 1's final fix wave must not be altered by Task 2's changes — only add a way to change `PollIntervalMs` after construction (Task 4 needs this for Settings); do not change the `AutoReset=false`/`finally`-restart pattern.

## Review Focus

1. A sensor that was visible becomes invisible (Settings unchecks it, or VisibleSensorIds is edited some other way) while the app is running — its MainWindow row and tray icon must actually disappear, not just stop updating (stale rows/icons left behind is a real, easy-to-miss bug in incremental "add-only" view models).
2. A sensor's custom label is set to an empty/whitespace string in Settings — must fall back to the default label, not display a blank row/icon with no way to identify what it is.
3. The user disables notifications for one specific sensor (e.g. GPU1 Hot) while leaving others enabled — a red crossing on the disabled sensor must not fire a toast, while the same crossing on a still-enabled sensor must.
4. Settings is opened while zero GPUs are present (matches this machine's current real state) — the per-sensor list must show just the CPU row without crashing, and must not assume at least one GPU exists anywhere in its layout logic.
5. The poll interval is changed in Settings to an invalid value (0, negative, or absurdly large) — must be clamped to a sane range before being applied to the live `SensorService`, not passed through raw (the timer already throws on `<= 0`, per Phase 1's fix; the UI must not be able to trigger that crash).

---

### Task 1: Settings display logic (Torrid.Core)

**Files:**
- Modify: `Torrid.Core/Config/AppConfig.cs`
- Create: `Torrid.Core/Config/SensorDisplay.cs`
- Test: `Torrid.Tests/SensorDisplayTests.cs`

**Interfaces:**
- Consumes: `Torrid.Core.Models.SensorReading`, existing `AppConfig`.
- Produces:
  - `AppConfig.CustomLabels : Dictionary<string, string>` (default `new()`) — sensor `Id` → user-assigned label override.
  - `AppConfig.NotificationDisabledSensorIds : HashSet<string>` (default `new()`) — sensor `Id`s excluded from notifications; empty means all sensors notify (same convention as `VisibleSensorIds`).
  - `Torrid.Core.Config.SensorDisplay.IsVisible(AppConfig config, string sensorId) -> bool`
  - `Torrid.Core.Config.SensorDisplay.ResolveLabel(AppConfig config, SensorReading reading) -> string`
  - `Torrid.Core.Config.SensorDisplay.NotificationsEnabled(AppConfig config, string sensorId) -> bool`

- [ ] **Step 1: Write the failing tests**

Create `Torrid.Tests/SensorDisplayTests.cs`:

```csharp
using Torrid.Core.Config;
using Torrid.Core.Models;

namespace Torrid.Tests;

public class SensorDisplayTests
{
    private static SensorReading Cpu(string id = "cpu", string label = "CPU") =>
        new(id, label, SensorKind.CpuPackage, null, 50.0);

    [Fact]
    public void IsVisible_WhenVisibleSensorIdsEmpty_ReturnsTrueForAnyId()
    {
        var config = new AppConfig();

        Assert.True(SensorDisplay.IsVisible(config, "anything"));
    }

    [Fact]
    public void IsVisible_WhenVisibleSensorIdsNonEmpty_ReturnsTrueOnlyForListedIds()
    {
        var config = new AppConfig { VisibleSensorIds = { "cpu" } };

        Assert.True(SensorDisplay.IsVisible(config, "cpu"));
        Assert.False(SensorDisplay.IsVisible(config, "gpu0-core"));
    }

    [Fact]
    public void ResolveLabel_WhenNoCustomLabel_ReturnsReadingLabel()
    {
        var config = new AppConfig();

        var resolved = SensorDisplay.ResolveLabel(config, Cpu());

        Assert.Equal("CPU", resolved);
    }

    [Fact]
    public void ResolveLabel_WhenCustomLabelSet_ReturnsCustomLabel()
    {
        var config = new AppConfig { CustomLabels = { ["cpu"] = "5950X" } };

        var resolved = SensorDisplay.ResolveLabel(config, Cpu());

        Assert.Equal("5950X", resolved);
    }

    [Fact]
    public void ResolveLabel_WhenCustomLabelIsWhitespace_FallsBackToReadingLabel()
    {
        var config = new AppConfig { CustomLabels = { ["cpu"] = "   " } };

        var resolved = SensorDisplay.ResolveLabel(config, Cpu());

        Assert.Equal("CPU", resolved);
    }

    [Fact]
    public void NotificationsEnabled_ByDefault_ReturnsTrue()
    {
        var config = new AppConfig();

        Assert.True(SensorDisplay.NotificationsEnabled(config, "gpu1-hotspot"));
    }

    [Fact]
    public void NotificationsEnabled_WhenSensorIdDisabled_ReturnsFalseOnlyForThatId()
    {
        var config = new AppConfig { NotificationDisabledSensorIds = { "gpu1-hotspot" } };

        Assert.False(SensorDisplay.NotificationsEnabled(config, "gpu1-hotspot"));
        Assert.True(SensorDisplay.NotificationsEnabled(config, "gpu0-hotspot"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter SensorDisplayTests`
Expected: FAIL to build — `SensorDisplay` and the two new `AppConfig` members don't exist yet.

- [ ] **Step 3: Add the two new `AppConfig` fields**

Edit `Torrid.Core/Config/AppConfig.cs` — add after the existing `StartMinimizedToTray` line:

```csharp
    public Dictionary<string, string> CustomLabels { get; set; } = new();
    public HashSet<string> NotificationDisabledSensorIds { get; set; } = new();
```

- [ ] **Step 4: Implement `SensorDisplay`**

Create `Torrid.Core/Config/SensorDisplay.cs`:

```csharp
namespace Torrid.Core.Config;

using Torrid.Core.Models;

public static class SensorDisplay
{
    public static bool IsVisible(AppConfig config, string sensorId) =>
        config.VisibleSensorIds.Count == 0 || config.VisibleSensorIds.Contains(sensorId);

    public static string ResolveLabel(AppConfig config, SensorReading reading) =>
        config.CustomLabels.TryGetValue(reading.Id, out var custom) && !string.IsNullOrWhiteSpace(custom)
            ? custom
            : reading.Label;

    public static bool NotificationsEnabled(AppConfig config, string sensorId) =>
        !config.NotificationDisabledSensorIds.Contains(sensorId);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter SensorDisplayTests`
Expected: PASS, 7/7.

- [ ] **Step 6: Commit**

```bash
git add Torrid.Core/Config/AppConfig.cs Torrid.Core/Config/SensorDisplay.cs Torrid.Tests/SensorDisplayTests.cs
git commit -m "Add per-sensor visibility, custom label, and notification opt-out to AppConfig"
```

---

### Task 2: Real sensor integration (Phase 2)

**Files:**
- Modify: `Torrid.App/MainWindow.xaml.cs`
- Modify: `Torrid.App/ViewModels/MainWindowViewModel.cs`
- Test: `Torrid.Tests/MainWindowViewModelTests.cs` (new — the view model's filtering/labeling logic is pure enough to extract and test even though the view model itself binds to WPF collections)

**Interfaces:**
- Consumes: `Torrid.Core.Sensors.SensorService(int pollIntervalMs)`, `Torrid.Core.Config.{JsonConfigStore, SensorDisplay}`, `Torrid.Core.Notifications.NotificationService`, `Torrid.Core.Time.SystemClock`, `Torrid.App.Notifications.WindowsToastNotifier`.
- Produces: `MainWindowViewModel.Apply` now filters by `SensorDisplay.IsVisible` and labels via `SensorDisplay.ResolveLabel`, and removes rows for sensors that become invisible (Review Focus item 1). This task's `MainWindow.xaml.cs` becomes the app's composition root — Task 3 and Task 4 will read from it to know how the shared `AppConfig` instance and `ISensorSource` are constructed.

- [ ] **Step 1: Write the failing test for visibility filtering and stale-row removal**

Create `Torrid.Tests/MainWindowViewModelTests.cs`:

```csharp
using Torrid.App.ViewModels;
using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

namespace Torrid.Tests;

public sealed class FakeSensorSourceForTests : ISensorSource
{
    public IReadOnlyList<SensorReading> CurrentReadings { get; private set; } = Array.Empty<SensorReading>();
    public event EventHandler<IReadOnlyList<SensorReading>>? ReadingsUpdated;

    public void Start() { }
    public void Stop() { }

    public void Publish(IReadOnlyList<SensorReading> readings)
    {
        CurrentReadings = readings;
        ReadingsUpdated?.Invoke(this, readings);
    }
}

public class MainWindowViewModelTests
{
    private static SensorReading Cpu(double value = 50.0) => new("cpu", "CPU", SensorKind.CpuPackage, null, value);
    private static SensorReading Gpu0Core(double value = 50.0) => new("gpu0-core", "GPU0 Core", SensorKind.GpuCore, 0, value);

    [Fact]
    public void Apply_UsesCustomLabelWhenSet()
    {
        var source = new FakeSensorSourceForTests();
        var config = new AppConfig { CustomLabels = { ["cpu"] = "5950X" } };
        var viewModel = new MainWindowViewModel(source, config);

        source.Publish(new[] { Cpu() });

        Assert.Equal("5950X", viewModel.Rows.Single().Label);
    }

    [Fact]
    public void Apply_WhenSensorNotVisible_DoesNotAddRow()
    {
        var source = new FakeSensorSourceForTests();
        var config = new AppConfig { VisibleSensorIds = { "cpu" } };
        var viewModel = new MainWindowViewModel(source, config);

        source.Publish(new[] { Cpu(), Gpu0Core() });

        Assert.Single(viewModel.Rows);
        Assert.Equal("CPU", viewModel.Rows.Single().Label);
    }

    [Fact]
    public void Apply_WhenSensorBecomesInvisible_RemovesExistingRow()
    {
        var source = new FakeSensorSourceForTests();
        var config = new AppConfig();
        var viewModel = new MainWindowViewModel(source, config);

        source.Publish(new[] { Cpu(), Gpu0Core() });
        Assert.Equal(2, viewModel.Rows.Count);

        config.VisibleSensorIds.Add("cpu");
        source.Publish(new[] { Cpu(), Gpu0Core() });

        Assert.Single(viewModel.Rows);
        Assert.Equal("CPU", viewModel.Rows.Single().Label);
    }
}
```

Note: `MainWindowViewModel`'s constructor calls `Application.Current?.Dispatcher.Invoke(...)` inside its `ReadingsUpdated` handler, which no-ops safely when `Application.Current` is null (already true of the existing code, confirmed in Phase 1's review) — these tests run outside a WPF `Application`, so `OnReadingsUpdated`'s dispatcher call would no-op. To make this testable, change `MainWindowViewModel.Apply` to run synchronously when called directly by the constructor's initial `Apply(_sensorSource.CurrentReadings)`, and keep dispatching only in the event-driven path — but since `FakeSensorSourceForTests.Publish` raises the event synchronously and `Application.Current` is null in tests, `Dispatcher.Invoke` would never run `Apply`. Fix this in Step 3 below by checking `Application.Current is null` and calling `Apply` directly (synchronously) in that case — this is also more correct for headless/test scenarios generally, not just a test workaround.

- [ ] **Step 2: Run tests to verify they fail**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter MainWindowViewModelTests`
Expected: FAIL — either a build error (if `SensorDisplay` isn't wired in yet) or the tests hang/fail because `Apply` never runs under a null `Application.Current`.

- [ ] **Step 3: Update `MainWindowViewModel`**

Replace `Torrid.App/ViewModels/MainWindowViewModel.cs` in full:

```csharp
namespace Torrid.App.ViewModels;

using System.Collections.ObjectModel;
using System.Windows;
using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

public sealed class MainWindowViewModel
{
    private readonly ISensorSource _sensorSource;
    private readonly AppConfig _config;
    private readonly Dictionary<string, SensorRowViewModel> _rowsById = new();

    public MainWindowViewModel(ISensorSource sensorSource, AppConfig config)
    {
        _sensorSource = sensorSource;
        _config = config;
        _sensorSource.ReadingsUpdated += OnReadingsUpdated;
        Apply(_sensorSource.CurrentReadings);
    }

    public ObservableCollection<SensorRowViewModel> Rows { get; } = new();

    private void OnReadingsUpdated(object? sender, IReadOnlyList<SensorReading> readings)
    {
        if (Application.Current is null)
        {
            Apply(readings);
        }
        else
        {
            Application.Current.Dispatcher.Invoke(() => Apply(readings));
        }
    }

    private void Apply(IReadOnlyList<SensorReading> readings)
    {
        var visibleIds = new HashSet<string>();

        foreach (var reading in readings)
        {
            if (!SensorDisplay.IsVisible(_config, reading.Id))
            {
                continue;
            }

            visibleIds.Add(reading.Id);

            if (!_rowsById.TryGetValue(reading.Id, out var row))
            {
                row = new SensorRowViewModel(SensorDisplay.ResolveLabel(_config, reading));
                _rowsById[reading.Id] = row;
                Rows.Add(row);
            }

            if (_config.Thresholds.TryGetValue(reading.Kind, out var band))
            {
                row.Update(reading.ValueCelsius, band);
            }
        }

        foreach (var staleId in _rowsById.Keys.Except(visibleIds).ToList())
        {
            Rows.Remove(_rowsById[staleId]);
            _rowsById.Remove(staleId);
        }
    }
}
```

Note this drops the ability to change a row's label after creation (a sensor's `SensorRowViewModel.Label` is set once, in the constructor, and never updated if the custom label changes later while the sensor stays visible). That's acceptable for this task — Task 4's Settings window closing and Task 2/3's periodic redraw naturally pick up label changes because `_rowsById` only tracks *visibility*, not label content, and a full app restart (or, more precisely, the next time a sensor transitions invisible→visible) will re-create the row with the current label. Do not over-engineer live label updates for already-visible rows; it's not in the Review Focus list and the plan's `Settings` step (Task 4) explicitly does not require hot-reloading label text within a still-visible row.

- [ ] **Step 4: Run tests to verify they pass**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter MainWindowViewModelTests`
Expected: PASS, 3/3.

- [ ] **Step 5: Run the full suite to confirm no regressions**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.sln`
Expected: PASS, all tests (35 from Phase 1 + 7 from Task 1 + 3 from this task = 45).

- [ ] **Step 6: Commit the view model change**

```bash
git add Torrid.App/ViewModels/MainWindowViewModel.cs Torrid.Tests/MainWindowViewModelTests.cs
git commit -m "MainWindowViewModel: filter by visibility, resolve custom labels, drop stale rows"
```

- [ ] **Step 7: Wire the real SensorService, JsonConfigStore, and NotificationService into MainWindow**

Replace `Torrid.App/MainWindow.xaml.cs` in full:

```csharp
namespace Torrid.App;

using System.Windows;
using Torrid.App.Notifications;
using Torrid.App.Tray;
using Torrid.App.ViewModels;
using Torrid.Core.Config;
using Torrid.Core.Notifications;
using Torrid.Core.Sensors;
using Torrid.Core.Time;

public partial class MainWindow : Window
{
    private readonly IConfigStore _configStore = new JsonConfigStore();
    private readonly AppConfig _config;
    private readonly SensorService _sensorSource;
    private readonly NotificationService _notificationService;
    private readonly TrayIconGroupManager _trayIconGroupManager;

    public MainWindow()
    {
        InitializeComponent();

        _config = _configStore.Load();
        _sensorSource = new SensorService(_config.PollIntervalMs);
        _notificationService = new NotificationService(new WindowsToastNotifier(), new SystemClock(), _config.NotificationRefireMinutes);

        DataContext = new MainWindowViewModel(_sensorSource, _config);

        _trayIconGroupManager = new TrayIconGroupManager(_sensorSource, _config);
        _trayIconGroupManager.TrayIconClicked += (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };

        _sensorSource.ReadingsUpdated += OnReadingsUpdatedForNotifications;

        _sensorSource.Start();

        Closed += (_, _) =>
        {
            _sensorSource.Stop();
            _sensorSource.Dispose();
            _trayIconGroupManager.Dispose();
        };
    }

    private void OnReadingsUpdatedForNotifications(object? sender, IReadOnlyList<Core.Models.SensorReading> readings)
    {
        var notifiable = readings.Where(r => Core.Config.SensorDisplay.NotificationsEnabled(_config, r.Id)).ToList();
        _notificationService.Evaluate(notifiable, _config.Thresholds);
    }
}
```

This references `TrayIconGroupManager`, which does not exist until Task 3 — this task will not build standalone until Task 3 lands. That is expected and acceptable here because Task 3 is the very next task in this same sequential plan (unlike Phase 1, where every task needed to build independently because dispatch order wasn't guaranteed sequential from the plan's own text — this plan is explicitly sequential and Task 3 is next). Do not attempt to work around this by stubbing `TrayIconGroupManager` here; just note in your task report that the solution will not build until Task 3 completes, and do not run the "verify build succeeds" step for this task in isolation — Task 3's own build-verification step covers both tasks together.

- [ ] **Step 8: Commit the composition-root wiring**

```bash
git add Torrid.App/MainWindow.xaml.cs
git commit -m "Wire real SensorService, JsonConfigStore, and NotificationService into MainWindow (Phase 2 integration)"
```

---

### Task 3: Per-sensor tray icons

**Files:**
- Delete: `Torrid.App/Tray/TrayIconManager.cs`
- Delete: `Torrid.App/Tray/TrayBitmapRenderer.cs`
- Create: `Torrid.App/Tray/SingleValueBitmapRenderer.cs`
- Create: `Torrid.App/Tray/SensorTrayIcon.cs`
- Create: `Torrid.App/Tray/TrayIconGroupManager.cs`
- Test: `Torrid.Tests/SingleValueBitmapRendererTests.cs` (only the pure color-selection helper is unit-testable; the GDI+ drawing itself is verified by running the app, same rationale as Phase 1's `TrayBitmapRenderer`)

**Interfaces:**
- Consumes: `Torrid.Core.Config.SensorDisplay`, `Torrid.App.ColorPalette`, `Torrid.App.Tray.{TooltipTextBuilder, NativeMethods}`, `Torrid.Core.Sensors.ISensorSource`, `Torrid.Core.Config.AppConfig`.
- Produces: `Torrid.App.Tray.TrayIconGroupManager`, constructed as `new TrayIconGroupManager(ISensorSource sensorSource, AppConfig config)`, implementing `IDisposable`, raising `event EventHandler? TrayIconClicked` — this is the exact type Task 2's `MainWindow.xaml.cs` already references.

- [ ] **Step 1: Write the failing test for the per-value color/text decision**

Create `Torrid.Tests/SingleValueBitmapRendererTests.cs`:

```csharp
using Torrid.App.Tray;
using Torrid.Core.Config;

namespace Torrid.Tests;

public class SingleValueBitmapRendererTests
{
    private static readonly ThresholdBand Cpu = new() { BlueMax = 40, GreenMax = 70, YellowMax = 85 };

    [Fact]
    public void FormatValueText_WhenValuePresent_ReturnsRoundedDegrees()
    {
        var text = SingleValueBitmapRenderer.FormatValueText(67.6);

        Assert.Equal("68", text);
    }

    [Fact]
    public void FormatValueText_WhenValueNull_ReturnsPlaceholder()
    {
        var text = SingleValueBitmapRenderer.FormatValueText(null);

        Assert.Equal("--", text);
    }

    [Fact]
    public void ResolveZone_WhenValueNull_ReturnsBlue()
    {
        var zone = SingleValueBitmapRenderer.ResolveZone(null, Cpu);

        Assert.Equal(ColorZone.Blue, zone);
    }

    [Fact]
    public void ResolveZone_WhenValuePresent_UsesColorThresholdEvaluator()
    {
        var zone = SingleValueBitmapRenderer.ResolveZone(90.0, Cpu);

        Assert.Equal(ColorZone.Red, zone);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter SingleValueBitmapRendererTests`
Expected: FAIL to build — `SingleValueBitmapRenderer` does not exist yet.

- [ ] **Step 3: Delete the Phase 1 combined-icon renderer, implement the per-sensor renderer**

Delete `Torrid.App/Tray/TrayIconManager.cs` and `Torrid.App/Tray/TrayBitmapRenderer.cs`.

Create `Torrid.App/Tray/SingleValueBitmapRenderer.cs`:

```csharp
namespace Torrid.App.Tray;

using System.Drawing;
using System.Drawing.Drawing2D;
using Torrid.Core.Config;

public sealed class SingleValueBitmapRenderer
{
    private const int Size = 32;

    public Icon Render(double? valueCelsius, ThresholdBand thresholds, bool blinkOn)
    {
        var zone = ResolveZone(valueCelsius, thresholds);
        var isRed = zone == ColorZone.Red;

        using var bitmap = new Bitmap(Size, Size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            if (isRed && !blinkOn)
            {
                using var dimBrush = new SolidBrush(Color.FromArgb(70, 244, 67, 54));
                g.FillEllipse(dimBrush, 0, 0, Size, Size);
            }

            var (r, gC, b) = ColorPalette.ForZone(zone);
            using var brush = new SolidBrush(Color.FromArgb(r, gC, b));
            using var font = new Font("Segoe UI", 13, System.Drawing.FontStyle.Bold);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(FormatValueText(valueCelsius), font, brush, new RectangleF(0, 0, Size, Size), format);
        }

        var handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }

    public static string FormatValueText(double? valueCelsius) =>
        valueCelsius is double v ? $"{v:F0}" : "--";

    public static ColorZone ResolveZone(double? valueCelsius, ThresholdBand thresholds) =>
        valueCelsius is double v ? ColorThresholdEvaluator.GetColor(v, thresholds) : ColorZone.Blue;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter SingleValueBitmapRendererTests`
Expected: PASS, 4/4.

- [ ] **Step 5: Commit the renderer**

```bash
git rm Torrid.App/Tray/TrayIconManager.cs Torrid.App/Tray/TrayBitmapRenderer.cs
git add Torrid.App/Tray/SingleValueBitmapRenderer.cs Torrid.Tests/SingleValueBitmapRendererTests.cs
git commit -m "Replace combined tray icon renderer with per-sensor SingleValueBitmapRenderer"
```

- [ ] **Step 6: Implement `SensorTrayIcon` and `TrayIconGroupManager` (thin wiring/GDI+ — no further red/green cycle; verify by running the app in Step 7)**

Create `Torrid.App/Tray/SensorTrayIcon.cs`:

```csharp
namespace Torrid.App.Tray;

using System.Windows.Forms;
using Torrid.Core.Config;
using Torrid.Core.Models;

public sealed class SensorTrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly SingleValueBitmapRenderer _renderer = new();
    private nint _previousIconHandle;

    public SensorTrayIcon()
    {
        _notifyIcon = new NotifyIcon { Visible = true };
        _notifyIcon.Click += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Clicked;

    public void UpdateVisual(SensorReading reading, string label, ThresholdBand thresholds, bool blinkOn)
    {
        var icon = _renderer.Render(reading.ValueCelsius, thresholds, blinkOn);
        _notifyIcon.Icon = icon;
        _notifyIcon.Text = TooltipTextBuilder.Build(new[] { reading with { Label = label } });

        if (_previousIconHandle != nint.Zero) NativeMethods.DestroyIcon(_previousIconHandle);
        _previousIconHandle = icon.Handle;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        if (_previousIconHandle != nint.Zero) NativeMethods.DestroyIcon(_previousIconHandle);
    }
}
```

Create `Torrid.App/Tray/TrayIconGroupManager.cs`:

```csharp
namespace Torrid.App.Tray;

using System.Windows;
using System.Windows.Threading;
using Torrid.Core.Config;
using Torrid.Core.Sensors;

public sealed class TrayIconGroupManager : IDisposable
{
    private readonly ISensorSource _sensorSource;
    private readonly AppConfig _config;
    private readonly Dictionary<string, SensorTrayIcon> _icons = new();
    private readonly DispatcherTimer _blinkTimer;
    private bool _blinkOn = true;

    public TrayIconGroupManager(ISensorSource sensorSource, AppConfig config)
    {
        _sensorSource = sensorSource;
        _config = config;

        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _blinkTimer.Tick += (_, _) => { _blinkOn = !_blinkOn; Redraw(); };
        _blinkTimer.Start();

        _sensorSource.ReadingsUpdated += (_, _) => Application.Current?.Dispatcher.Invoke(Redraw);

        Redraw();
    }

    public event EventHandler? TrayIconClicked;

    private void Redraw()
    {
        var visibleReadings = _sensorSource.CurrentReadings
            .Where(r => SensorDisplay.IsVisible(_config, r.Id))
            .ToList();
        var visibleIds = visibleReadings.Select(r => r.Id).ToHashSet();

        foreach (var staleId in _icons.Keys.Except(visibleIds).ToList())
        {
            _icons[staleId].Dispose();
            _icons.Remove(staleId);
        }

        foreach (var reading in visibleReadings)
        {
            if (!_icons.TryGetValue(reading.Id, out var icon))
            {
                icon = new SensorTrayIcon();
                icon.Clicked += (_, _) => TrayIconClicked?.Invoke(this, EventArgs.Empty);
                _icons[reading.Id] = icon;
            }

            if (_config.Thresholds.TryGetValue(reading.Kind, out var band))
            {
                icon.UpdateVisual(reading, SensorDisplay.ResolveLabel(_config, reading), band, _blinkOn);
            }
        }
    }

    public void Dispose()
    {
        _blinkTimer.Stop();
        foreach (var icon in _icons.Values) icon.Dispose();
        _icons.Clear();
    }
}
```

This satisfies Review Focus item 1 (a sensor becoming invisible removes its tray icon, mirroring `MainWindowViewModel`'s row removal from Task 2) and item 4 (an empty/1-GPU reading list just produces fewer icons, nothing in `Redraw` assumes a minimum count).

- [ ] **Step 7: Build and manually verify by running the app**

Run: `& "C:\Program Files\dotnet\dotnet.exe" build Torrid.sln`
Expected: Build succeeded, 0 errors (this also confirms Task 2's `MainWindow.xaml.cs` now compiles against the real `TrayIconGroupManager`).

Run: `& "C:\Program Files\dotnet\dotnet.exe" run --project Torrid.App` (requires elevation — a UAC prompt will appear; this must be approved by a human, not a subagent, per the established constraint from Phase 1). Confirm: one tray icon per visible sensor appears (CPU, plus one icon per GPU sensor actually present on this machine), each showing just that sensor's number, colored by its own threshold; hovering shows that sensor's own tooltip; clicking any icon reopens the main window. If you cannot get UAC approval in your environment, report this plainly as an unverified manual step rather than skipping it silently — the same constraint Phase 1 already established for tray/window verification.

- [ ] **Step 8: Commit**

```bash
git add Torrid.App/Tray/SensorTrayIcon.cs Torrid.App/Tray/TrayIconGroupManager.cs
git commit -m "Add TrayIconGroupManager: one tray icon per visible sensor, replacing the combined icon"
```

---

### Task 4: Settings window

**Files:**
- Create: `Torrid.App/ViewModels/SensorSettingRowViewModel.cs`
- Create: `Torrid.App/ViewModels/ThresholdSettingRowViewModel.cs`
- Create: `Torrid.App/ViewModels/SettingsViewModel.cs`
- Create: `Torrid.App/SettingsWindow.xaml`
- Create: `Torrid.App/SettingsWindow.xaml.cs`
- Modify: `Torrid.Core/Sensors/SensorService.cs` (add a way to change the poll interval live)
- Modify: `Torrid.App/MainWindow.xaml.cs` (add a way to open Settings)
- Test: `Torrid.Tests/SettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `Torrid.Core.Config.{AppConfig, IConfigStore, ThresholdBand}`, `Torrid.Core.Models.{SensorReading, SensorKind}`.
- Produces: `Torrid.App.ViewModels.SettingsViewModel`, constructed as `new SettingsViewModel(AppConfig config, IReadOnlyList<SensorReading> currentReadings)`, with an `Apply()` method that writes edited state back into the same `AppConfig` instance (mutates in place, does not replace the reference) and returns the (possibly clamped) new poll interval in milliseconds.

- [ ] **Step 1: Write the failing tests for the settings view model's edit/apply logic**

Create `Torrid.Tests/SettingsViewModelTests.cs`:

```csharp
using Torrid.App.ViewModels;
using Torrid.Core.Config;
using Torrid.Core.Models;

namespace Torrid.Tests;

public class SettingsViewModelTests
{
    private static SensorReading Cpu() => new("cpu", "CPU", SensorKind.CpuPackage, null, 50.0);
    private static SensorReading Gpu0Core() => new("gpu0-core", "GPU0 Core", SensorKind.GpuCore, 0, 50.0);

    [Fact]
    public void Constructor_BuildsOneSensorRowPerCurrentReading()
    {
        var config = new AppConfig();
        var viewModel = new SettingsViewModel(config, new[] { Cpu(), Gpu0Core() });

        Assert.Equal(2, viewModel.SensorRows.Count);
    }

    [Fact]
    public void Constructor_WithNoGpus_BuildsJustTheCpuRowWithoutThrowing()
    {
        var config = new AppConfig();

        var viewModel = new SettingsViewModel(config, new[] { Cpu() });

        Assert.Single(viewModel.SensorRows);
    }

    [Fact]
    public void Apply_UncheckingVisible_ExcludesThatSensorFromVisibilityOnly()
    {
        var config = new AppConfig();
        var viewModel = new SettingsViewModel(config, new[] { Cpu(), Gpu0Core() });
        var cpuRow = viewModel.SensorRows.Single(r => r.SensorId == "cpu");

        cpuRow.IsVisible = false;
        viewModel.Apply();

        // Verify through the actual public contract (SensorDisplay.IsVisible), not raw
        // set membership — VisibleSensorIds stores the VISIBLE set when not all sensors
        // are visible (see the Apply() note below), so asserting membership directly
        // would silently invert if that internal representation ever changed.
        Assert.False(SensorDisplay.IsVisible(config, "cpu"));
        Assert.True(SensorDisplay.IsVisible(config, "gpu0-core"));
    }

    [Fact]
    public void Apply_SettingCustomLabel_WritesToCustomLabels()
    {
        var config = new AppConfig();
        var viewModel = new SettingsViewModel(config, new[] { Gpu0Core() });
        var row = viewModel.SensorRows.Single();

        row.CustomLabel = "MSI Core";
        viewModel.Apply();

        Assert.Equal("MSI Core", config.CustomLabels["gpu0-core"]);
    }

    [Fact]
    public void Apply_SettingCustomLabelToWhitespace_RemovesAnyExistingOverride()
    {
        var config = new AppConfig { CustomLabels = { ["cpu"] = "5950X" } };
        var viewModel = new SettingsViewModel(config, new[] { Cpu() });
        var row = viewModel.SensorRows.Single();

        row.CustomLabel = "   ";
        viewModel.Apply();

        Assert.False(config.CustomLabels.ContainsKey("cpu"));
    }

    [Fact]
    public void Apply_UncheckingNotify_AddsSensorIdToNotificationDisabledSensorIds()
    {
        var config = new AppConfig();
        var viewModel = new SettingsViewModel(config, new[] { Gpu0Core() });
        var row = viewModel.SensorRows.Single();

        row.NotificationsEnabled = false;
        viewModel.Apply();

        Assert.Contains("gpu0-core", config.NotificationDisabledSensorIds);
    }

    [Theory]
    [InlineData(0, 500)]
    [InlineData(-100, 500)]
    [InlineData(50, 500)]
    [InlineData(2000, 2000)]
    [InlineData(120000, 60000)]
    public void Apply_ClampsPollIntervalToSaneRange(int enteredMs, int expectedMs)
    {
        var config = new AppConfig();
        var viewModel = new SettingsViewModel(config, new[] { Cpu() }) { PollIntervalMs = enteredMs };

        var applied = viewModel.Apply();

        Assert.Equal(expectedMs, applied);
        Assert.Equal(expectedMs, config.PollIntervalMs);
    }

    [Fact]
    public void Apply_WritesEditedThresholdBandBackToConfig()
    {
        var config = new AppConfig();
        var viewModel = new SettingsViewModel(config, new[] { Cpu() });
        var cpuThresholds = viewModel.ThresholdRows.Single(r => r.Kind == SensorKind.CpuPackage);

        cpuThresholds.BlueMax = 35;
        cpuThresholds.GreenMax = 65;
        cpuThresholds.YellowMax = 80;
        viewModel.Apply();

        var band = config.Thresholds[SensorKind.CpuPackage];
        Assert.Equal(35, band.BlueMax);
        Assert.Equal(65, band.GreenMax);
        Assert.Equal(80, band.YellowMax);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter SettingsViewModelTests`
Expected: FAIL to build — none of the new view model types exist yet.

- [ ] **Step 3: Implement the row view models and `SettingsViewModel`**

Create `Torrid.App/ViewModels/SensorSettingRowViewModel.cs`:

```csharp
namespace Torrid.App.ViewModels;

public sealed class SensorSettingRowViewModel
{
    public SensorSettingRowViewModel(string sensorId, string defaultLabel, bool isVisible, string customLabel, bool notificationsEnabled)
    {
        SensorId = sensorId;
        DefaultLabel = defaultLabel;
        IsVisible = isVisible;
        CustomLabel = customLabel;
        NotificationsEnabled = notificationsEnabled;
    }

    public string SensorId { get; }
    public string DefaultLabel { get; }
    public bool IsVisible { get; set; }
    public string CustomLabel { get; set; }
    public bool NotificationsEnabled { get; set; }
}
```

Create `Torrid.App/ViewModels/ThresholdSettingRowViewModel.cs`:

```csharp
namespace Torrid.App.ViewModels;

using Torrid.Core.Models;

public sealed class ThresholdSettingRowViewModel
{
    public ThresholdSettingRowViewModel(SensorKind kind, double blueMax, double greenMax, double yellowMax)
    {
        Kind = kind;
        BlueMax = blueMax;
        GreenMax = greenMax;
        YellowMax = yellowMax;
    }

    public SensorKind Kind { get; }
    public double BlueMax { get; set; }
    public double GreenMax { get; set; }
    public double YellowMax { get; set; }
}
```

Create `Torrid.App/ViewModels/SettingsViewModel.cs`:

```csharp
namespace Torrid.App.ViewModels;

using Torrid.Core.Config;
using Torrid.Core.Models;

public sealed class SettingsViewModel
{
    private const int MinPollIntervalMs = 500;
    private const int MaxPollIntervalMs = 60000;

    private readonly AppConfig _config;

    public SettingsViewModel(AppConfig config, IReadOnlyList<SensorReading> currentReadings)
    {
        _config = config;
        PollIntervalMs = config.PollIntervalMs;
        NotificationRefireMinutes = config.NotificationRefireMinutes;
        StartMinimizedToTray = config.StartMinimizedToTray;

        SensorRows = currentReadings
            .Select(reading => new SensorSettingRowViewModel(
                reading.Id,
                reading.Label,
                SensorDisplay.IsVisible(config, reading.Id),
                config.CustomLabels.TryGetValue(reading.Id, out var custom) ? custom : string.Empty,
                SensorDisplay.NotificationsEnabled(config, reading.Id)))
            .ToList();

        ThresholdRows = Enum.GetValues<SensorKind>()
            .Select(kind =>
            {
                var band = config.Thresholds.TryGetValue(kind, out var existing) ? existing : new ThresholdBand();
                return new ThresholdSettingRowViewModel(kind, band.BlueMax, band.GreenMax, band.YellowMax);
            })
            .ToList();
    }

    public int PollIntervalMs { get; set; }
    public int NotificationRefireMinutes { get; set; }
    public bool StartMinimizedToTray { get; set; }
    public List<SensorSettingRowViewModel> SensorRows { get; }
    public List<ThresholdSettingRowViewModel> ThresholdRows { get; }

    public int Apply()
    {
        var clampedPollIntervalMs = Math.Clamp(PollIntervalMs, MinPollIntervalMs, MaxPollIntervalMs);

        _config.PollIntervalMs = clampedPollIntervalMs;
        _config.NotificationRefireMinutes = NotificationRefireMinutes;
        _config.StartMinimizedToTray = StartMinimizedToTray;

        _config.VisibleSensorIds.Clear();
        if (!SensorRows.All(r => r.IsVisible))
        {
            foreach (var row in SensorRows.Where(r => r.IsVisible))
            {
                _config.VisibleSensorIds.Add(row.SensorId);
            }
        }

        _config.CustomLabels.Clear();
        foreach (var row in SensorRows.Where(r => !string.IsNullOrWhiteSpace(r.CustomLabel)))
        {
            _config.CustomLabels[row.SensorId] = row.CustomLabel;
        }

        _config.NotificationDisabledSensorIds.Clear();
        foreach (var row in SensorRows.Where(r => !r.NotificationsEnabled))
        {
            _config.NotificationDisabledSensorIds.Add(row.SensorId);
        }

        foreach (var row in ThresholdRows)
        {
            _config.Thresholds[row.Kind] = new ThresholdBand { BlueMax = row.BlueMax, GreenMax = row.GreenMax, YellowMax = row.YellowMax };
        }

        return clampedPollIntervalMs;
    }
}
```

Note on the `VisibleSensorIds` block above: it stores the VISIBLE set (not the hidden set) whenever not every sensor is visible, and clears it (empty = all visible, the existing convention) only when literally every known sensor is checked visible. This correctly distinguishes "every sensor visible" (empty set) from "every sensor hidden" (a non-empty set naming every id) since those are different set contents under this convention — there is no ambiguity to resolve further.

- [ ] **Step 4: Run tests to verify they pass**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter SettingsViewModelTests`
Expected: PASS, 8/8.

- [ ] **Step 5: Commit the view models**

```bash
git add Torrid.App/ViewModels/SensorSettingRowViewModel.cs Torrid.App/ViewModels/ThresholdSettingRowViewModel.cs Torrid.App/ViewModels/SettingsViewModel.cs Torrid.Tests/SettingsViewModelTests.cs
git commit -m "Add SettingsViewModel: per-sensor visibility/label/notification editing, threshold editing"
```

- [ ] **Step 6: Add a live poll-interval setter to `SensorService`**

Modify `Torrid.Core/Sensors/SensorService.cs` — add a public property after the existing `CurrentReadings` property:

```csharp
    public int PollIntervalMs
    {
        get => (int)_timer.Interval;
        set => _timer.Interval = Math.Max(1, value);
    }
```

This is a simple property that changes the underlying `System.Timers.Timer.Interval` — it takes effect on the next scheduled restart (the existing `AutoReset = false` / restart-in-`finally` pattern from Phase 1's fix wave already re-reads `_timer.Interval` each time `_timer.Start()` is called, so no other change is needed). `SensorService` remains untested directly per its established design rationale; this property has no independent logic worth a unit test (it's a direct passthrough with a floor of 1ms to avoid `ArgumentException` from `System.Timers.Timer`, and `SettingsViewModel.Apply`'s own clamp to a minimum of 500ms is the real safety net — Review Focus item 5 is satisfied at the `SettingsViewModel` layer, tested in Step 1 above).

- [ ] **Step 7: Build to confirm it compiles**

Run: `& "C:\Program Files\dotnet\dotnet.exe" build Torrid.sln`
Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add Torrid.Core/Sensors/SensorService.cs
git commit -m "Add live PollIntervalMs setter to SensorService for Settings to apply"
```

- [ ] **Step 9: Build the Settings window XAML and code-behind (thin wiring — no further red/green cycle; verify by running the app in Step 10)**

Create `Torrid.App/SettingsWindow.xaml`:

```xml
<Window x:Class="Torrid.App.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Torrid Settings" Height="520" Width="480"
        Background="#1E1E1E" WindowStartupLocation="CenterOwner">
    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,12">
            <TextBlock Text="Poll interval (ms):" Foreground="White" VerticalAlignment="Center" Margin="0,0,8,0"/>
            <TextBox Name="PollIntervalTextBox" Width="80" Text="{Binding PollIntervalMs, UpdateSourceTrigger=PropertyChanged}"/>
            <TextBlock Text="Notification refire (min, 0=never):" Foreground="White" VerticalAlignment="Center" Margin="16,0,8,0"/>
            <TextBox Name="RefireTextBox" Width="60" Text="{Binding NotificationRefireMinutes, UpdateSourceTrigger=PropertyChanged}"/>
        </StackPanel>

        <TabControl Grid.Row="1" Background="#1E1E1E">
            <TabItem Header="Sensors">
                <ItemsControl ItemsSource="{Binding SensorRows}" Margin="8">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border BorderBrush="#3A3A3A" BorderThickness="0,0,0,1" Padding="0,8">
                                <StackPanel>
                                    <TextBlock Text="{Binding DefaultLabel}" Foreground="White" FontWeight="Bold"/>
                                    <StackPanel Orientation="Horizontal" Margin="0,4,0,0">
                                        <CheckBox Content="Visible" IsChecked="{Binding IsVisible}" Foreground="White" Margin="0,0,12,0"/>
                                        <CheckBox Content="Notify" IsChecked="{Binding NotificationsEnabled}" Foreground="White" Margin="0,0,12,0"/>
                                        <TextBlock Text="Label:" Foreground="White" VerticalAlignment="Center" Margin="0,0,4,0"/>
                                        <TextBox Text="{Binding CustomLabel, UpdateSourceTrigger=PropertyChanged}" Width="140"/>
                                    </StackPanel>
                                </StackPanel>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </TabItem>
            <TabItem Header="Thresholds">
                <ItemsControl ItemsSource="{Binding ThresholdRows}" Margin="8">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border BorderBrush="#3A3A3A" BorderThickness="0,0,0,1" Padding="0,8">
                                <StackPanel>
                                    <TextBlock Text="{Binding Kind}" Foreground="White" FontWeight="Bold"/>
                                    <StackPanel Orientation="Horizontal" Margin="0,4,0,0">
                                        <TextBlock Text="Blue &lt;" Foreground="#429CF5" VerticalAlignment="Center" Margin="0,0,4,0"/>
                                        <TextBox Text="{Binding BlueMax, UpdateSourceTrigger=PropertyChanged}" Width="50" Margin="0,0,12,0"/>
                                        <TextBlock Text="Green &lt;" Foreground="#4CAF50" VerticalAlignment="Center" Margin="0,0,4,0"/>
                                        <TextBox Text="{Binding GreenMax, UpdateSourceTrigger=PropertyChanged}" Width="50" Margin="0,0,12,0"/>
                                        <TextBlock Text="Yellow &lt;=" Foreground="#FFC107" VerticalAlignment="Center" Margin="0,0,4,0"/>
                                        <TextBox Text="{Binding YellowMax, UpdateSourceTrigger=PropertyChanged}" Width="50"/>
                                    </StackPanel>
                                </StackPanel>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </TabItem>
        </TabControl>

        <CheckBox Grid.Row="2" Content="Start minimized to tray" IsChecked="{Binding StartMinimizedToTray}" Foreground="White" Margin="0,12,0,0"/>

        <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,12,0,0">
            <Button Name="SaveButton" Content="Save" Width="80" Margin="0,0,8,0" Click="OnSaveClick"/>
            <Button Name="CancelButton" Content="Cancel" Width="80" Click="OnCancelClick"/>
        </StackPanel>
    </Grid>
</Window>
```

Create `Torrid.App/SettingsWindow.xaml.cs`:

```csharp
namespace Torrid.App;

using System.Windows;
using Torrid.App.ViewModels;
using Torrid.Core.Config;
using Torrid.Core.Models;

public partial class SettingsWindow : Window
{
    private readonly AppConfig _config;
    private readonly SettingsViewModel _viewModel;
    private readonly IConfigStore _configStore;
    private readonly Action<int> _onPollIntervalApplied;

    public SettingsWindow(AppConfig config, IReadOnlyList<SensorReading> currentReadings, IConfigStore configStore, Action<int> onPollIntervalApplied)
    {
        InitializeComponent();
        _config = config;
        _viewModel = new SettingsViewModel(config, currentReadings);
        _configStore = configStore;
        _onPollIntervalApplied = onPollIntervalApplied;
        DataContext = _viewModel;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var appliedPollIntervalMs = _viewModel.Apply();
        _configStore.Save(_config);
        _onPollIntervalApplied(appliedPollIntervalMs);
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
}
```

`SettingsViewModel.Apply()` mutates the `_config` instance passed into the constructor in place (it's the same shared `AppConfig` object `MainWindow` constructed at startup), so saving `_config` right after calling `Apply()` persists exactly what was just applied to the running app — there is no separate "get the config back out of the view model" step needed.

- [ ] **Step 10: Wire Settings into MainWindow and verify by running the app**

Modify `Torrid.App/MainWindow.xaml.cs` — add a way to open Settings. Add this after the existing `_trayIconGroupManager.TrayIconClicked += ...` block from Task 2:

```csharp
        MouseRightButtonUp += (_, _) => OpenSettings();
```

And add this method to the `MainWindow` class:

```csharp
    private void OpenSettings()
    {
        var settings = new SettingsWindow(_config, _sensorSource.CurrentReadings, _configStore, newPollIntervalMs => _sensorSource.PollIntervalMs = newPollIntervalMs)
        {
            Owner = this,
        };
        settings.ShowDialog();
    }
```

(Right-click-anywhere-on-the-window to open Settings is a minimal, functional entry point for this plan — a proper menu/button is a polish item the user can ask for later; this plan does not spend a task on window chrome.)

Run: `& "C:\Program Files\dotnet\dotnet.exe" build Torrid.sln`
Expected: Build succeeded, 0 errors.

Run: `& "C:\Program Files\dotnet\dotnet.exe" run --project Torrid.App` (requires elevation — human UAC approval). Confirm: right-clicking the main window opens the Settings window; it lists one row per currently-detected sensor (just CPU if no GPU is present, confirming Review Focus item 4); unchecking a sensor's Visible box and clicking Save removes its MainWindow row and tray icon; setting a Custom Label and Save then reopening Settings shows the label persisted in the field; editing a threshold row and Save changes that sensor's color banding on the next poll tick.

- [ ] **Step 11: Commit**

```bash
git add Torrid.App/SettingsWindow.xaml Torrid.App/SettingsWindow.xaml.cs Torrid.App/MainWindow.xaml.cs
git commit -m "Add Settings window: thresholds, poll interval, per-sensor visibility/label/notify"
```

---

### Task 5: Persistence verification and packaging

**Files:**
- No new source files. This task is verification and a publish command; document results in the task report.

**Interfaces:**
- Consumes: everything built in Tasks 1-4.
- Produces: a self-contained published build under `Torrid.App/bin/Release/net8.0-windows10.0.17763.0/win-x64/publish/`.

- [ ] **Step 1: Manually verify persistence across a restart**

Run the app elevated (human UAC approval required), open Settings, make a distinctive change (e.g. set the CPU's custom label to `"PERSISTENCE-TEST"`, uncheck one GPU sensor's Visible box, change the poll interval to `3000`), click Save, close the app fully (not just to tray — actually exit), then relaunch it. Confirm:
- The MainWindow shows the custom label immediately (not just in Settings).
- The previously-hidden sensor's row and tray icon are still absent.
- Read `%AppData%\Torrid\config.json` directly and confirm `PollIntervalMs: 3000`, the custom label, and the hidden sensor id are all present in the file.

Report the exact file contents you observed (or ask a human to paste them, if you cannot read files outside the repo directory in your environment) as evidence, not just "it worked."

- [ ] **Step 2: Publish a self-contained single-file build**

Run:
```bash
& "C:\Program Files\dotnet\dotnet.exe" publish Torrid.App/Torrid.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

`-p:IncludeNativeLibrariesForSelfExtract=true` is required here specifically because `LibreHardwareMonitorLib` ships native runtime assets (its hardware-access helper libraries) that a single-file publish must be told to extract rather than leave as loose files next to a now-nonexistent multi-file layout — without this flag, the published exe can fail at runtime with a file-not-found error the first time it tries to touch hardware, even though it builds and launches fine.

Expected: the command succeeds and produces `Torrid.App.exe` (and a handful of native `.dll`s alongside it — self-contained single-file still needs to extract native, non-IL assets) under `Torrid.App/bin/Release/net8.0-windows10.0.17763.0/win-x64/publish/`.

- [ ] **Step 3: Verify the published build self-elevates without manual "Run as Administrator"**

Run (do NOT pass `-Verb RunAs` here — the whole point of this check is confirming the embedded manifest triggers elevation on its own, the way a normal double-click would):
```powershell
Start-Process -FilePath "Torrid.App\bin\Release\net8.0-windows10.0.17763.0\win-x64\publish\Torrid.App.exe"
```

A UAC prompt should appear automatically (this still requires human approval — the point being verified is that the prompt appears without you first right-clicking "Run as Administrator", not that it can be approved without a human). Confirm the app launches successfully after approval, and report whether the UAC prompt appeared automatically as expected.

- [ ] **Step 4: Report final state**

No commit for this task (nothing new to check in — `publish/` output is already covered by the existing `.gitignore`'s `publish/` entry). Write a summary in your task report covering: the exact config.json contents observed in Step 1, confirmation the publish command succeeded and what files it produced, and confirmation (or an honest note if you couldn't get UAC approval) that the published exe self-elevates.

---

### Task 6: Tray context menu, start-minimized, close-to-tray, and tray icon legibility

Added after trying the running app: right-clicking a tray icon did nothing (no context menu — the user expected one, referencing another app they use, PowerGenie, whose tray icon exposes "Settings"/"Exit"), the app always shows its window on launch regardless of `AppConfig.StartMinimizedToTray` (that field exists but was never wired to anything), and pressing the window's X button exits the whole app instead of minimizing to tray. The tray icon text was also reported as hard to read.

**Files:**
- Modify: `Torrid.App/App.xaml`
- Modify: `Torrid.App/App.xaml.cs`
- Modify: `Torrid.App/MainWindow.xaml.cs`
- Modify: `Torrid.App/Tray/TrayIconGroupManager.cs`
- Modify: `Torrid.App/Tray/SensorTrayIcon.cs`
- Modify: `Torrid.App/Tray/SingleValueBitmapRenderer.cs`

**Interfaces:**
- Consumes: everything already built in Tasks 1-4.
- Produces: `MainWindow.StartMinimizedToTray : bool` (read-only property, consumed by `App.xaml.cs`). `TrayIconGroupManager.{SettingsRequested, ExitRequested} : EventHandler?` (new events, alongside the existing `TrayIconClicked`).

This task has no unit-testable pure logic — every change here is WPF/WinForms lifecycle wiring or a rendering constant. Verify by building and, if you can get human UAC approval, running the app.

- [ ] **Step 1: Stop WPF from auto-showing the window, and let the app run without exiting when the window closes**

Edit `Torrid.App/App.xaml` — remove the `StartupUri="MainWindow.xaml"` attribute and add `ShutdownMode="OnExplicitShutdown"` in its place:

```xml
<Application x:Class="Torrid.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:Torrid.App"
             ShutdownMode="OnExplicitShutdown">
    <Application.Resources>
         
    </Application.Resources>
</Application>
```

Without a `StartupUri`, WPF no longer auto-creates and shows a window on launch — `App.xaml.cs`'s `OnStartup` must now do this explicitly (Step 2). `ShutdownMode="OnExplicitShutdown"` means the app keeps running even while no window is visible (e.g. minimized to tray) or during the moment between construction and `Show()` — only an explicit `Application.Current.Shutdown()` call (wired in Step 4, from the tray's "Exit" item) ends the process.

- [ ] **Step 2: Construct MainWindow explicitly in App.xaml.cs, honoring StartMinimizedToTray**

Replace `Torrid.App/App.xaml.cs` in full:

```csharp
using System.Configuration;
using System.Data;
using System.Runtime.InteropServices;
using System.Windows;

namespace Torrid.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private const string AppUserModelId = "Torrid.App";

    protected override void OnStartup(StartupEventArgs e)
    {
        var hr = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        if (hr != 0) System.Diagnostics.Debug.WriteLine($"SetCurrentProcessExplicitAppUserModelID failed: 0x{hr:X8}");
        base.OnStartup(e);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        if (!mainWindow.StartMinimizedToTray)
        {
            mainWindow.Show();
        }
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);
}
```

`MainWindow = mainWindow;` assigns the inherited `Application.MainWindow` property (a `System.Windows.Window`) — unrelated to, and not shadowed by, the `Torrid.App.MainWindow` class name used a few characters earlier in `new MainWindow()`; both resolve correctly because one is a type position and the other is a property-assignment position.

- [ ] **Step 3: Add the `StartMinimizedToTray` property, close-to-tray behavior, and tray menu wiring to MainWindow**

Modify `Torrid.App/MainWindow.xaml.cs` — replace the file in full:

```csharp
namespace Torrid.App;

using System.ComponentModel;
using System.Windows;
using Torrid.App.Notifications;
using Torrid.App.Tray;
using Torrid.App.ViewModels;
using Torrid.Core.Config;
using Torrid.Core.Notifications;
using Torrid.Core.Sensors;
using Torrid.Core.Time;

public partial class MainWindow : Window
{
    private readonly IConfigStore _configStore = new JsonConfigStore();
    private readonly AppConfig _config;
    private readonly SensorService _sensorSource;
    private readonly NotificationService _notificationService;
    private readonly TrayIconGroupManager _trayIconGroupManager;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();

        _config = _configStore.Load();
        _sensorSource = new SensorService(_config.PollIntervalMs);
        _notificationService = new NotificationService(new WindowsToastNotifier(), new SystemClock(), _config.NotificationRefireMinutes);

        DataContext = new MainWindowViewModel(_sensorSource, _config);

        _trayIconGroupManager = new TrayIconGroupManager(_sensorSource, _config);
        _trayIconGroupManager.TrayIconClicked += (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };
        _trayIconGroupManager.SettingsRequested += (_, _) => OpenSettings();
        _trayIconGroupManager.ExitRequested += (_, _) =>
        {
            _isExiting = true;
            System.Windows.Application.Current.Shutdown();
        };

        Closing += OnClosing;

        _sensorSource.ReadingsUpdated += OnReadingsUpdatedForNotifications;

        _sensorSource.Start();

        Closed += (_, _) =>
        {
            _sensorSource.Stop();
            _sensorSource.Dispose();
            _trayIconGroupManager.Dispose();
        };
    }

    public bool StartMinimizedToTray => _config.StartMinimizedToTray;

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_isExiting) return;
        e.Cancel = true;
        Hide();
    }

    private void OnReadingsUpdatedForNotifications(object? sender, IReadOnlyList<Core.Models.SensorReading> readings)
    {
        var notifiable = readings.Where(r => Core.Config.SensorDisplay.NotificationsEnabled(_config, r.Id)).ToList();
        _notificationService.Evaluate(notifiable, _config.Thresholds);
    }

    private void OpenSettings()
    {
        var settings = new SettingsWindow(_config, _sensorSource.CurrentReadings, _configStore, newPollIntervalMs => _sensorSource.PollIntervalMs = newPollIntervalMs)
        {
            Owner = this,
        };
        settings.ShowDialog();
    }
}
```

This drops the previous `MouseRightButtonUp += (_, _) => OpenSettings();` line entirely — Settings is now reached only through the tray's right-click menu (Step 4), which is more discoverable and matches the convention the user pointed to (PowerGenie). The `Closing`/`_isExiting` pair means: the X button (or any programmatic `Close()`) just hides the window unless `_isExiting` was explicitly set first by the tray's "Exit" path — this is the standard WPF minimize-to-tray pattern, and it correctly keeps the SAME `MainWindow` instance alive so `TrayIconClicked`'s `Show()` reopens the real, already-running window rather than reconstructing the whole composition root (sensor service, tray icons, config) from scratch.

- [ ] **Step 4: Add the tray context menu to `TrayIconGroupManager` and `SensorTrayIcon`**

Modify `Torrid.App/Tray/TrayIconGroupManager.cs` — replace the file in full:

```csharp
namespace Torrid.App.Tray;

using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using Torrid.Core.Config;
using Torrid.Core.Sensors;

public sealed class TrayIconGroupManager : IDisposable
{
    private readonly ISensorSource _sensorSource;
    private readonly AppConfig _config;
    private readonly Dictionary<string, SensorTrayIcon> _icons = new();
    private readonly DispatcherTimer _blinkTimer;
    private readonly ContextMenuStrip _contextMenu;
    private bool _blinkOn = true;

    public TrayIconGroupManager(ISensorSource sensorSource, AppConfig config)
    {
        _sensorSource = sensorSource;
        _config = config;

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add("Settings", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        _contextMenu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _blinkTimer.Tick += (_, _) => { _blinkOn = !_blinkOn; Redraw(); };
        _blinkTimer.Start();

        _sensorSource.ReadingsUpdated += (_, _) => Application.Current?.Dispatcher.Invoke(Redraw);

        Redraw();
    }

    public event EventHandler? TrayIconClicked;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    private void Redraw()
    {
        var visibleReadings = _sensorSource.CurrentReadings
            .Where(r => SensorDisplay.IsVisible(_config, r.Id))
            .ToList();
        var visibleIds = visibleReadings.Select(r => r.Id).ToHashSet();

        foreach (var staleId in _icons.Keys.Except(visibleIds).ToList())
        {
            _icons[staleId].Dispose();
            _icons.Remove(staleId);
        }

        foreach (var reading in visibleReadings)
        {
            if (!_icons.TryGetValue(reading.Id, out var icon))
            {
                icon = new SensorTrayIcon();
                icon.Clicked += (_, _) => TrayIconClicked?.Invoke(this, EventArgs.Empty);
                icon.AttachContextMenu(_contextMenu);
                _icons[reading.Id] = icon;
            }

            if (_config.Thresholds.TryGetValue(reading.Kind, out var band))
            {
                icon.UpdateVisual(reading, SensorDisplay.ResolveLabel(_config, reading), band, _blinkOn);
            }
        }
    }

    public void Dispose()
    {
        _blinkTimer.Stop();
        foreach (var icon in _icons.Values) icon.Dispose();
        _icons.Clear();
        _contextMenu.Dispose();
    }
}
```

One `ContextMenuStrip` instance is built once and shared across every `SensorTrayIcon` — right-clicking ANY sensor's tray icon shows the same Settings/Exit menu, which is the simplest correct behavior (there's no per-sensor menu content).

Modify `Torrid.App/Tray/SensorTrayIcon.cs` — add this method to the class (anywhere inside the class body, e.g. right after the constructor):

```csharp
    public void AttachContextMenu(System.Windows.Forms.ContextMenuStrip menu) => _notifyIcon.ContextMenuStrip = menu;
```

- [ ] **Step 5: Bump tray icon font size for legibility**

Modify `Torrid.App/Tray/SingleValueBitmapRenderer.cs` — in the `Render` method, change:

```csharp
            using var font = new Font("Segoe UI", 13, System.Drawing.FontStyle.Bold);
```

to:

```csharp
            using var font = new Font("Segoe UI", 16, System.Drawing.FontStyle.Bold);
```

The Windows shell downsizes a 32×32 source icon to the tray's actual on-screen size (16–24px depending on DPI scaling), so a larger, bolder glyph in the source bitmap reads more clearly after that downscale. This is a starting adjustment, not a final tuned value — note in your task report that further DPI-specific tuning may need another round of visual feedback, the same way Phase 1's plan already flagged this as an area needing manual iteration.

- [ ] **Step 6: Build and manually verify**

Run: `& "C:\Program Files\dotnet\dotnet.exe" build Torrid.sln`
Expected: Build succeeded, 0 errors.

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.sln`
Expected: all existing tests still pass (this task adds no new tests, so the count should be unchanged from Task 4's 61).

Run the app elevated (requires human UAC approval — you likely cannot get this yourself; say so plainly and rely on the build/tests instead if so). If you can get approval, confirm: the app starts with NO window visible when `StartMinimizedToTray` is `true` (the default) — only tray icons appear; right-clicking any tray icon shows a "Settings"/"Exit" menu; "Settings" opens the Settings window; clicking the window's X button hides it (window disappears, tray icons keep updating, process keeps running — check Task Manager or that a subsequent tray-icon left-click still reopens the SAME window with intact state) rather than exiting; only the tray's "Exit" item actually terminates the process.

- [ ] **Step 7: Commit**

```bash
git add Torrid.App/App.xaml Torrid.App/App.xaml.cs Torrid.App/MainWindow.xaml.cs Torrid.App/Tray/TrayIconGroupManager.cs Torrid.App/Tray/SensorTrayIcon.cs Torrid.App/Tray/SingleValueBitmapRenderer.cs
git commit -m "Add tray context menu (Settings/Exit), start-minimized-to-tray, close-to-tray, larger tray icon text"
```

---

### Task 7: Fix tray icon text fitting, restyle windows to plain system-native look

Two more rounds of user feedback after trying the app with Task 6's changes:

1. **Regression from Task 6 Step 5:** bumping the tray icon font from 13pt to 16pt broke rendering — reported as "numbers are wrongly displayed now" with a screenshot showing what look like single, truncated digits in the tray instead of full two-digit values. A fixed font size with no fit-checking is fragile against digit count (2-digit values today, but also `"--"` for unavailable, and in principle 3-digit values above 99°C) — this must become self-fitting, not another guessed constant.
2. **UI feedback:** "the ui is still bare and ugly cant you make it more similar to power genie." The user pointed at `C:\Users\pc\Documents\GitHub\power-genie` as their visual reference. That app uses **zero custom styling** — no theme colors, no custom fonts, no window chrome changes, just plain default WPF controls on the system's default light background, with simple `Grid`/`StackPanel` layout and modest margins (`Margin="12"` on root containers, `Margin="0,8,0,0"`/`"0,16,0,0"` between sections). Torrid's `MainWindow`/`SettingsWindow` currently hardcode a dark theme (`Background="#1E1E1E"`, `Foreground="White"` on every label) that has nothing to do with either app's actual requirements — match PowerGenie's plain-native-Windows convention: remove the custom dark theme entirely, let windows render with default system colors. Do NOT remove the color-coded temperature VALUE text (blue/green/yellow/red) — that coloring is a core functional requirement from `PROJECT_PLAN.md` section 3, not decoration, and has nothing to do with the dark-vs-light chrome question.

**Files:**
- Modify: `Torrid.App/Tray/SingleValueBitmapRenderer.cs`
- Modify: `Torrid.App/MainWindow.xaml`
- Modify: `Torrid.App/SettingsWindow.xaml`

**Interfaces:**
- Consumes: nothing new. Modifies existing rendering/XAML only — no public signatures change.
- Produces: nothing new consumed by later tasks (this is the last task in the plan).

This task has no new unit-testable pure logic (the font-fitting logic depends on `System.Drawing.Graphics` text measurement, which needs a live GDI+ graphics context and isn't meaningfully fakeable — verify by building and, if you can get human UAC approval, running and visually inspecting both the tray icons and the windows).

- [ ] **Step 1: Make the tray icon font self-fitting instead of a fixed guessed size**

Modify `Torrid.App/Tray/SingleValueBitmapRenderer.cs` — replace the file in full:

```csharp
namespace Torrid.App.Tray;

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Torrid.Core.Config;

public sealed class SingleValueBitmapRenderer
{
    private const int Size = 32;
    private const int TextMargin = 2;

    public Icon Render(double? valueCelsius, ThresholdBand thresholds, bool blinkOn)
    {
        var zone = ResolveZone(valueCelsius, thresholds);
        var isRed = zone == ColorZone.Red;
        var text = FormatValueText(valueCelsius);

        using var bitmap = new Bitmap(Size, Size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            if (isRed && !blinkOn)
            {
                using var dimBrush = new SolidBrush(Color.FromArgb(70, 244, 67, 54));
                g.FillEllipse(dimBrush, 0, 0, Size, Size);
            }

            var (r, gC, b) = ColorPalette.ForZone(zone);
            using var brush = new SolidBrush(Color.FromArgb(r, gC, b));
            using var font = CreateFittingFont(g, text, Size - TextMargin * 2, Size - TextMargin * 2);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, brush, new RectangleF(0, 0, Size, Size), format);
        }

        var handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }

    public static string FormatValueText(double? valueCelsius) =>
        valueCelsius is double v ? $"{v:F0}" : "--";

    public static ColorZone ResolveZone(double? valueCelsius, ThresholdBand thresholds) =>
        valueCelsius is double v ? ColorThresholdEvaluator.GetColor(v, thresholds) : ColorZone.Blue;

    private static Font CreateFittingFont(Graphics g, string text, float maxWidth, float maxHeight)
    {
        for (var size = 16; size >= 8; size--)
        {
            var candidate = new Font("Segoe UI", size, FontStyle.Bold);
            var measured = g.MeasureString(text, candidate);
            if (measured.Width <= maxWidth && measured.Height <= maxHeight)
            {
                return candidate;
            }
            candidate.Dispose();
        }

        return new Font("Segoe UI", 8, FontStyle.Bold);
    }
}
```

This starts at the same 16pt the user asked for last round and only shrinks when the actual measured text (via `Graphics.MeasureString`, not a guess) would overflow a 28×28 safe area inside the 32×32 icon — so `"--"` or a single digit still renders at the larger, more legible size, while `"100"` or any wider text automatically steps down until it fits, instead of silently clipping. `TextRenderingHint.AntiAliasGridFit` was also missing before (only shape smoothing was enabled, not text smoothing) — this alone measurably improves small-text legibility and may have contributed to the "wrongly displayed" complaint independent of the sizing issue.

- [ ] **Step 2: Strip the dark theme from MainWindow, keep the functional color-coded values**

Modify `Torrid.App/MainWindow.xaml` — replace the file in full:

```xml
<Window x:Class="Torrid.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:conv="clr-namespace:Torrid.App.Converters"
        Title="Torrid" Height="340" Width="360">
    <Window.Resources>
        <conv:ColorZoneToBrushConverter x:Key="ZoneToBrush"/>
    </Window.Resources>
    <ItemsControl ItemsSource="{Binding Rows}" Margin="12">
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <Grid Margin="0,4">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="{Binding Label}" FontSize="14" VerticalAlignment="Center"/>
                    <TextBlock Grid.Column="1" Text="{Binding DisplayValue}" FontSize="20" FontWeight="Bold"
                               Foreground="{Binding Zone, Converter={StaticResource ZoneToBrush}}"/>
                </Grid>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</Window>
```

Removed `Background="#1E1E1E"` from the `Window` (so it renders with the system default light background and default title bar — already standard chrome, no `WindowStyle`/`AllowsTransparency` overrides existed to begin with) and `Foreground="White"` from the label `TextBlock` (so it renders in the system default text color instead of white-on-nothing, which would be invisible against a light background). The value `TextBlock`'s `Foreground` binding through `ZoneToBrush` is UNCHANGED — the blue/green/yellow/red temperature coloring stays exactly as before, since that's functional, not part of the dark-theme chrome being removed. Margins reduced slightly (`16`→`12` root, `6`→`4` per-row) to match PowerGenie's more modest spacing convention.

- [ ] **Step 3: Strip the dark theme from SettingsWindow, keep the functional threshold-band accent colors**

Modify `Torrid.App/SettingsWindow.xaml` — replace the file in full:

```xml
<Window x:Class="Torrid.App.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Torrid Settings" Height="520" Width="480"
        WindowStartupLocation="CenterOwner">
    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,12">
            <TextBlock Text="Poll interval (ms):" VerticalAlignment="Center" Margin="0,0,8,0"/>
            <TextBox Name="PollIntervalTextBox" Width="80" Text="{Binding PollIntervalMs, UpdateSourceTrigger=PropertyChanged}"/>
            <TextBlock Text="Notification refire (min, 0=never):" VerticalAlignment="Center" Margin="16,0,8,0"/>
            <TextBox Name="RefireTextBox" Width="60" Text="{Binding NotificationRefireMinutes, UpdateSourceTrigger=PropertyChanged}"/>
        </StackPanel>

        <TabControl Grid.Row="1">
            <TabItem Header="Sensors">
                <ItemsControl ItemsSource="{Binding SensorRows}" Margin="8">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border BorderBrush="#DDDDDD" BorderThickness="0,0,0,1" Padding="0,8">
                                <StackPanel>
                                    <TextBlock Text="{Binding DefaultLabel}" FontWeight="Bold"/>
                                    <StackPanel Orientation="Horizontal" Margin="0,4,0,0">
                                        <CheckBox Content="Visible" IsChecked="{Binding IsVisible}" Margin="0,0,12,0"/>
                                        <CheckBox Content="Notify" IsChecked="{Binding NotificationsEnabled}" Margin="0,0,12,0"/>
                                        <TextBlock Text="Label:" VerticalAlignment="Center" Margin="0,0,4,0"/>
                                        <TextBox Text="{Binding CustomLabel, UpdateSourceTrigger=PropertyChanged}" Width="140"/>
                                    </StackPanel>
                                </StackPanel>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </TabItem>
            <TabItem Header="Thresholds">
                <ItemsControl ItemsSource="{Binding ThresholdRows}" Margin="8">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border BorderBrush="#DDDDDD" BorderThickness="0,0,0,1" Padding="0,8">
                                <StackPanel>
                                    <TextBlock Text="{Binding Kind}" FontWeight="Bold"/>
                                    <StackPanel Orientation="Horizontal" Margin="0,4,0,0">
                                        <TextBlock Text="Blue &lt;" Foreground="#2B6CB0" VerticalAlignment="Center" Margin="0,0,4,0"/>
                                        <TextBox Text="{Binding BlueMax, UpdateSourceTrigger=PropertyChanged}" Width="50" Margin="0,0,12,0"/>
                                        <TextBlock Text="Green &lt;" Foreground="#2F855A" VerticalAlignment="Center" Margin="0,0,4,0"/>
                                        <TextBox Text="{Binding GreenMax, UpdateSourceTrigger=PropertyChanged}" Width="50" Margin="0,0,12,0"/>
                                        <TextBlock Text="Yellow &lt;=" Foreground="#B7791F" VerticalAlignment="Center" Margin="0,0,4,0"/>
                                        <TextBox Text="{Binding YellowMax, UpdateSourceTrigger=PropertyChanged}" Width="50"/>
                                    </StackPanel>
                                </StackPanel>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </TabItem>
        </TabControl>

        <CheckBox Grid.Row="2" Content="Start minimized to tray" IsChecked="{Binding StartMinimizedToTray}" Margin="0,12,0,0"/>

        <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,12,0,0">
            <Button Name="SaveButton" Content="Save" Width="80" Margin="0,0,8,0" Click="OnSaveClick"/>
            <Button Name="CancelButton" Content="Cancel" Width="80" Click="OnCancelClick"/>
        </StackPanel>
    </Grid>
</Window>
```

Every hardcoded `Background="#1E1E1E"` (on the `Window` and the `TabControl`) and `Foreground="White"` is removed, letting all of it render with default system light-theme colors and the standard title bar — matching PowerGenie's "no custom chrome at all" convention. The border color between rows changed from a dark-theme gray (`#3A3A3A`) to a light-theme-appropriate one (`#DDDDDD`), and the Blue/Green/Yellow threshold-band accent colors were darkened slightly (`#429CF5`→`#2B6CB0`, `#4CAF50`→`#2F855A`, `#FFC107`→`#B7791F`) so they stay legible as text on a white background instead of the original values, which were tuned for a dark background — the intent (identify which numeric field is which threshold) is unchanged, only the exact hex values shift for contrast on light.

- [ ] **Step 4: Build and manually verify**

Run: `& "C:\Program Files\dotnet\dotnet.exe" build Torrid.sln`
Expected: Build succeeded, 0 errors.

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.sln`
Expected: all existing tests still pass (this task adds no new tests, count unchanged).

Run the app elevated (requires human UAC approval — you likely cannot get this yourself; say so plainly and rely on the build/tests instead if so). If you can get approval, confirm: tray icons show fully-legible two-digit numbers with no visible clipping; the main window has a plain white/system background with standard title bar, readable default-colored labels, and colored (blue/green/yellow/red) temperature values exactly as before; the Settings window matches the same plain-background convention with legible threshold accent-colored labels.

- [ ] **Step 5: Commit**

```bash
git add Torrid.App/Tray/SingleValueBitmapRenderer.cs Torrid.App/MainWindow.xaml Torrid.App/SettingsWindow.xaml
git commit -m "Fix tray icon text clipping with self-fitting font; restyle windows to plain system-native look"
```

---

### Task 8: Non-elevated Debug builds for fast iteration

Every visual-iteration round so far has required a human to approve a UAC prompt to launch the app, and — twice — to manually close an orphaned elevated process afterward, because an elevated process can't be terminated by non-elevated automation (`Stop-Process`/`taskkill` both return "Access is denied"). This is real, repeated friction, and the user explicitly asked for a way to iterate without it.

`Torrid.App.csproj` embeds `app.manifest` (`requestedExecutionLevel level="requireAdministrator"`) into every build — Debug included — which is why even a plain `dotnet run` during development triggers UAC. The fix: give Debug builds a *different* manifest that requests `asInvoker` (no elevation) instead, while Release builds (what `PROJECT_PLAN.md`'s acceptance criteria and this plan's Task 5 packaging step actually care about) keep `requireAdministrator` exactly as before.

This has one real tradeoff, not a bug to fix: per `tools/SensorProbe/FINDINGS.md` (Phase 0), the CPU temperature sensor (`Core (Tctl/Tdie)`) reads `0.0` without elevation — so a non-elevated dev build will show a real (if incorrect-looking, ~0°C, blue) CPU value while GPU Core/Hot Spot/VRAM temps read correctly even unelevated. That's an acceptable, known limitation for a fast dev loop, not something this task tries to work around.

**Files:**
- Create: `Torrid.App/app.dev.manifest`
- Modify: `Torrid.App/Torrid.App.csproj`

**Interfaces:**
- Consumes: nothing new.
- Produces: nothing consumed by later tasks — this is the last task in the plan.

No new tests (build-configuration change, not application logic).

- [ ] **Step 1: Create the non-elevated manifest**

Create `Torrid.App/app.dev.manifest` as an exact copy of `Torrid.App/app.manifest` except the execution level:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="Torrid.App"/>
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="asInvoker" uiAccess="false"/>
      </requestedPrivileges>
    </security>
  </trustInfo>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/PM</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **Step 2: Make the manifest choice depend on build configuration**

Read the current `Torrid.App/Torrid.App.csproj` first (it's short, ~15 lines) so your edit lands on the real file. Find the line:

```xml
    <ApplicationManifest>app.manifest</ApplicationManifest>
```

Replace it with:

```xml
    <ApplicationManifest Condition="'$(Configuration)' == 'Debug'">app.dev.manifest</ApplicationManifest>
    <ApplicationManifest Condition="'$(Configuration)' != 'Debug'">app.manifest</ApplicationManifest>
```

Do not change anything else in the file.

- [ ] **Step 3: Build both configurations and confirm the manifests differ**

Run: `& "C:\Program Files\dotnet\dotnet.exe" build Torrid.sln -c Debug`
Expected: Build succeeded, 0 errors.

Run: `& "C:\Program Files\dotnet\dotnet.exe" build Torrid.App/Torrid.App.csproj -c Release`
Expected: Build succeeded, 0 errors.

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.sln`
Expected: all existing tests still pass (this task adds no new tests, count unchanged).

Confirm the manifests actually differ by checking the embedded execution level in each built exe. `sigcheck`/`mt.exe` aren't guaranteed available — instead just search the binary for the literal string, which reliably works for an uncompressed embedded manifest:

```powershell
Select-String -Path "Torrid.App\bin\Debug\net8.0-windows10.0.17763.0\Torrid.App.exe" -Pattern "asInvoker" -Encoding utf8
Select-String -Path "Torrid.App\bin\Release\net8.0-windows10.0.17763.0\Torrid.App.exe" -Pattern "requireAdministrator" -Encoding utf8
```

Expected: the Debug exe's search finds `asInvoker`, the Release exe's search finds `requireAdministrator`. If a search finds nothing, do not guess why — report it plainly as a verification failure in your report rather than assuming success.

Do NOT run the Debug build's exe as part of this task (`dotnet run` or launching the exe directly) — the point of this task is to make that safe for the controller/human to do freely afterward, not to verify it here. Your verification is the manifest-content check above plus the build/test results.

- [ ] **Step 4: Commit**

```bash
git add Torrid.App/app.dev.manifest Torrid.App/Torrid.App.csproj
git commit -m "Use asInvoker manifest for Debug builds so dev iteration doesn't require elevation"
```

---

### Task 9: Fix tray right-click bug, group MainWindow by device, bigger numbers

Three more rounds of feedback after trying the restyled app:

1. **Real bug:** right-clicking a tray icon was observed to also open the main window, not just show the Settings/Exit menu. This is a well-known WinForms `NotifyIcon` gotcha — its `Click` event fires for ANY mouse button (left or right), not just left, regardless of a `ContextMenuStrip` also being attached. `SensorTrayIcon` currently wires `Clicked` off `Click`, so a right-click fires both the context menu AND the "show window" behavior. The fix is to use `MouseUp` and filter to `MouseButtons.Left` explicitly.
2. **"can we make the numbers bigger?"** — the MainWindow's temperature values are the app's core glanceable readout; increase their size.
3. **"the window with live temps need to be improved aesthetically and functionally"** (the user confirmed keeping two separate windows — Settings stays a separate popup, only `MainWindow` needs work): group rows by device (a "CPU" header, then a "GPU 0" header, etc. — using `SensorReading.GpuIndex`, already available, `null` = CPU) instead of one flat list, and size the window to fit its actual content instead of leaving a large empty area below 4 rows (today's fixed `Height="340"` was sized for the original always-visible-7-rows Phase 1 design; both content and Settings' visibility toggle can now make the real row count vary a lot).

**Files:**
- Modify: `Torrid.App/Tray/SensorTrayIcon.cs`
- Modify: `Torrid.App/ViewModels/SensorRowViewModel.cs`
- Modify: `Torrid.App/ViewModels/MainWindowViewModel.cs`
- Modify: `Torrid.App/MainWindow.xaml`
- Test: `Torrid.Tests/MainWindowViewModelTests.cs` (add one test for device grouping)

**Interfaces:**
- Consumes: `Torrid.Core.Models.SensorReading.GpuIndex : int?` (already exists).
- Produces: `SensorRowViewModel.DeviceName : string` (new, read-only, set at construction) — this changes `SensorRowViewModel`'s constructor from `SensorRowViewModel(string label)` to `SensorRowViewModel(string label, string deviceName)`; the only production call site is in `MainWindowViewModel.Apply`, updated in this same task.

- [ ] **Step 1: Fix the right-click-also-opens-window bug**

Modify `Torrid.App/Tray/SensorTrayIcon.cs` — replace this line in the constructor:

```csharp
        _notifyIcon.Click += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
```

with:

```csharp
        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                Clicked?.Invoke(this, EventArgs.Empty);
            }
        };
```

Nothing else in this file changes — `using System.Windows.Forms;` (already present) covers both `NotifyIcon` and `MouseButtons`.

- [ ] **Step 2: Write the failing test for device grouping**

Add this test to `Torrid.Tests/MainWindowViewModelTests.cs` (append it inside the existing `MainWindowViewModelTests` class, alongside the tests already there from Task 2 — do not remove or modify the existing tests):

```csharp
    [Fact]
    public void Apply_SetsDeviceNameFromGpuIndex()
    {
        var source = new FakeSensorSourceForTests();
        var config = new AppConfig();
        var viewModel = new MainWindowViewModel(source, config);

        source.Publish(new[] { Cpu(), Gpu0Core() });

        var cpuRow = viewModel.Rows.Single(r => r.Label == "CPU");
        var gpuRow = viewModel.Rows.Single(r => r.Label == "GPU0 Core");
        Assert.Equal("CPU", cpuRow.DeviceName);
        Assert.Equal("GPU 0", gpuRow.DeviceName);
    }
```

- [ ] **Step 3: Run test to verify it fails**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter Apply_SetsDeviceNameFromGpuIndex`
Expected: FAIL to build — `SensorRowViewModel.DeviceName` doesn't exist yet, and the two-argument constructor doesn't exist yet.

- [ ] **Step 4: Add `DeviceName` to `SensorRowViewModel`**

Modify `Torrid.App/ViewModels/SensorRowViewModel.cs` — replace the constructor and add the property:

```csharp
    public SensorRowViewModel(string label, string deviceName)
    {
        Label = label;
        DeviceName = deviceName;
    }

    public string Label { get; }

    public string DeviceName { get; }
```

(This replaces the old one-line `public SensorRowViewModel(string label) => Label = label;` and the old standalone `public string Label { get; }` — the rest of the file, including `DisplayValue`/`Zone`/`Update`/`PropertyChanged`, is unchanged.)

- [ ] **Step 5: Update `MainWindowViewModel` to pass the device name**

Modify `Torrid.App/ViewModels/MainWindowViewModel.cs` — change the row-construction line inside `Apply`:

```csharp
                row = new SensorRowViewModel(SensorDisplay.ResolveLabel(_config, reading));
```

to:

```csharp
                row = new SensorRowViewModel(SensorDisplay.ResolveLabel(_config, reading), DeviceNameFor(reading));
```

and add this private static method to the class (anywhere in the class body, e.g. right after `Apply`):

```csharp
    private static string DeviceNameFor(SensorReading reading) =>
        reading.GpuIndex is int index ? $"GPU {index}" : "CPU";
```

`SensorReading` is already in scope via the existing `using Torrid.Core.Models;` at the top of this file.

- [ ] **Step 6: Run test to verify it passes**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter MainWindowViewModelTests`
Expected: PASS, 4/4 (3 existing from Task 2 + this new one).

- [ ] **Step 7: Run the full suite to confirm no regressions**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.sln`
Expected: PASS, all tests (61 before this task + 1 new = 62).

- [ ] **Step 8: Commit the view model changes**

```bash
git add Torrid.App/Tray/SensorTrayIcon.cs Torrid.App/ViewModels/SensorRowViewModel.cs Torrid.App/ViewModels/MainWindowViewModel.cs Torrid.Tests/MainWindowViewModelTests.cs
git commit -m "Fix tray right-click also triggering window-open; add DeviceName for grouping"
```

- [ ] **Step 9: Redesign MainWindow.xaml — device-grouped rows, bigger values, content-sized window**

Replace `Torrid.App/MainWindow.xaml` in full:

```xml
<Window x:Class="Torrid.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:conv="clr-namespace:Torrid.App.Converters"
        Title="Torrid" Width="340" SizeToContent="Height" MinHeight="120">
    <Window.Resources>
        <conv:ColorZoneToBrushConverter x:Key="ZoneToBrush"/>
        <CollectionViewSource x:Key="GroupedRows" Source="{Binding Rows}">
            <CollectionViewSource.GroupDescriptions>
                <PropertyGroupDescription PropertyName="DeviceName"/>
            </CollectionViewSource.GroupDescriptions>
        </CollectionViewSource>
    </Window.Resources>
    <ItemsControl ItemsSource="{Binding Source={StaticResource GroupedRows}}" Margin="14">
        <ItemsControl.GroupStyle>
            <GroupStyle>
                <GroupStyle.HeaderTemplate>
                    <DataTemplate>
                        <TextBlock Text="{Binding Name}" FontWeight="Bold" FontSize="13"
                                   Foreground="#777777" Margin="0,10,0,2"/>
                    </DataTemplate>
                </GroupStyle.HeaderTemplate>
            </GroupStyle>
        </ItemsControl.GroupStyle>
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <Grid Margin="0,3">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="{Binding Label}" FontSize="15" VerticalAlignment="Center"/>
                    <TextBlock Grid.Column="1" Text="{Binding DisplayValue}" FontSize="30" FontWeight="Bold"
                               Foreground="{Binding Zone, Converter={StaticResource ZoneToBrush}}"/>
                </Grid>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</Window>
```

Notes on what changed and why:
- `Title`/`Width` kept, but the fixed `Height="340"` is replaced with `SizeToContent="Height"` plus a `MinHeight="120"` floor — the window now grows or shrinks to fit however many sensor rows are actually visible (which varies once Settings can hide sensors), instead of always reserving space for a fixed row count.
- `CollectionViewSource` with a `PropertyGroupDescription` on the new `DeviceName` groups rows by device automatically and stays in sync with the underlying `ObservableCollection<SensorRowViewModel>` (including the existing add/remove-on-visibility-change behavior from Task 2) — WPF's collection views track `ObservableCollection` changes without any extra wiring.
- `GroupStyle.HeaderTemplate` renders a small bold gray label ("CPU", "GPU 0", "GPU 1", ...) above each device's rows.
- The value `FontSize` goes from `20` to `30` (bigger numbers, per feedback); the label `FontSize` goes from `14` to `15` to stay visually balanced against the larger value. The `Foreground="{Binding Zone, Converter={StaticResource ZoneToBrush}}"` binding on the value — the functional color-coding — is unchanged.
- Root `Margin` increased slightly (`12`→`14`) and per-row `Margin` reduced (`0,4`→`0,3`) since group headers now provide most of the vertical rhythm between sections.

- [ ] **Step 10: Build and manually verify**

Run: `& "C:\Program Files\dotnet\dotnet.exe" build Torrid.sln`
Expected: Build succeeded, 0 errors.

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.sln`
Expected: 62/62 passing.

If Task 8 has already landed by the time you do this task, use the now-non-elevated Debug build to verify without needing UAC approval: run `& "C:\Program Files\dotnet\dotnet.exe" run --project Torrid.App` directly and confirm you can launch, observe, and close it yourself without any elevation prompt. If Task 8 has NOT landed yet (tasks in this plan execute in order, so this shouldn't normally happen), fall back to the elevated-launch-requires-human-approval note used by every earlier task. Confirm: right-clicking a tray icon shows ONLY the Settings/Exit menu (the main window does not also pop up); the main window shows a "CPU" section header above the CPU row, and a "GPU 0" header above that GPU's three rows (and "GPU 1", etc. if more are present); temperature values are visibly larger and still colored blue/green/yellow/red; the window's height roughly matches its content instead of leaving a large empty area.

- [ ] **Step 11: Commit**

```bash
git add Torrid.App/MainWindow.xaml
git commit -m "Group MainWindow rows by device, enlarge temperature values, size window to content"
```
