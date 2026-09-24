# Torrid Phase 1 (Core, MainWindow, Tray, Notifications) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the four independent Phase 1 subsystems from `PROJECT_PLAN.md` section 8 — Sensor Service + Config, Main Window UI, Tray Icon Renderer, Notification Service — each against a pre-defined stub contract so they compile and are individually testable before Phase 2 wires them together against real hardware.

**Architecture:** `Torrid.Core` (net8.0, no UI dependency) owns the domain model, sensor-reading construction, threshold/color logic, JSON config persistence, and notification debounce logic — all pure and unit-tested. `Torrid.App` (net8.0-windows, WPF) owns `SensorService` (the real LibreHardwareMonitor integration), the MainWindow UI, the tray icon renderer, and the concrete Windows toast notifier — these are integration-heavy and are verified by running the app, not by fake-hardware unit tests. `Torrid.Tests` (xunit) covers everything in `Torrid.Core` plus the handful of pure functions inside `Torrid.App` (color palette, tooltip text).

**Tech Stack:** C#/.NET 8, WPF, LibreHardwareMonitorLib 0.9.6, System.Text.Json, System.Windows.Forms (for `NotifyIcon`, referenced from the WPF project), xunit.

**Spec:** `PROJECT_PLAN.md` (repo root) — sections 3, 4, 6, 7, 8 (Phase 1), 9. `tools/SensorProbe/FINDINGS.md` — Phase 0 ground truth this plan builds on.

## Global Constraints

- Solution layout is already scaffolded and committed: `Torrid.sln`, `Torrid.Core/Torrid.Core.csproj` (net8.0, `LibreHardwareMonitorLib` 0.9.6 referenced), `Torrid.App/Torrid.App.csproj` (net8.0-windows, WPF, references `Torrid.Core`, `app.manifest` requests `requireAdministrator`), `Torrid.Tests/Torrid.Tests.csproj` (**net8.0-windows** — not plain net8.0; the SDK forbids a net8.0 project referencing a net8.0-windows one, and Tasks 2-4 test App-side classes — xunit, references both `Torrid.Core` and `Torrid.App`). Do not create new solution/project files — add source files into these.
- Confirmed real sensor names (Phase 0, see `tools/SensorProbe/FINDINGS.md`) — build against these exact strings, not placeholders:
  - CPU package temp: hardware with `HardwareType.Cpu`, sensor `SensorType.Temperature` named exactly `"Core (Tctl/Tdie)"`.
  - GPU core/hot-spot/VRAM temps: hardware with `HardwareType.GpuNvidia` (also handle `GpuAmd`/`GpuIntel` — LibreHardwareMonitorLib has no single generic "Gpu" `HardwareType` value, unlike `PROJECT_PLAN.md`'s architecture note; loop over all three vendor-specific enum values), sensors `SensorType.Temperature` named exactly `"GPU Core"`, `"GPU Hot Spot"`, `"GPU Memory Junction"`.
- Must support **1..N GPUs generically** — confirmed with the user 2026-09-24: do not hardcode to exactly 2 GPUs or to MSI/EVGA branding (the sensor API exposes no vendor-model string; branding is a future Settings-assigned label, out of scope for this plan). GPUs are distinguished by enumeration order (`gpu0`, `gpu1`, ...).
- Default thresholds — copy exactly from `PROJECT_PLAN.md` section 4:

  | SensorKind | BlueMax | GreenMax | YellowMax |
  |---|---|---|---|
  | CpuPackage | 40 | 70 | 85 |
  | GpuCore | 40 | 75 | 85 |
  | GpuHotSpot | 50 | 85 | 95 |
  | GpuVram | 50 | 85 | 95 |

  Color banding convention (resolves the source table's inclusive/inclusive boundary overlap — pin this exact rule, do not invent another): `value < BlueMax` → Blue; `BlueMax <= value < GreenMax` → Green; `GreenMax <= value <= YellowMax` → Yellow; `value > YellowMax` → Red.
- Namespaces: `Torrid.Core.Models`, `Torrid.Core.Sensors`, `Torrid.Core.Config`, `Torrid.Core.Notifications`, `Torrid.Core.Time` in Core; `Torrid.App`, `Torrid.App.Converters`, `Torrid.App.Sensors`, `Torrid.App.ViewModels`, `Torrid.App.Tray`, `Torrid.App.Notifications` in App.
- `dotnet build Torrid.sln` (using `C:\Program Files\dotnet\dotnet.exe` — the `dotnet` on PATH resolves to a copy with no SDK installed) must succeed after every task's commit. Run tests with the same full path: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.sln`.
- Every task that touches `Torrid.Core` logic follows strict TDD (failing test → implementation → passing test). Tasks 2 and 3's WPF/GDI+ rendering and wiring code has no meaningful unit-testable red/green cycle for the rendering itself — pull every pure decision (color selection, tooltip text) into a small static function that *is* unit tested, and verify the rendering/wiring by running the app (`dotnet run --project Torrid.App`) and visually confirming, per that task's manual verification step.
- Tasks run sequentially, one implementer at a time (not literally in parallel) — this is a deliberate deviation from `PROJECT_PLAN.md`'s "dispatch in parallel" phrasing: `superpowers:subagent-driven-development`'s own process forbids concurrent implementer dispatch (git conflict risk). Because this plan fully specifies every shared interface up front (below), each task's implementer does not need the previous task's subagent to finish reasoning about the contract — only its committed files, which sequential dispatch already guarantees.

### Shared contract (all tasks build against these exact names)

```csharp
// Torrid.Core/Models/SensorKind.cs
namespace Torrid.Core.Models;

public enum SensorKind
{
    CpuPackage,
    GpuCore,
    GpuHotSpot,
    GpuVram
}
```

```csharp
// Torrid.Core/Models/SensorReading.cs
namespace Torrid.Core.Models;

public sealed record SensorReading(
    string Id,
    string Label,
    SensorKind Kind,
    int? GpuIndex,
    double? ValueCelsius);
```

```csharp
// Torrid.Core/Sensors/ISensorSource.cs
namespace Torrid.Core.Sensors;

using Torrid.Core.Models;

public interface ISensorSource
{
    event EventHandler<IReadOnlyList<SensorReading>>? ReadingsUpdated;
    IReadOnlyList<SensorReading> CurrentReadings { get; }
    void Start();
    void Stop();
}
```

```csharp
// Torrid.Core/Config/ColorZone.cs
namespace Torrid.Core.Config;

public enum ColorZone { Blue, Green, Yellow, Red }
```

```csharp
// Torrid.Core/Config/ThresholdBand.cs
namespace Torrid.Core.Config;

public sealed class ThresholdBand
{
    public double BlueMax { get; set; }
    public double GreenMax { get; set; }
    public double YellowMax { get; set; }
}
```

```csharp
// Torrid.Core/Config/AppConfig.cs (fields only — full listing in Task 1)
namespace Torrid.Core.Config;

using Torrid.Core.Models;

public sealed class AppConfig
{
    public int PollIntervalMs { get; set; } = 2000;
    public Dictionary<SensorKind, ThresholdBand> Thresholds { get; set; } = DefaultThresholds();
    public HashSet<string> VisibleSensorIds { get; set; } = new();
    public int NotificationRefireMinutes { get; set; } = 0;
    public bool StartMinimizedToTray { get; set; } = true;

    public static Dictionary<SensorKind, ThresholdBand> DefaultThresholds() => new()
    {
        [SensorKind.CpuPackage] = new ThresholdBand { BlueMax = 40, GreenMax = 70, YellowMax = 85 },
        [SensorKind.GpuCore] = new ThresholdBand { BlueMax = 40, GreenMax = 75, YellowMax = 85 },
        [SensorKind.GpuHotSpot] = new ThresholdBand { BlueMax = 50, GreenMax = 85, YellowMax = 95 },
        [SensorKind.GpuVram] = new ThresholdBand { BlueMax = 50, GreenMax = 85, YellowMax = 95 },
    };
}
```

**Interfaces summary (who consumes/produces what):**
- Task 1 produces: `SensorKind`, `SensorReading`, `ISensorSource`, `ColorZone`, `ThresholdBand`, `ColorThresholdEvaluator.GetColor`, `AppConfig`, `IConfigStore`/`JsonConfigStore`, `SensorService` (real), `HardwareSnapshot`/`SensorSnapshot`/`SensorReadingBuilder` (testable adapter layer).
- Task 2 consumes: all of Task 1's `Torrid.Core` types. Produces: `Torrid.App.Sensors.FakeSensorSource`, `Torrid.App.ColorPalette`, `Torrid.App.Converters.ColorZoneToBrushConverter`, `Torrid.App.ViewModels.SensorRowViewModel`/`MainWindowViewModel`, `MainWindow`.
- Task 3 consumes: Task 1's Core types + Task 2's `FakeSensorSource` and `ColorPalette` (do not redefine either — reference them). Produces: `Torrid.App.Tray.TooltipTextBuilder`, `TrayBitmapRenderer`, `TrayIconManager`.
- Task 4 consumes: Task 1's `SensorReading`, `ColorThresholdEvaluator`, `ThresholdBand`, `SensorKind`. Produces: `Torrid.Core.Time.IClock`/`SystemClock`, `Torrid.Core.Notifications.IToastNotifier`/`NotificationService`, `Torrid.App.Notifications.WindowsToastNotifier`.

## Review Focus

1. GPU count is 0 (all GPUs removed, or a driver issue) — `SensorReadingBuilder.Build` and every downstream consumer must produce a CPU-only reading list without throwing, not assume at least one GPU exists.
2. A sensor's value is temporarily unavailable (LibreHardwareMonitor returns `null`) — color evaluation and the tray/tooltip renderers must render a neutral placeholder (`"--"`), never throw on a null `double?`.
3. A value sits exactly on a threshold boundary (e.g. CPU temp exactly `40.0`, `70.0`, or `85.0`) — color banding must follow the exact convention pinned in Global Constraints, not silently misclassify at the edges.
4. A sensor oscillates right at the red boundary across polls (e.g. `84.9` / `85.1` flapping) — `NotificationService` must not fire a toast on every tick; only once on the real crossing into red, with hysteresis reset only after a genuine drop back out of red.
5. The config file on disk is missing or corrupted at startup — `JsonConfigStore.Load` must fall back to `new AppConfig()` defaults rather than throwing and crashing the app.

---

### Task 1: Sensor Service + Config Model

**Files:**
- Create: `Torrid.Core/Models/SensorKind.cs`
- Create: `Torrid.Core/Models/SensorReading.cs`
- Create: `Torrid.Core/Sensors/ISensorSource.cs`
- Create: `Torrid.Core/Sensors/HardwareSnapshot.cs`
- Create: `Torrid.Core/Sensors/SensorReadingBuilder.cs`
- Create: `Torrid.Core/Sensors/SensorService.cs`
- Create: `Torrid.Core/Config/ColorZone.cs`
- Create: `Torrid.Core/Config/ThresholdBand.cs`
- Create: `Torrid.Core/Config/ColorThresholdEvaluator.cs`
- Create: `Torrid.Core/Config/AppConfig.cs`
- Create: `Torrid.Core/Config/IConfigStore.cs`
- Create: `Torrid.Core/Config/JsonConfigStore.cs`
- Test: `Torrid.Tests/SensorReadingBuilderTests.cs`
- Test: `Torrid.Tests/ColorThresholdEvaluatorTests.cs`
- Test: `Torrid.Tests/JsonConfigStoreTests.cs`

**Interfaces:**
- Consumes: nothing (first task).
- Produces: everything listed in "Shared contract" above, plus:
  - `Torrid.Core.Sensors.HardwareSnapshot` / `SensorSnapshot` — an owned DTO that `SensorService` copies real `LibreHardwareMonitor.Hardware.IHardware`/`ISensor` data into each poll, so reading-construction logic is unit-testable without faking LibreHardwareMonitor's own interfaces (some of which use sealed non-trivial types).
  - `Torrid.Core.Sensors.SensorReadingBuilder.Build(IReadOnlyList<HardwareSnapshot>) -> IReadOnlyList<SensorReading>` — the pure, fully-tested transformation.
  - `Torrid.Core.Config.ColorThresholdEvaluator.GetColor(double value, ThresholdBand thresholds) -> ColorZone`.
  - `Torrid.Core.Config.IConfigStore { AppConfig Load(); void Save(AppConfig config); }` and `JsonConfigStore : IConfigStore` (constructor takes optional `string? path` override — later tasks/tests must be able to point it at a temp file).
  - `Torrid.Core.Sensors.SensorService : ISensorSource, IDisposable` — the real LibreHardwareMonitor integration (constructor `SensorService(int pollIntervalMs)`).

- [ ] **Step 1: Add the shared model files (no tests needed — plain data types)**

Create `Torrid.Core/Models/SensorKind.cs`:

```csharp
namespace Torrid.Core.Models;

public enum SensorKind
{
    CpuPackage,
    GpuCore,
    GpuHotSpot,
    GpuVram
}
```

Create `Torrid.Core/Models/SensorReading.cs`:

```csharp
namespace Torrid.Core.Models;

public sealed record SensorReading(
    string Id,
    string Label,
    SensorKind Kind,
    int? GpuIndex,
    double? ValueCelsius);
```

Create `Torrid.Core/Sensors/ISensorSource.cs`:

```csharp
namespace Torrid.Core.Sensors;

using Torrid.Core.Models;

public interface ISensorSource
{
    event EventHandler<IReadOnlyList<SensorReading>>? ReadingsUpdated;
    IReadOnlyList<SensorReading> CurrentReadings { get; }
    void Start();
    void Stop();
}
```

- [ ] **Step 2: Write the failing test for color threshold banding**

Create `Torrid.Tests/ColorThresholdEvaluatorTests.cs`:

```csharp
using Torrid.Core.Config;

namespace Torrid.Tests;

public class ColorThresholdEvaluatorTests
{
    private static readonly ThresholdBand Cpu = new() { BlueMax = 40, GreenMax = 70, YellowMax = 85 };

    [Theory]
    [InlineData(39.9, ColorZone.Blue)]
    [InlineData(40.0, ColorZone.Green)]
    [InlineData(69.9, ColorZone.Green)]
    [InlineData(70.0, ColorZone.Yellow)]
    [InlineData(84.9, ColorZone.Yellow)]
    [InlineData(85.0, ColorZone.Yellow)]
    [InlineData(85.1, ColorZone.Red)]
    [InlineData(120.0, ColorZone.Red)]
    public void GetColor_ReturnsExpectedZone_AtEachBoundary(double value, ColorZone expected)
    {
        var actual = ColorThresholdEvaluator.GetColor(value, Cpu);

        Assert.Equal(expected, actual);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter ColorThresholdEvaluatorTests`
Expected: FAIL to build — `ColorThresholdEvaluator` does not exist yet.

- [ ] **Step 4: Implement `ColorThresholdEvaluator`**

Create `Torrid.Core/Config/ColorZone.cs`:

```csharp
namespace Torrid.Core.Config;

public enum ColorZone { Blue, Green, Yellow, Red }
```

Create `Torrid.Core/Config/ThresholdBand.cs`:

```csharp
namespace Torrid.Core.Config;

public sealed class ThresholdBand
{
    public double BlueMax { get; set; }
    public double GreenMax { get; set; }
    public double YellowMax { get; set; }
}
```

Create `Torrid.Core/Config/ColorThresholdEvaluator.cs`:

```csharp
namespace Torrid.Core.Config;

public static class ColorThresholdEvaluator
{
    public static ColorZone GetColor(double value, ThresholdBand thresholds)
    {
        if (value < thresholds.BlueMax) return ColorZone.Blue;
        if (value < thresholds.GreenMax) return ColorZone.Green;
        if (value <= thresholds.YellowMax) return ColorZone.Yellow;
        return ColorZone.Red;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter ColorThresholdEvaluatorTests`
Expected: PASS, 8/8.

- [ ] **Step 6: Commit**

```bash
git add Torrid.Core/Models/SensorKind.cs Torrid.Core/Models/SensorReading.cs Torrid.Core/Sensors/ISensorSource.cs Torrid.Core/Config/ColorZone.cs Torrid.Core/Config/ThresholdBand.cs Torrid.Core/Config/ColorThresholdEvaluator.cs Torrid.Tests/ColorThresholdEvaluatorTests.cs
git commit -m "Add sensor/color domain model and threshold evaluator"
```

- [ ] **Step 7: Write the failing test for `SensorReadingBuilder`**

Create `Torrid.Core/Sensors/HardwareSnapshot.cs`:

```csharp
namespace Torrid.Core.Sensors;

using LibreHardwareMonitor.Hardware;

public sealed record HardwareSnapshot(
    HardwareType HardwareType,
    string Name,
    IReadOnlyList<SensorSnapshot> Sensors,
    IReadOnlyList<HardwareSnapshot> SubHardware);

public sealed record SensorSnapshot(
    string Name,
    SensorType SensorType,
    double? Value);
```

Create `Torrid.Tests/SensorReadingBuilderTests.cs`:

```csharp
using LibreHardwareMonitor.Hardware;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

namespace Torrid.Tests;

public class SensorReadingBuilderTests
{
    private static SensorSnapshot Temp(string name, double? value) => new(name, SensorType.Temperature, value);

    [Fact]
    public void Build_WithCpuAndOneGpu_ProducesFourReadings()
    {
        var hardware = new List<HardwareSnapshot>
        {
            new(HardwareType.Cpu, "AMD Ryzen 9 5950X",
                new[] { Temp("Core (Tctl/Tdie)", 59.0), Temp("SoC", 38.0) },
                Array.Empty<HardwareSnapshot>()),
            new(HardwareType.GpuNvidia, "NVIDIA GeForce RTX 3090",
                new[] { Temp("GPU Core", 41.0), Temp("GPU Hot Spot", 51.0), Temp("GPU Memory Junction", 50.0) },
                Array.Empty<HardwareSnapshot>()),
        };

        var readings = SensorReadingBuilder.Build(hardware);

        Assert.Equal(4, readings.Count);
        var cpu = Assert.Single(readings, r => r.Kind == SensorKind.CpuPackage);
        Assert.Equal("cpu", cpu.Id);
        Assert.Null(cpu.GpuIndex);
        Assert.Equal(59.0, cpu.ValueCelsius);

        var gpuCore = Assert.Single(readings, r => r.Kind == SensorKind.GpuCore);
        Assert.Equal("gpu0-core", gpuCore.Id);
        Assert.Equal(0, gpuCore.GpuIndex);
        Assert.Equal(41.0, gpuCore.ValueCelsius);
    }

    [Fact]
    public void Build_WithTwoGpus_LabelsThemByIndex()
    {
        HardwareSnapshot Gpu(string name) => new(HardwareType.GpuNvidia, name,
            new[] { Temp("GPU Core", 40.0), Temp("GPU Hot Spot", 50.0), Temp("GPU Memory Junction", 50.0) },
            Array.Empty<HardwareSnapshot>());

        var hardware = new List<HardwareSnapshot> { Gpu("RTX 3090"), Gpu("RTX 3090") };

        var readings = SensorReadingBuilder.Build(hardware);

        Assert.Contains(readings, r => r.Id == "gpu0-core" && r.GpuIndex == 0);
        Assert.Contains(readings, r => r.Id == "gpu1-core" && r.GpuIndex == 1);
    }

    [Fact]
    public void Build_WithNoGpu_ProducesCpuOnlyWithoutThrowing()
    {
        var hardware = new List<HardwareSnapshot>
        {
            new(HardwareType.Cpu, "AMD Ryzen 9 5950X",
                new[] { Temp("Core (Tctl/Tdie)", 59.0) }, Array.Empty<HardwareSnapshot>()),
        };

        var readings = SensorReadingBuilder.Build(hardware);

        Assert.Single(readings);
        Assert.Equal(SensorKind.CpuPackage, readings[0].Kind);
    }

    [Fact]
    public void Build_WhenTemperatureSensorMissing_ProducesNullValueNotThrow()
    {
        var hardware = new List<HardwareSnapshot>
        {
            new(HardwareType.Cpu, "AMD Ryzen 9 5950X", Array.Empty<SensorSnapshot>(), Array.Empty<HardwareSnapshot>()),
        };

        var readings = SensorReadingBuilder.Build(hardware);

        Assert.Null(readings[0].ValueCelsius);
    }
}
```

- [ ] **Step 8: Run test to verify it fails**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter SensorReadingBuilderTests`
Expected: FAIL to build — `SensorReadingBuilder` does not exist yet.

- [ ] **Step 9: Implement `SensorReadingBuilder`**

Create `Torrid.Core/Sensors/SensorReadingBuilder.cs`:

```csharp
namespace Torrid.Core.Sensors;

using LibreHardwareMonitor.Hardware;
using Torrid.Core.Models;

public static class SensorReadingBuilder
{
    public const string CpuTemperatureSensorName = "Core (Tctl/Tdie)";
    public const string GpuCoreTemperatureSensorName = "GPU Core";
    public const string GpuHotSpotTemperatureSensorName = "GPU Hot Spot";
    public const string GpuVramTemperatureSensorName = "GPU Memory Junction";

    private static readonly HardwareType[] GpuHardwareTypes =
    {
        HardwareType.GpuNvidia, HardwareType.GpuAmd, HardwareType.GpuIntel,
    };

    public static IReadOnlyList<SensorReading> Build(IReadOnlyList<HardwareSnapshot> hardware)
    {
        var readings = new List<SensorReading>();

        var cpu = hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        if (cpu is not null)
        {
            readings.Add(new SensorReading(
                "cpu", "CPU", SensorKind.CpuPackage, null,
                FindTemperature(cpu, CpuTemperatureSensorName)));
        }

        var gpuIndex = 0;
        foreach (var gpu in hardware.Where(h => GpuHardwareTypes.Contains(h.HardwareType)))
        {
            readings.Add(new SensorReading(
                $"gpu{gpuIndex}-core", $"GPU{gpuIndex} Core", SensorKind.GpuCore, gpuIndex,
                FindTemperature(gpu, GpuCoreTemperatureSensorName)));
            readings.Add(new SensorReading(
                $"gpu{gpuIndex}-hotspot", $"GPU{gpuIndex} Hot", SensorKind.GpuHotSpot, gpuIndex,
                FindTemperature(gpu, GpuHotSpotTemperatureSensorName)));
            readings.Add(new SensorReading(
                $"gpu{gpuIndex}-vram", $"GPU{gpuIndex} VRAM", SensorKind.GpuVram, gpuIndex,
                FindTemperature(gpu, GpuVramTemperatureSensorName)));
            gpuIndex++;
        }

        return readings;
    }

    private static double? FindTemperature(HardwareSnapshot hardware, string sensorName) =>
        hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name == sensorName)?.Value;
}
```

- [ ] **Step 10: Run test to verify it passes**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter SensorReadingBuilderTests`
Expected: PASS, 4/4.

- [ ] **Step 11: Commit**

```bash
git add Torrid.Core/Sensors/HardwareSnapshot.cs Torrid.Core/Sensors/SensorReadingBuilder.cs Torrid.Tests/SensorReadingBuilderTests.cs
git commit -m "Add HardwareSnapshot adapter and SensorReadingBuilder with tests"
```

- [ ] **Step 12: Write the failing test for `JsonConfigStore`**

Create `Torrid.Tests/JsonConfigStoreTests.cs`:

```csharp
using Torrid.Core.Config;
using Torrid.Core.Models;

namespace Torrid.Tests;

public class JsonConfigStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"torrid-test-{Guid.NewGuid():N}.json");

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaults()
    {
        var store = new JsonConfigStore(_path);

        var config = store.Load();

        Assert.Equal(2000, config.PollIntervalMs);
        Assert.Equal(40, config.Thresholds[SensorKind.CpuPackage].BlueMax);
    }

    [Fact]
    public void Load_WhenFileCorrupted_ReturnsDefaultsInsteadOfThrowing()
    {
        File.WriteAllText(_path, "{ not valid json");
        var store = new JsonConfigStore(_path);

        var config = store.Load();

        Assert.Equal(2000, config.PollIntervalMs);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsCustomValues()
    {
        var store = new JsonConfigStore(_path);
        var config = new AppConfig { PollIntervalMs = 5000, NotificationRefireMinutes = 10 };

        store.Save(config);
        var loaded = store.Load();

        Assert.Equal(5000, loaded.PollIntervalMs);
        Assert.Equal(10, loaded.NotificationRefireMinutes);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
```

- [ ] **Step 13: Run test to verify it fails**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter JsonConfigStoreTests`
Expected: FAIL to build — `AppConfig`/`JsonConfigStore` do not exist yet.

- [ ] **Step 14: Implement `AppConfig`, `IConfigStore`, `JsonConfigStore`**

Create `Torrid.Core/Config/AppConfig.cs`:

```csharp
namespace Torrid.Core.Config;

using Torrid.Core.Models;

public sealed class AppConfig
{
    public int PollIntervalMs { get; set; } = 2000;
    public Dictionary<SensorKind, ThresholdBand> Thresholds { get; set; } = DefaultThresholds();
    public HashSet<string> VisibleSensorIds { get; set; } = new();
    public int NotificationRefireMinutes { get; set; } = 0;
    public bool StartMinimizedToTray { get; set; } = true;

    public static Dictionary<SensorKind, ThresholdBand> DefaultThresholds() => new()
    {
        [SensorKind.CpuPackage] = new ThresholdBand { BlueMax = 40, GreenMax = 70, YellowMax = 85 },
        [SensorKind.GpuCore] = new ThresholdBand { BlueMax = 40, GreenMax = 75, YellowMax = 85 },
        [SensorKind.GpuHotSpot] = new ThresholdBand { BlueMax = 50, GreenMax = 85, YellowMax = 95 },
        [SensorKind.GpuVram] = new ThresholdBand { BlueMax = 50, GreenMax = 85, YellowMax = 95 },
    };
}
```

Create `Torrid.Core/Config/IConfigStore.cs`:

```csharp
namespace Torrid.Core.Config;

public interface IConfigStore
{
    AppConfig Load();
    void Save(AppConfig config);
}
```

Create `Torrid.Core/Config/JsonConfigStore.cs`:

```csharp
namespace Torrid.Core.Config;

using System.Text.Json;

public sealed class JsonConfigStore : IConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _path;

    public JsonConfigStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Torrid", "config.json");
    }

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppConfig();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions) ?? new AppConfig();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(_path, JsonSerializer.Serialize(config, SerializerOptions));
    }
}
```

- [ ] **Step 15: Run test to verify it passes**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter JsonConfigStoreTests`
Expected: PASS, 3/3.

- [ ] **Step 16: Commit**

```bash
git add Torrid.Core/Config/AppConfig.cs Torrid.Core/Config/IConfigStore.cs Torrid.Core/Config/JsonConfigStore.cs Torrid.Tests/JsonConfigStoreTests.cs
git commit -m "Add AppConfig and JsonConfigStore with fallback-to-defaults behavior"
```

- [ ] **Step 17: Implement `SensorService` (real LibreHardwareMonitor integration — not unit-tested, wraps confirmed sensor names)**

Create `Torrid.Core/Sensors/SensorService.cs`:

```csharp
namespace Torrid.Core.Sensors;

using LibreHardwareMonitor.Hardware;
using Torrid.Core.Models;

public sealed class SensorService : ISensorSource, IDisposable
{
    private readonly Computer _computer;
    private readonly System.Timers.Timer _timer;
    private readonly UpdateVisitor _visitor = new();

    public SensorService(int pollIntervalMs)
    {
        _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
        _computer.Open();

        _timer = new System.Timers.Timer(pollIntervalMs) { AutoReset = true };
        _timer.Elapsed += (_, _) => Poll();

        Poll();
    }

    public IReadOnlyList<SensorReading> CurrentReadings { get; private set; } = Array.Empty<SensorReading>();

    public event EventHandler<IReadOnlyList<SensorReading>>? ReadingsUpdated;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    private void Poll()
    {
        _computer.Accept(_visitor);
        var snapshot = _computer.Hardware.Select(ToSnapshot).ToList();
        var readings = SensorReadingBuilder.Build(snapshot);
        CurrentReadings = readings;
        ReadingsUpdated?.Invoke(this, readings);
    }

    private static HardwareSnapshot ToSnapshot(IHardware hardware) => new(
        hardware.HardwareType,
        hardware.Name,
        hardware.Sensors.Select(s => new SensorSnapshot(s.Name, s.SensorType, s.Value)).ToList(),
        hardware.SubHardware.Select(ToSnapshot).ToList());

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        _computer.Close();
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) sub.Accept(this);
        }

        public void VisitSensor(ISensor sensor) { }

        public void VisitParameter(IParameter parameter) { }
    }
}
```

This class is deliberately thin — all its decision logic (which sensors matter, how to label them) already lives in the tested `SensorReadingBuilder`. It cannot be meaningfully unit tested (it needs the real hardware/LibreHardwareMonitor); it is verified in Phase 2 by running the app elevated against real hardware, matching `tools/SensorProbe/FINDINGS.md`.

- [ ] **Step 18: Build to confirm it compiles**

Run: `& "C:\Program Files\dotnet\dotnet.exe" build Torrid.sln`
Expected: Build succeeded, 0 errors.

- [ ] **Step 19: Commit**

```bash
git add Torrid.Core/Sensors/SensorService.cs
git commit -m "Add SensorService: real LibreHardwareMonitor polling via SensorReadingBuilder"
```

---

### Task 2: Main Window UI

**Files:**
- Create: `Torrid.App/ColorPalette.cs`
- Create: `Torrid.App/Converters/ColorZoneToBrushConverter.cs`
- Create: `Torrid.App/Sensors/FakeSensorSource.cs`
- Create: `Torrid.App/ViewModels/SensorRowViewModel.cs`
- Create: `Torrid.App/ViewModels/MainWindowViewModel.cs`
- Modify: `Torrid.App/MainWindow.xaml` (replace template content)
- Modify: `Torrid.App/MainWindow.xaml.cs` (replace template content)
- Test: `Torrid.Tests/ColorPaletteTests.cs`

**Interfaces:**
- Consumes: `Torrid.Core.Models.{SensorKind, SensorReading}`, `Torrid.Core.Sensors.ISensorSource`, `Torrid.Core.Config.{ColorZone, ThresholdBand, ColorThresholdEvaluator, AppConfig}` — all from Task 1, already committed.
- Produces: `Torrid.App.ColorPalette.ForZone(ColorZone) -> (byte R, byte G, byte B)` (Task 3 reuses this — do not redefine tray-side colors separately). `Torrid.App.Sensors.FakeSensorSource : ISensorSource` — a 2-GPU simulated feed with randomized values every 2s, used by both this task and Task 3 so tray/window layouts can be exercised even though this machine currently has only 1 physical GPU. `Torrid.App.ViewModels.MainWindowViewModel` with `ObservableCollection<SensorRowViewModel> Rows`.

- [ ] **Step 1: Write the failing test for the shared color palette**

Create `Torrid.Tests/ColorPaletteTests.cs`:

```csharp
using Torrid.App;
using Torrid.Core.Config;

namespace Torrid.Tests;

public class ColorPaletteTests
{
    [Theory]
    [InlineData(ColorZone.Blue, 0x42, 0x9C, 0xF5)]
    [InlineData(ColorZone.Green, 0x4C, 0xAF, 0x50)]
    [InlineData(ColorZone.Yellow, 0xFF, 0xC1, 0x07)]
    [InlineData(ColorZone.Red, 0xF4, 0x43, 0x36)]
    public void ForZone_ReturnsExpectedRgb(ColorZone zone, byte r, byte g, byte b)
    {
        var (actualR, actualG, actualB) = ColorPalette.ForZone(zone);

        Assert.Equal((r, g, b), (actualR, actualG, actualB));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter ColorPaletteTests`
Expected: FAIL to build — `Torrid.App.ColorPalette` does not exist yet.

- [ ] **Step 3: Implement `ColorPalette`**

Create `Torrid.App/ColorPalette.cs`:

```csharp
namespace Torrid.App;

using Torrid.Core.Config;

public static class ColorPalette
{
    public static readonly (byte R, byte G, byte B) Blue = (0x42, 0x9C, 0xF5);
    public static readonly (byte R, byte G, byte B) Green = (0x4C, 0xAF, 0x50);
    public static readonly (byte R, byte G, byte B) Yellow = (0xFF, 0xC1, 0x07);
    public static readonly (byte R, byte G, byte B) Red = (0xF4, 0x43, 0x36);
    public static readonly (byte R, byte G, byte B) Unknown = (0x80, 0x80, 0x80);

    public static (byte R, byte G, byte B) ForZone(ColorZone zone) => zone switch
    {
        ColorZone.Blue => Blue,
        ColorZone.Green => Green,
        ColorZone.Yellow => Yellow,
        ColorZone.Red => Red,
        _ => Unknown,
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter ColorPaletteTests`
Expected: PASS, 4/4.

- [ ] **Step 5: Commit**

```bash
git add Torrid.App/ColorPalette.cs Torrid.Tests/ColorPaletteTests.cs
git commit -m "Add shared ColorPalette used by both MainWindow and tray renderer"
```

- [ ] **Step 6: Implement the WPF converter, fake sensor source, and view models (no further red/green cycle — these are thin wiring classes over already-tested logic; verify by running the app in Step 7)**

Create `Torrid.App/Converters/ColorZoneToBrushConverter.cs`:

```csharp
namespace Torrid.App.Converters;

using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Torrid.Core.Config;

public sealed class ColorZoneToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var zone = value is ColorZone z ? z : ColorZone.Blue;
        var (r, g, b) = ColorPalette.ForZone(zone);
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

Create `Torrid.App/Sensors/FakeSensorSource.cs`:

```csharp
namespace Torrid.App.Sensors;

using System.Windows.Threading;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

public sealed class FakeSensorSource : ISensorSource
{
    private readonly DispatcherTimer _timer;
    private readonly Random _random = new();

    public FakeSensorSource()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Tick();
        CurrentReadings = BuildReadings();
    }

    public IReadOnlyList<SensorReading> CurrentReadings { get; private set; }

    public event EventHandler<IReadOnlyList<SensorReading>>? ReadingsUpdated;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    private void Tick()
    {
        CurrentReadings = BuildReadings();
        ReadingsUpdated?.Invoke(this, CurrentReadings);
    }

    private IReadOnlyList<SensorReading> BuildReadings() => new List<SensorReading>
    {
        new("cpu", "CPU", SensorKind.CpuPackage, null, NextValue(30, 95)),
        new("gpu0-core", "GPU0 Core", SensorKind.GpuCore, 0, NextValue(30, 95)),
        new("gpu0-hotspot", "GPU0 Hot", SensorKind.GpuHotSpot, 0, NextValue(40, 100)),
        new("gpu0-vram", "GPU0 VRAM", SensorKind.GpuVram, 0, NextValue(40, 100)),
        new("gpu1-core", "GPU1 Core", SensorKind.GpuCore, 1, NextValue(30, 95)),
        new("gpu1-hotspot", "GPU1 Hot", SensorKind.GpuHotSpot, 1, NextValue(40, 100)),
        new("gpu1-vram", "GPU1 VRAM", SensorKind.GpuVram, 1, NextValue(40, 100)),
    };

    private double NextValue(double min, double max) => min + _random.NextDouble() * (max - min);
}
```

Create `Torrid.App/ViewModels/SensorRowViewModel.cs`:

```csharp
namespace Torrid.App.ViewModels;

using System.ComponentModel;
using Torrid.Core.Config;

public sealed class SensorRowViewModel : INotifyPropertyChanged
{
    private string _displayValue = "--";
    private ColorZone _zone = ColorZone.Blue;

    public SensorRowViewModel(string label) => Label = label;

    public string Label { get; }

    public string DisplayValue
    {
        get => _displayValue;
        private set { _displayValue = value; OnPropertyChanged(nameof(DisplayValue)); }
    }

    public ColorZone Zone
    {
        get => _zone;
        private set { _zone = value; OnPropertyChanged(nameof(Zone)); }
    }

    public void Update(double? valueCelsius, ThresholdBand thresholds)
    {
        DisplayValue = valueCelsius is double v ? $"{v:F0}\u00b0C" : "--";
        Zone = valueCelsius is double v2 ? ColorThresholdEvaluator.GetColor(v2, thresholds) : ColorZone.Blue;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

Create `Torrid.App/ViewModels/MainWindowViewModel.cs`:

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

    private void OnReadingsUpdated(object? sender, IReadOnlyList<SensorReading> readings) =>
        Application.Current?.Dispatcher.Invoke(() => Apply(readings));

    private void Apply(IReadOnlyList<SensorReading> readings)
    {
        foreach (var reading in readings)
        {
            if (!_rowsById.TryGetValue(reading.Id, out var row))
            {
                row = new SensorRowViewModel(reading.Label);
                _rowsById[reading.Id] = row;
                Rows.Add(row);
            }

            if (_config.Thresholds.TryGetValue(reading.Kind, out var band))
            {
                row.Update(reading.ValueCelsius, band);
            }
        }
    }
}
```

Replace `Torrid.App/MainWindow.xaml` with:

```xml
<Window x:Class="Torrid.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:conv="clr-namespace:Torrid.App.Converters"
        Title="Torrid" Height="340" Width="360"
        Background="#1E1E1E">
    <Window.Resources>
        <conv:ColorZoneToBrushConverter x:Key="ZoneToBrush"/>
    </Window.Resources>
    <ItemsControl ItemsSource="{Binding Rows}" Margin="16">
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <Grid Margin="0,6">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="{Binding Label}" Foreground="White" FontSize="16" VerticalAlignment="Center"/>
                    <TextBlock Grid.Column="1" Text="{Binding DisplayValue}" FontSize="22" FontWeight="Bold"
                               Foreground="{Binding Zone, Converter={StaticResource ZoneToBrush}}"/>
                </Grid>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</Window>
```

Replace `Torrid.App/MainWindow.xaml.cs` with:

```csharp
namespace Torrid.App;

using System.Windows;
using Torrid.App.Sensors;
using Torrid.App.ViewModels;
using Torrid.Core.Config;

public partial class MainWindow : Window
{
    private readonly FakeSensorSource _sensorSource = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(_sensorSource, new AppConfig());
        _sensorSource.Start();
        Closed += (_, _) => _sensorSource.Stop();
    }
}
```

- [ ] **Step 7: Manually verify by running the app**

Run: `& "C:\Program Files\dotnet\dotnet.exe" run --project Torrid.App`
Expected: a window titled "Torrid" opens showing 7 rows (CPU, GPU0/GPU1 x Core/Hot/VRAM), each value changing every ~2 seconds and colored blue/green/yellow/red depending on its randomized value against the default thresholds. Close the window to stop the app. Note in the task report whether this matched expectations, with a screenshot description if anything looked wrong.

- [ ] **Step 8: Commit**

```bash
git add Torrid.App/Converters/ColorZoneToBrushConverter.cs Torrid.App/Sensors/FakeSensorSource.cs Torrid.App/ViewModels/SensorRowViewModel.cs Torrid.App/ViewModels/MainWindowViewModel.cs Torrid.App/MainWindow.xaml Torrid.App/MainWindow.xaml.cs
git commit -m "Add MainWindow UI bound to FakeSensorSource via MainWindowViewModel"
```

---

### Task 3: Tray Icon Renderer

**Files:**
- Create: `Torrid.App/Tray/TooltipTextBuilder.cs`
- Create: `Torrid.App/Tray/TrayBitmapRenderer.cs`
- Create: `Torrid.App/Tray/TrayIconManager.cs`
- Create: `Torrid.App/Tray/NativeMethods.cs`
- Modify: `Torrid.App/Torrid.App.csproj` (add `<UseWindowsForms>true</UseWindowsForms>` for `NotifyIcon`)
- Modify: `Torrid.App/MainWindow.xaml.cs` (wire up `TrayIconManager` alongside the existing `FakeSensorSource`, so both this task and Task 2's window are visible together for manual verification)
- Test: `Torrid.Tests/TooltipTextBuilderTests.cs`

**Interfaces:**
- Consumes: `Torrid.Core.Models.SensorReading`, `Torrid.Core.Config.{ThresholdBand, ColorZone, ColorThresholdEvaluator, AppConfig}` from Task 1. `Torrid.App.ColorPalette` and `Torrid.App.Sensors.FakeSensorSource` from Task 2 — reuse both, do not redefine.
- Produces: `Torrid.App.Tray.TooltipTextBuilder.Build(IReadOnlyList<SensorReading>) -> string`. `Torrid.App.Tray.TrayIconManager : IDisposable`, constructed as `new TrayIconManager(ISensorSource sensorSource, AppConfig config)`, raising `event EventHandler? TrayIconClicked`.

- [ ] **Step 1: Write the failing test for tooltip text**

Create `Torrid.Tests/TooltipTextBuilderTests.cs`:

```csharp
using Torrid.App.Tray;
using Torrid.Core.Models;

namespace Torrid.Tests;

public class TooltipTextBuilderTests
{
    [Fact]
    public void Build_WithNoReadings_ReturnsEmptyStringWithoutThrowing()
    {
        var text = TooltipTextBuilder.Build(Array.Empty<SensorReading>());

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void Build_WithReadings_JoinsLabelAndValuePerLine()
    {
        var readings = new List<SensorReading>
        {
            new("cpu", "CPU", SensorKind.CpuPackage, null, 59.0),
            new("gpu0-core", "GPU0 Core", SensorKind.GpuCore, 0, null),
        };

        var text = TooltipTextBuilder.Build(readings);

        Assert.Contains("CPU: 59\u00b0C", text);
        Assert.Contains("GPU0 Core: --", text);
    }

    [Fact]
    public void Build_WhenTextWouldExceedNotifyIconLimit_TruncatesTo127Chars()
    {
        var readings = Enumerable.Range(0, 20)
            .Select(i => new SensorReading($"s{i}", $"Sensor Number {i}", SensorKind.CpuPackage, null, 50.0))
            .ToList();

        var text = TooltipTextBuilder.Build(readings);

        Assert.True(text.Length <= 127);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter TooltipTextBuilderTests`
Expected: FAIL to build — `Torrid.App.Tray.TooltipTextBuilder` does not exist yet.

- [ ] **Step 3: Implement `TooltipTextBuilder`**

Create `Torrid.App/Tray/TooltipTextBuilder.cs`:

```csharp
namespace Torrid.App.Tray;

using Torrid.Core.Models;

public static class TooltipTextBuilder
{
    // NotifyIcon.Text throws ArgumentOutOfRangeException above 127 chars on Vista+.
    private const int MaxLength = 127;

    public static string Build(IReadOnlyList<SensorReading> readings)
    {
        var text = string.Join(
            Environment.NewLine,
            readings.Select(r => $"{r.Label}: {(r.ValueCelsius is double v ? $"{v:F0}\u00b0C" : "--")}"));

        return text.Length <= MaxLength ? text : text[..(MaxLength - 3)] + "...";
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter TooltipTextBuilderTests`
Expected: PASS, 3/3.

- [ ] **Step 5: Commit**

```bash
git add Torrid.App/Tray/TooltipTextBuilder.cs Torrid.Tests/TooltipTextBuilderTests.cs
git commit -m "Add TooltipTextBuilder for tray hover text"
```

- [ ] **Step 6: Enable WinForms in Torrid.App for `NotifyIcon`**

Edit `Torrid.App/Torrid.App.csproj`, add `<UseWindowsForms>true</UseWindowsForms>` next to `<UseWPF>true</UseWPF>` in the `PropertyGroup`.

Run: `& "C:\Program Files\dotnet\dotnet.exe" build Torrid.sln`
Expected: Build succeeded (WPF and WinForms can coexist in one project).

- [ ] **Step 7: Implement the bitmap renderer, native icon cleanup helper, and tray manager (thin wiring/GDI+ — no further red/green cycle, verify visually in Step 8)**

Create `Torrid.App/Tray/NativeMethods.cs`:

```csharp
namespace Torrid.App.Tray;

using System.Runtime.InteropServices;

internal static class NativeMethods
{
    // Icon.FromHandle / Bitmap.GetHicon() hand back a native HICON that .NET does not
    // own or free automatically — every icon this renderer creates must be destroyed
    // explicitly or the process leaks a GDI handle on every redraw (multiple times a second).
    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(nint handle);
}
```

Create `Torrid.App/Tray/TrayBitmapRenderer.cs`:

```csharp
namespace Torrid.App.Tray;

using System.Drawing;
using System.Drawing.Drawing2D;
using Torrid.Core.Config;
using Torrid.Core.Models;

public sealed class TrayBitmapRenderer
{
    private const int Size = 32;

    public Icon Render(IReadOnlyList<SensorReading> readings, IReadOnlyDictionary<SensorKind, ThresholdBand> thresholds, bool blinkOn)
    {
        var cpu = readings.FirstOrDefault(r => r.Kind == SensorKind.CpuPackage);
        var hottestGpu = readings
            .Where(r => r.Kind == SensorKind.GpuHotSpot)
            .OrderByDescending(r => r.ValueCelsius ?? double.MinValue)
            .FirstOrDefault();

        var anyRed = readings.Any(r =>
            r.ValueCelsius is double v &&
            thresholds.TryGetValue(r.Kind, out var band) &&
            ColorThresholdEvaluator.GetColor(v, band) == ColorZone.Red);

        using var bitmap = new Bitmap(Size, Size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            if (anyRed && !blinkOn)
            {
                using var dimBrush = new SolidBrush(Color.FromArgb(70, 244, 67, 54));
                g.FillEllipse(dimBrush, 0, 0, Size, Size);
            }

            DrawHalf(g, top: true, cpu, thresholds);
            DrawHalf(g, top: false, hottestGpu, thresholds);
        }

        var handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }

    private static void DrawHalf(Graphics g, bool top, SensorReading? reading, IReadOnlyDictionary<SensorKind, ThresholdBand> thresholds)
    {
        var value = reading?.ValueCelsius;
        var zone = value is double v && reading is not null && thresholds.TryGetValue(reading.Kind, out var band)
            ? ColorThresholdEvaluator.GetColor(v, band)
            : ColorZone.Blue;
        var (r, gC, b) = ColorPalette.ForZone(zone);

        using var brush = new SolidBrush(Color.FromArgb(r, gC, b));
        using var font = new Font("Segoe UI", 11, System.Drawing.FontStyle.Bold);
        var text = value is double v2 ? $"{v2:F0}" : "--";
        var y = top ? 0 : Size / 2;
        var rect = new RectangleF(0, y, Size, Size / 2f);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, brush, rect, format);
    }
}
```

Create `Torrid.App/Tray/TrayIconManager.cs`:

```csharp
namespace Torrid.App.Tray;

using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using Torrid.Core.Config;
using Torrid.Core.Sensors;

public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly TrayBitmapRenderer _renderer = new();
    private readonly DispatcherTimer _blinkTimer;
    private readonly ISensorSource _sensorSource;
    private readonly AppConfig _config;
    private bool _blinkOn = true;
    private nint _previousIconHandle;

    public TrayIconManager(ISensorSource sensorSource, AppConfig config)
    {
        _sensorSource = sensorSource;
        _config = config;

        _notifyIcon = new NotifyIcon { Visible = true };
        _notifyIcon.Click += (_, _) => TrayIconClicked?.Invoke(this, EventArgs.Empty);

        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _blinkTimer.Tick += (_, _) => { _blinkOn = !_blinkOn; Redraw(); };
        _blinkTimer.Start();

        _sensorSource.ReadingsUpdated += (_, _) => Application.Current?.Dispatcher.Invoke(Redraw);

        Redraw();
    }

    public event EventHandler? TrayIconClicked;

    private void Redraw()
    {
        var readings = _sensorSource.CurrentReadings;
        var icon = _renderer.Render(readings, _config.Thresholds, _blinkOn);

        _notifyIcon.Icon = icon;
        _notifyIcon.Text = TooltipTextBuilder.Build(readings);

        if (_previousIconHandle != nint.Zero) NativeMethods.DestroyIcon(_previousIconHandle);
        _previousIconHandle = icon.Handle;
    }

    public void Dispose()
    {
        _blinkTimer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        if (_previousIconHandle != nint.Zero) NativeMethods.DestroyIcon(_previousIconHandle);
    }
}
```

Update `Torrid.App/MainWindow.xaml.cs` to also construct the tray icon against the same `FakeSensorSource`, so Task 2's window and Task 3's tray icon can both be exercised in one manual run:

```csharp
namespace Torrid.App;

using System.Windows;
using Torrid.App.Sensors;
using Torrid.App.Tray;
using Torrid.App.ViewModels;
using Torrid.Core.Config;

public partial class MainWindow : Window
{
    private readonly FakeSensorSource _sensorSource = new();
    private readonly TrayIconManager _trayIconManager;

    public MainWindow()
    {
        InitializeComponent();
        var config = new AppConfig();
        DataContext = new MainWindowViewModel(_sensorSource, config);
        _trayIconManager = new TrayIconManager(_sensorSource, config);
        _trayIconManager.TrayIconClicked += (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };
        _sensorSource.Start();
        Closed += (_, _) => { _sensorSource.Stop(); _trayIconManager.Dispose(); };
    }
}
```

- [ ] **Step 8: Manually verify by running the app**

Run: `& "C:\Program Files\dotnet\dotnet.exe" run --project Torrid.App`
Expected: alongside the MainWindow from Task 2, a tray icon appears in the notification area showing two small colored numbers (CPU on top, hottest GPU's hot-spot temp on bottom) that update every ~2 seconds. Hovering shows the full multi-line tooltip built by `TooltipTextBuilder`. Since `FakeSensorSource` randomizes into the red range periodically, confirm the icon visibly dims/pulses (the blink effect) while any value is red, and confirm at both 100% and a higher Windows display-scaling setting that the icon still renders legibly (per `PROJECT_PLAN.md`'s DPI-scaling note). Note the result in the task report; if DPI scaling can't be changed in this environment, say so explicitly rather than skipping silently.

- [ ] **Step 9: Commit**

```bash
git add Torrid.App/Torrid.App.csproj Torrid.App/Tray/TrayBitmapRenderer.cs Torrid.App/Tray/TrayIconManager.cs Torrid.App/Tray/NativeMethods.cs Torrid.App/MainWindow.xaml.cs
git commit -m "Add TrayIconManager: custom-rendered bitmap tray icon with blink-on-red and tooltip"
```

---

### Task 4: Notification Service

**Files:**
- Create: `Torrid.Core/Time/IClock.cs`
- Create: `Torrid.Core/Time/SystemClock.cs`
- Create: `Torrid.Core/Notifications/IToastNotifier.cs`
- Create: `Torrid.Core/Notifications/NotificationService.cs`
- Create: `Torrid.App/Notifications/WindowsToastNotifier.cs`
- Modify: `Torrid.App/App.xaml.cs` (set the AppUserModelID toast notifications need — see Step 7)
- Test: `Torrid.Tests/NotificationServiceTests.cs`

**Interfaces:**
- Consumes: `Torrid.Core.Models.{SensorKind, SensorReading}`, `Torrid.Core.Config.{ColorZone, ThresholdBand, ColorThresholdEvaluator}` from Task 1.
- Produces: `Torrid.Core.Time.IClock { DateTime UtcNow { get; } }` / `SystemClock`. `Torrid.Core.Notifications.IToastNotifier { void ShowRedAlert(SensorReading reading); }`. `Torrid.Core.Notifications.NotificationService`, constructed as `new NotificationService(IToastNotifier notifier, IClock clock, int refireMinutes)`, with `void Evaluate(IReadOnlyList<SensorReading> readings, IReadOnlyDictionary<SensorKind, ThresholdBand> thresholds)`.

- [ ] **Step 1: Write the failing tests for debounce/hysteresis**

Create `Torrid.Core/Time/IClock.cs`:

```csharp
namespace Torrid.Core.Time;

public interface IClock
{
    DateTime UtcNow { get; }
}
```

Create `Torrid.Tests/NotificationServiceTests.cs`:

```csharp
using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Notifications;
using Torrid.Core.Time;

namespace Torrid.Tests;

public sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}

public sealed class RecordingToastNotifier : IToastNotifier
{
    public List<SensorReading> ShownAlerts { get; } = new();

    public void ShowRedAlert(SensorReading reading) => ShownAlerts.Add(reading);
}

public class NotificationServiceTests
{
    private static readonly Dictionary<SensorKind, ThresholdBand> Thresholds = new()
    {
        [SensorKind.CpuPackage] = new ThresholdBand { BlueMax = 40, GreenMax = 70, YellowMax = 85 },
    };

    private static SensorReading Cpu(double value) => new("cpu", "CPU", SensorKind.CpuPackage, null, value);

    [Fact]
    public void Evaluate_WhenCrossingIntoRed_FiresExactlyOneAlert()
    {
        var notifier = new RecordingToastNotifier();
        var service = new NotificationService(notifier, new FakeClock(), refireMinutes: 0);

        service.Evaluate(new[] { Cpu(90) }, Thresholds);

        Assert.Single(notifier.ShownAlerts);
    }

    [Fact]
    public void Evaluate_WhenSustainedInRedAcrossManyPolls_DoesNotSpamEveryTick()
    {
        var notifier = new RecordingToastNotifier();
        var service = new NotificationService(notifier, new FakeClock(), refireMinutes: 0);

        for (var i = 0; i < 10; i++)
        {
            service.Evaluate(new[] { Cpu(90 + i * 0.1) }, Thresholds);
        }

        Assert.Single(notifier.ShownAlerts);
    }

    [Fact]
    public void Evaluate_WhenFlappingRightAtBoundary_DoesNotRefireUntilRealDropBelowRed()
    {
        var notifier = new RecordingToastNotifier();
        var service = new NotificationService(notifier, new FakeClock(), refireMinutes: 0);

        service.Evaluate(new[] { Cpu(85.1) }, Thresholds); // enters red -> 1 alert
        service.Evaluate(new[] { Cpu(84.9) }, Thresholds); // yellow, not red -> resets hysteresis
        service.Evaluate(new[] { Cpu(85.1) }, Thresholds); // re-enters red -> 2nd alert

        Assert.Equal(2, notifier.ShownAlerts.Count);
    }

    [Fact]
    public void Evaluate_WhenSustainedPastRefireInterval_FiresAgain()
    {
        var clock = new FakeClock();
        var notifier = new RecordingToastNotifier();
        var service = new NotificationService(notifier, clock, refireMinutes: 10);

        service.Evaluate(new[] { Cpu(90) }, Thresholds); // 1st alert
        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        service.Evaluate(new[] { Cpu(90) }, Thresholds); // still sustained, too soon -> no alert
        clock.UtcNow = clock.UtcNow.AddMinutes(6);
        service.Evaluate(new[] { Cpu(90) }, Thresholds); // 11 min since 1st alert -> 2nd alert

        Assert.Equal(2, notifier.ShownAlerts.Count);
    }

    [Fact]
    public void Evaluate_WithNullValue_DoesNotThrow()
    {
        var notifier = new RecordingToastNotifier();
        var service = new NotificationService(notifier, new FakeClock(), refireMinutes: 0);

        var exception = Record.Exception(() =>
            service.Evaluate(new[] { new SensorReading("cpu", "CPU", SensorKind.CpuPackage, null, null) }, Thresholds));

        Assert.Null(exception);
        Assert.Empty(notifier.ShownAlerts);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter NotificationServiceTests`
Expected: FAIL to build — `IToastNotifier`/`NotificationService` do not exist yet.

- [ ] **Step 3: Implement `SystemClock`, `IToastNotifier`, `NotificationService`**

Create `Torrid.Core/Time/SystemClock.cs`:

```csharp
namespace Torrid.Core.Time;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
```

Create `Torrid.Core/Notifications/IToastNotifier.cs`:

```csharp
namespace Torrid.Core.Notifications;

using Torrid.Core.Models;

public interface IToastNotifier
{
    void ShowRedAlert(SensorReading reading);
}
```

Create `Torrid.Core/Notifications/NotificationService.cs`:

```csharp
namespace Torrid.Core.Notifications;

using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Time;

public sealed class NotificationService
{
    private readonly IToastNotifier _notifier;
    private readonly IClock _clock;
    private readonly int _refireMinutes;
    private readonly Dictionary<string, DateTime> _lastNotifiedUtc = new();
    private readonly HashSet<string> _currentlyRed = new();

    public NotificationService(IToastNotifier notifier, IClock clock, int refireMinutes)
    {
        _notifier = notifier;
        _clock = clock;
        _refireMinutes = refireMinutes;
    }

    public void Evaluate(IReadOnlyList<SensorReading> readings, IReadOnlyDictionary<SensorKind, ThresholdBand> thresholds)
    {
        foreach (var reading in readings)
        {
            if (reading.ValueCelsius is not double value) continue;
            if (!thresholds.TryGetValue(reading.Kind, out var band)) continue;

            var isRed = ColorThresholdEvaluator.GetColor(value, band) == ColorZone.Red;
            var wasRed = _currentlyRed.Contains(reading.Id);

            if (isRed && !wasRed)
            {
                _notifier.ShowRedAlert(reading);
                _lastNotifiedUtc[reading.Id] = _clock.UtcNow;
                _currentlyRed.Add(reading.Id);
            }
            else if (isRed && wasRed && _refireMinutes > 0 &&
                     _clock.UtcNow - _lastNotifiedUtc[reading.Id] >= TimeSpan.FromMinutes(_refireMinutes))
            {
                _notifier.ShowRedAlert(reading);
                _lastNotifiedUtc[reading.Id] = _clock.UtcNow;
            }
            else if (!isRed && wasRed)
            {
                _currentlyRed.Remove(reading.Id);
            }
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter NotificationServiceTests`
Expected: PASS, 5/5.

- [ ] **Step 5: Commit**

```bash
git add Torrid.Core/Time/IClock.cs Torrid.Core/Time/SystemClock.cs Torrid.Core/Notifications/IToastNotifier.cs Torrid.Core/Notifications/NotificationService.cs Torrid.Tests/NotificationServiceTests.cs
git commit -m "Add NotificationService with debounce/hysteresis, fully unit tested"
```

- [ ] **Step 6: Verify the current recommended NuGet package for WPF toast notifications, then add it**

`PROJECT_PLAN.md` section 10 flags this as needing verification at implementation time — this ecosystem has had package-naming churn. Before writing `WindowsToastNotifier`:

1. Check `https://www.nuget.org/packages/Microsoft.Toolkit.Uwp.Notifications` and `https://www.nuget.org/packages/CommunityToolkit.WinUI.Notifications` for current versions and target frameworks.
2. `CommunityToolkit.WinUI.Notifications` targets the Windows App SDK/WinUI3, which this project does not use (it's a classic `net8.0-windows` WPF app) — unless that has changed, prefer `Microsoft.Toolkit.Uwp.Notifications`, which works from a plain unpackaged desktop app via `ToastContentBuilder` + an explicit AppUserModelID (set in Step 7).
3. Add whichever package is actually current:

```bash
& "C:\Program Files\dotnet\dotnet.exe" add Torrid.App/Torrid.App.csproj package Microsoft.Toolkit.Uwp.Notifications
```

If the verification in step 1-2 finds this package deprecated with no unpackaged-app-compatible successor, note that clearly in the task report as a `DONE_WITH_CONCERNS` — do not silently swap in a different notification mechanism (e.g. a plain `NotifyIcon` balloon tip) without flagging it, since `PROJECT_PLAN.md` section 3 explicitly requires a real Windows toast, not a balloon.

- [ ] **Step 7: Implement `WindowsToastNotifier` and set the AppUserModelID**

Create `Torrid.App/Notifications/WindowsToastNotifier.cs`:

```csharp
namespace Torrid.App.Notifications;

using Microsoft.Toolkit.Uwp.Notifications;
using Torrid.Core.Models;
using Torrid.Core.Notifications;

public sealed class WindowsToastNotifier : IToastNotifier
{
    public void ShowRedAlert(SensorReading reading)
    {
        var value = reading.ValueCelsius is double v ? $"{v:F0}\u00b0C" : "unknown";
        new ToastContentBuilder()
            .AddText("Torrid — temperature critical")
            .AddText($"{reading.Label} is at {value}")
            .Show();
    }
}
```

Toast activation without an MSIX package needs an explicit AppUserModelID set before any toast is shown, via the stable Win32 `SetCurrentProcessExplicitAppUserModelID` API (this call itself does not vary with which notification NuGet package Step 6 lands on). In `Torrid.App/App.xaml.cs`:

```csharp
namespace Torrid.App;

using System.Runtime.InteropServices;
using System.Windows;

public partial class App : Application
{
    private const string AppUserModelId = "Torrid.App";

    protected override void OnStartup(StartupEventArgs e)
    {
        SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        base.OnStartup(e);
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);
}
```

Confirm during Step 6's verification whether the chosen notification package additionally requires COM server registration (e.g. `DesktopNotificationManagerCompat.RegisterAumidAndComServer`) for click activation to work in an unpackaged app — if its current documentation says so, add that call in `OnStartup` too, right after the AUMID call above, and note the addition in the task report.

- [ ] **Step 8: Build to confirm it compiles**

Run: `& "C:\Program Files\dotnet\dotnet.exe" build Torrid.sln`
Expected: Build succeeded, 0 errors.

- [ ] **Step 9: Manually verify by running the app and forcing a red value**

Since `WindowsToastNotifier` isn't wired to live data until Phase 2, verify it directly: temporarily add one line to `MainWindow`'s constructor —
`new WindowsToastNotifier().ShowRedAlert(new SensorReading("test", "Test Sensor", SensorKind.CpuPackage, null, 99));` — run the app, confirm a real Windows toast notification appears in the Action Center, then remove that temporary line before committing.

- [ ] **Step 10: Commit**

```bash
git add Torrid.App/Torrid.App.csproj Torrid.App/Notifications/WindowsToastNotifier.cs Torrid.App/App.xaml.cs
git commit -m "Add WindowsToastNotifier using verified toast notification package"
```
