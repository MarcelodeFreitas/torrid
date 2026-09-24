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
    public void Apply_UncheckingVisible_AddsSensorIdToVisibleSensorIds()
    {
        var config = new AppConfig();
        var viewModel = new SettingsViewModel(config, new[] { Cpu(), Gpu0Core() });
        var cpuRow = viewModel.SensorRows.Single(r => r.SensorId == "cpu");

        cpuRow.IsVisible = false;
        viewModel.Apply();

        Assert.Contains("cpu", config.VisibleSensorIds);
        Assert.DoesNotContain("gpu0-core", config.VisibleSensorIds);
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
