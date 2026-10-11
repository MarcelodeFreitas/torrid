# Torrid Phase 4 (Anti-cheat safety, working toasts, single-window UI rebuild) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** (1) Stop Torrid from interfering with anti-cheat software (Riot Vanguard and others) by automatically backing off from the privileged hardware driver and from GPU polling while an anti-cheat is running; (2) make Windows toast notifications actually work; (3) replace the two-window UI with one FanControl-inspired window — sidebar, live Home dashboard, and Sensors / Thresholds / Settings pages whose edits apply immediately — with all sensors listable, only CPU and GPU hot spot on by default, and real sensor names shown.

**Architecture:** `Torrid.Core` stays UI-free and owns all decisions: a unified per-sensor preference model (`SensorPreference` keyed by sensor id, with per-kind defaults), a live `ConfigManager` (change event + debounced save), and the anti-cheat detector/policy that `SensorService` consults every poll. `Torrid.App` gets an `AppHost` composition root (config, sensors, notifications, tray, shell view model), a dark card theme, a sidebar shell and four pages. All config reads and writes happen on the UI thread; the only values read from the sensor timer thread are an enum and a list that is replaced wholesale, never mutated in place.

**Tech Stack:** C#/.NET 8, WPF, the existing `Torrid.Core` / `Torrid.App` / `Torrid.Tests` solution, LibreHardwareMonitorLib 0.9.6 (PawnIO-based), xunit.

**Spec:** `PROJECT_PLAN.md` sections 3, 6, 9, plus the user feedback gathered after Phase 2+3 shipped (paraphrased from their messages):
1. "It's interfering with Vanguard and maybe other anticheat systems — fix that."
2. "Improve UI", using FanControl's UI as inspiration (sidebar navigation, dark navy cards); "when clicking settings I want a main page to show up displaying the temps but also a way to edit what shows up in that screen and in the task bar"; "make the numbers bigger".
3. "Show a list of all sensors and by default just active CPU hot spot and GPU hotspot, and don't mask the names in the settings, show the actual sensor name."
4. "Notifications are not working." (Root cause now proven — see `tools/ToastProbe/FINDINGS.md`.)
5. Already done and must keep working: one tray icon per sensor, right-click tray menu (not opening the window), start minimized to tray, X hides to tray, publish goes to the top-level `publish/` folder, Debug builds run non-elevated.

## Global Constraints

- Solution layout (already committed): `Torrid.Core` (net8.0), `Torrid.App` (net8.0-windows10.0.17763.0, WPF + WinForms for `NotifyIcon`), `Torrid.Tests` (net8.0-windows10.0.17763.0, xunit; references both). Use the full path `C:\Program Files\dotnet\dotnet.exe` for every dotnet command (the one on PATH has no SDK).
- **Commit trailer, verbatim, on every commit:** `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>` — do not substitute your own model name.
- **Debug builds are non-elevated** (`asInvoker`), so you CAN run the app yourself and MUST clean up: never leave a `Torrid.App.exe` process running (check with `Get-Process Torrid.App`; `Stop-Process` works because it is not elevated). In a non-elevated Debug run the CPU temperature reads 0.0 / unavailable — expected, not a bug.
- **Seeing the UI:** `powershell -NoProfile -File tools\Capture-Window.ps1 -Launch -Out <png> [-AppArgs @('--show','--demo')]` launches the Debug build, screenshots just its window, and closes it. Open the PNG with the Read tool to look at it. `--demo` feeds fake two-GPU data (so screenshots are deterministic and hardware-independent), `--demo-paused` additionally simulates "anti-cheat running", `--config <path>` uses a throwaway config file instead of the real `%AppData%\Torrid\config.json` (ALWAYS pass `--config` with a temp path when screenshotting or testing, so the user's real config is never touched). Task 3 adds the `--demo`, `--demo-paused` and `--config` flags; the screenshot tool and `--show` already exist.
- `Torrid.Core` has no UI dependency. Pure logic gets unit tests (TDD); WPF/GDI+/COM wiring is verified by building, running and screenshots.
- Sensor ids: `cpu`, `gpu{N}-core`, `gpu{N}-hotspot`, `gpu{N}-vram` (N = 0-based enumeration index). Default labels: `CPU`, `GPU{N} Core`, `GPU{N} Hot`, `GPU{N} VRAM`.
- Per-kind defaults (code, not config): shown in window and tray = `CpuPackage` and `GpuHotSpot` only; notifications = all four kinds on.
- Threshold colouring semantics are unchanged (`value < BlueMax` blue; `< GreenMax` green; `<= YellowMax` yellow; else red).
- The config schema changes (accepted, pre-release): `VisibleSensorIds`, `CustomLabels`, `NotificationDisabledSensorIds` are removed in favour of `Sensors`. Old `config.json` files still load (unknown properties are ignored) but those old preferences reset to the defaults.
- Zone colours (shared by WPF and the tray via `ColorPalette`): blue `#5AA9FF`, green `#4ED36B`, yellow `#FFC93C`, red `#FF5C4D`, unavailable `#8A94A6`.
- Theme colours: window `#20232A`, sidebar `#181B20`, card `#14306B` (border `#2B4A8F`), primary text `#FFFFFF`, secondary text `#A8B6D4`, accent `#7C8CFF` (deliberately not yellow/green/blue/red so it never reads as a temperature state). Font: Segoe UI. Icons: `Segoe Fluent Icons, Segoe MDL2 Assets`.
- Anti-cheat behaviour contract: when an anti-cheat is detected the hardware layer behaves per `AppConfig.WhileAntiCheatActive` — `Pause` (default: close the hardware computer entirely; readings become unavailable/null), `GpuOnly` (CPU/driver path off, GPU readings continue), `Continue` (no change). At startup the policy is evaluated BEFORE any hardware is opened, so an already-running anti-cheat means the privileged driver is never touched.

## Review Focus

1. Torrid is launched while an anti-cheat is already running: no CPU/driver hardware may be opened at all before the first policy evaluation (tested at the policy/decision level; the service must evaluate first).
2. An anti-cheat starts or stops while Torrid runs: level changes both ways without leaking `Computer` instances, readings turn unavailable (null) rather than stale, and null readings never trigger alerts.
3. The Sensors page for a sensor that is not detected right now (second GPU unplugged) or whose saved key is garbage: a parsable saved id still gets an editable row from its preferences; an unparsable key is ignored without crashing.
4. Config edits are live: toggling Window / Tray / Alerts or renaming a sensor updates the Home cards, tray icons and alert filtering immediately; an invalid threshold ordering is rejected, never applied.
5. Closing with X hides; Exit from the tray really exits and flushes a pending debounced config save so no edit is lost.

---

### Task 1: Unified per-sensor preferences, live ConfigManager, and sensor names (Torrid.Core, plus minimal App adaptation)

**Files:**
- Modify: `Torrid.Core/Models/SensorReading.cs`
- Modify: `Torrid.Core/Sensors/SensorReadingBuilder.cs`
- Create: `Torrid.Core/Models/SensorIdentity.cs`
- Create: `Torrid.Core/Config/SensorDefaults.cs`
- Create: `Torrid.Core/Config/SensorPreference.cs`
- Create: `Torrid.Core/Config/SensorPreferences.cs`
- Create: `Torrid.Core/Config/ConfigManager.cs`
- Modify: `Torrid.Core/Config/AppConfig.cs`
- Modify: `Torrid.Core/Config/SensorDisplay.cs`
- Modify: `Torrid.Core/Config/JsonConfigStore.cs`
- Modify (App, minimal): `Torrid.App/ViewModels/MainWindowViewModel.cs`, `Torrid.App/Tray/TrayIconGroupManager.cs`, `Torrid.App/MainWindow.xaml.cs`
- Delete: `Torrid.App/SettingsWindow.xaml`, `Torrid.App/SettingsWindow.xaml.cs`, `Torrid.App/ViewModels/SettingsViewModel.cs`, `Torrid.App/ViewModels/SensorSettingRowViewModel.cs`, `Torrid.App/ViewModels/ThresholdSettingRowViewModel.cs`, `Torrid.Tests/SettingsViewModelTests.cs`
- Tests: `Torrid.Tests/SensorIdentityTests.cs` (new), `Torrid.Tests/SensorDefaultsTests.cs` (new), `Torrid.Tests/SensorPreferencesTests.cs` (new), `Torrid.Tests/ConfigManagerTests.cs` (new), `Torrid.Tests/SensorDisplayTests.cs` (rewrite), `Torrid.Tests/JsonConfigStoreTests.cs` (update), `Torrid.Tests/SensorReadingBuilderTests.cs` (extend), `Torrid.Tests/MainWindowViewModelTests.cs` (adapt)

**Interfaces:**
- Consumes: existing `SensorKind`, `SensorReading`, `ThresholdBand`, `AppConfig`, `IConfigStore`, `JsonConfigStore`.
- Produces (later tasks rely on these exact names):
  - `SensorReading(string Id, string Label, SensorKind Kind, int? GpuIndex, double? ValueCelsius, string? HardwareName = null, string? SourceSensorName = null)` — the two new trailing parameters default to `null`, so every existing positional construction still compiles.
  - `Torrid.Core.Models.SensorIdentity(string Id, string DefaultLabel, SensorKind Kind, int? GpuIndex)` with `static SensorIdentity FromReading(SensorReading)` and `static SensorIdentity? TryParse(string? id)`.
  - `Torrid.Core.Config.SensorDefaults.{ShownInWindow, ShownInTray, Notify}(SensorKind) -> bool`.
  - `Torrid.Core.Config.SensorPreference { string? Label; bool? ShowInWindow; bool? ShowInTray; bool? Notify; bool IsEmpty }`.
  - `AppConfig.Sensors : Dictionary<string, SensorPreference>`; `VisibleSensorIds`, `CustomLabels`, `NotificationDisabledSensorIds` removed.
  - `Torrid.Core.Config.SensorPreferences.Update(AppConfig, string sensorId, Action<SensorPreference>)` (creates/mutates the entry, removes it when it becomes empty).
  - `SensorDisplay`: `IsShownInWindow`, `IsShownInTray`, `NotificationsEnabled` (each with `(AppConfig, SensorIdentity)` and `(AppConfig, SensorReading)` overloads) and `ResolveLabel(AppConfig, SensorIdentity)` / `ResolveLabel(AppConfig, SensorReading)`.
  - `Torrid.Core.Config.ConfigManager(IConfigStore store, AppConfig? config = null, TimeSpan? saveDelay = null)` with `AppConfig Config`, `event EventHandler? Changed`, `void Update(Action<AppConfig>)`, `void Flush()`, `string? LastSaveError`, `IDisposable` (Dispose flushes). `saveDelay` default 500 ms; `TimeSpan.Zero` saves synchronously inside `Update` (used by tests).

- [ ] **Step 1: Failing tests for the model additions**

Create `Torrid.Tests/SensorIdentityTests.cs`:

```csharp
using Torrid.Core.Models;

namespace Torrid.Tests;

public class SensorIdentityTests
{
    [Fact]
    public void TryParse_Cpu_ReturnsCpuIdentity()
    {
        var identity = SensorIdentity.TryParse("cpu");

        Assert.NotNull(identity);
        Assert.Equal(SensorKind.CpuPackage, identity!.Kind);
        Assert.Equal("CPU", identity.DefaultLabel);
        Assert.Null(identity.GpuIndex);
    }

    [Theory]
    [InlineData("gpu0-core", SensorKind.GpuCore, "GPU0 Core", 0)]
    [InlineData("gpu1-hotspot", SensorKind.GpuHotSpot, "GPU1 Hot", 1)]
    [InlineData("gpu12-vram", SensorKind.GpuVram, "GPU12 VRAM", 12)]
    public void TryParse_GpuIds_ReturnExpectedIdentity(string id, SensorKind kind, string label, int index)
    {
        var identity = SensorIdentity.TryParse(id);

        Assert.NotNull(identity);
        Assert.Equal(id, identity!.Id);
        Assert.Equal(kind, identity.Kind);
        Assert.Equal(label, identity.DefaultLabel);
        Assert.Equal(index, identity.GpuIndex);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gpu-core")]
    [InlineData("gpu0-fan")]
    [InlineData("gpux-core")]
    [InlineData("GPU0-CORE")]
    [InlineData("gpu99999999999999999999-core")]
    [InlineData("something-else")]
    public void TryParse_InvalidIds_ReturnNull(string? id)
    {
        Assert.Null(SensorIdentity.TryParse(id));
    }

    [Fact]
    public void FromReading_CopiesIdentityFields()
    {
        var reading = new SensorReading("gpu1-vram", "GPU1 VRAM", SensorKind.GpuVram, 1, 70.0);

        var identity = SensorIdentity.FromReading(reading);

        Assert.Equal(new SensorIdentity("gpu1-vram", "GPU1 VRAM", SensorKind.GpuVram, 1), identity);
    }
}
```

Create `Torrid.Tests/SensorDefaultsTests.cs`:

```csharp
using Torrid.Core.Config;
using Torrid.Core.Models;

namespace Torrid.Tests;

public class SensorDefaultsTests
{
    [Theory]
    [InlineData(SensorKind.CpuPackage, true)]
    [InlineData(SensorKind.GpuHotSpot, true)]
    [InlineData(SensorKind.GpuCore, false)]
    [InlineData(SensorKind.GpuVram, false)]
    public void ShownByDefault_OnlyForCpuAndGpuHotSpot(SensorKind kind, bool expected)
    {
        Assert.Equal(expected, SensorDefaults.ShownInWindow(kind));
        Assert.Equal(expected, SensorDefaults.ShownInTray(kind));
    }

    [Theory]
    [InlineData(SensorKind.CpuPackage)]
    [InlineData(SensorKind.GpuCore)]
    [InlineData(SensorKind.GpuHotSpot)]
    [InlineData(SensorKind.GpuVram)]
    public void Notify_IsOnForEveryKindByDefault(SensorKind kind)
    {
        Assert.True(SensorDefaults.Notify(kind));
    }
}
```

Append this test to `Torrid.Tests/SensorReadingBuilderTests.cs` (inside the existing class; reuse its `Temp` helper):

```csharp
    [Fact]
    public void Build_PopulatesHardwareAndSourceSensorNames()
    {
        var hardware = new List<HardwareSnapshot>
        {
            new(HardwareType.Cpu, "AMD Ryzen 9 5950X",
                new[] { Temp("Core (Tctl/Tdie)", 59.0) }, Array.Empty<HardwareSnapshot>()),
            new(HardwareType.GpuNvidia, "NVIDIA GeForce RTX 3090",
                new[] { Temp("GPU Core", 41.0), Temp("GPU Hot Spot", 51.0), Temp("GPU Memory Junction", 50.0) },
                Array.Empty<HardwareSnapshot>()),
        };

        var readings = SensorReadingBuilder.Build(hardware);

        var cpu = Assert.Single(readings, r => r.Id == "cpu");
        Assert.Equal("AMD Ryzen 9 5950X", cpu.HardwareName);
        Assert.Equal("Core (Tctl/Tdie)", cpu.SourceSensorName);
        var hot = Assert.Single(readings, r => r.Id == "gpu0-hotspot");
        Assert.Equal("NVIDIA GeForce RTX 3090", hot.HardwareName);
        Assert.Equal("GPU Hot Spot", hot.SourceSensorName);
        var vram = Assert.Single(readings, r => r.Id == "gpu0-vram");
        Assert.Equal("GPU Memory Junction", vram.SourceSensorName);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter "SensorIdentityTests|SensorDefaultsTests|SensorReadingBuilderTests"`
Expected: FAIL to build (`SensorIdentity`, `SensorDefaults`, `HardwareName`, `SourceSensorName` do not exist).

- [ ] **Step 3: Implement the model additions**

Replace `Torrid.Core/Models/SensorReading.cs`:

```csharp
namespace Torrid.Core.Models;

public sealed record SensorReading(
    string Id,
    string Label,
    SensorKind Kind,
    int? GpuIndex,
    double? ValueCelsius,
    string? HardwareName = null,
    string? SourceSensorName = null);
```

Create `Torrid.Core/Models/SensorIdentity.cs`:

```csharp
namespace Torrid.Core.Models;

using System.Text.RegularExpressions;

public sealed record SensorIdentity(string Id, string DefaultLabel, SensorKind Kind, int? GpuIndex)
{
    private static readonly Regex GpuId = new(@"^gpu(\d+)-(core|hotspot|vram)$", RegexOptions.Compiled);

    public static SensorIdentity FromReading(SensorReading reading) =>
        new(reading.Id, reading.Label, reading.Kind, reading.GpuIndex);

    public static SensorIdentity? TryParse(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (id == "cpu") return new SensorIdentity("cpu", "CPU", SensorKind.CpuPackage, null);

        var match = GpuId.Match(id);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var index)) return null;

        return match.Groups[2].Value switch
        {
            "core" => new SensorIdentity(id, $"GPU{index} Core", SensorKind.GpuCore, index),
            "hotspot" => new SensorIdentity(id, $"GPU{index} Hot", SensorKind.GpuHotSpot, index),
            "vram" => new SensorIdentity(id, $"GPU{index} VRAM", SensorKind.GpuVram, index),
            _ => null,
        };
    }
}
```

Create `Torrid.Core/Config/SensorDefaults.cs`:

```csharp
namespace Torrid.Core.Config;

using Torrid.Core.Models;

public static class SensorDefaults
{
    public static bool ShownInWindow(SensorKind kind) => kind is SensorKind.CpuPackage or SensorKind.GpuHotSpot;

    public static bool ShownInTray(SensorKind kind) => ShownInWindow(kind);

    public static bool Notify(SensorKind kind) => true;
}
```

In `Torrid.Core/Sensors/SensorReadingBuilder.cs`, make `Build` fill the names and have `FindTemperature` return the sensor so its name is available. Replace the `Build` method and `FindTemperature` with:

```csharp
    public static IReadOnlyList<SensorReading> Build(IReadOnlyList<HardwareSnapshot> hardware)
    {
        var readings = new List<SensorReading>();

        var cpu = hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        if (cpu is not null)
        {
            readings.Add(MakeReading("cpu", "CPU", SensorKind.CpuPackage, null, cpu, CpuTemperatureSensorName));
        }

        var gpuIndex = 0;
        foreach (var gpu in hardware.Where(h => GpuHardwareTypes.Contains(h.HardwareType)))
        {
            readings.Add(MakeReading($"gpu{gpuIndex}-core", $"GPU{gpuIndex} Core", SensorKind.GpuCore, gpuIndex, gpu, GpuCoreTemperatureSensorName));
            readings.Add(MakeReading($"gpu{gpuIndex}-hotspot", $"GPU{gpuIndex} Hot", SensorKind.GpuHotSpot, gpuIndex, gpu, GpuHotSpotTemperatureSensorName));
            readings.Add(MakeReading($"gpu{gpuIndex}-vram", $"GPU{gpuIndex} VRAM", SensorKind.GpuVram, gpuIndex, gpu, GpuVramTemperatureSensorName));
            gpuIndex++;
        }

        return readings;
    }

    private static SensorReading MakeReading(string id, string label, SensorKind kind, int? gpuIndex, HardwareSnapshot hardware, string sensorName) =>
        new(id, label, kind, gpuIndex,
            hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name == sensorName)?.Value,
            hardware.Name,
            sensorName);
```

(Delete the old private `FindTemperature`; keep `ToUnavailable` unchanged — `record with` preserves the new fields.)

- [ ] **Step 4: Run to verify the model tests pass**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter "SensorIdentityTests|SensorDefaultsTests|SensorReadingBuilderTests"`
Expected: PASS (the Core project builds; the App project may not yet — only run this filter against `Torrid.Tests`, which references Torrid.App, so if the build fails because Torrid.App still references removed APIs you have not removed yet, that cannot happen at this step: nothing has been removed yet).

- [ ] **Step 5: Commit**

```bash
git add Torrid.Core/Models Torrid.Core/Config/SensorDefaults.cs Torrid.Core/Sensors/SensorReadingBuilder.cs Torrid.Tests/SensorIdentityTests.cs Torrid.Tests/SensorDefaultsTests.cs Torrid.Tests/SensorReadingBuilderTests.cs
git commit -m "Add SensorIdentity, per-kind defaults, and hardware/sensor names on readings"
```

- [ ] **Step 6: Failing tests for preferences, display rules and ConfigManager**

Create `Torrid.Tests/SensorPreferencesTests.cs`:

```csharp
using Torrid.Core.Config;

namespace Torrid.Tests;

public class SensorPreferencesTests
{
    [Fact]
    public void Update_CreatesEntryWhenMissing()
    {
        var config = new AppConfig();

        SensorPreferences.Update(config, "gpu0-vram", p => p.ShowInTray = true);

        Assert.True(config.Sensors["gpu0-vram"].ShowInTray);
    }

    [Fact]
    public void Update_RemovesEntryOnceItBecomesEmpty()
    {
        var config = new AppConfig();
        SensorPreferences.Update(config, "cpu", p => p.Label = "5950X");

        SensorPreferences.Update(config, "cpu", p => p.Label = null);

        Assert.False(config.Sensors.ContainsKey("cpu"));
    }

    [Fact]
    public void IsEmpty_TreatsWhitespaceLabelAsEmpty()
    {
        Assert.True(new SensorPreference { Label = "   " }.IsEmpty);
        Assert.False(new SensorPreference { Label = "x" }.IsEmpty);
        Assert.False(new SensorPreference { Notify = false }.IsEmpty);
    }
}
```

Replace `Torrid.Tests/SensorDisplayTests.cs` entirely:

```csharp
using Torrid.Core.Config;
using Torrid.Core.Models;

namespace Torrid.Tests;

public class SensorDisplayTests
{
    private static readonly SensorIdentity Cpu = new("cpu", "CPU", SensorKind.CpuPackage, null);
    private static readonly SensorIdentity Core = new("gpu0-core", "GPU0 Core", SensorKind.GpuCore, 0);
    private static readonly SensorIdentity Hot = new("gpu0-hotspot", "GPU0 Hot", SensorKind.GpuHotSpot, 0);
    private static readonly SensorIdentity Vram = new("gpu0-vram", "GPU0 VRAM", SensorKind.GpuVram, 0);

    [Fact]
    public void ByDefault_OnlyCpuAndHotSpotAreShown()
    {
        var config = new AppConfig();

        Assert.True(SensorDisplay.IsShownInWindow(config, Cpu));
        Assert.True(SensorDisplay.IsShownInWindow(config, Hot));
        Assert.False(SensorDisplay.IsShownInWindow(config, Core));
        Assert.False(SensorDisplay.IsShownInWindow(config, Vram));
        Assert.True(SensorDisplay.IsShownInTray(config, Cpu));
        Assert.False(SensorDisplay.IsShownInTray(config, Vram));
    }

    [Fact]
    public void ExplicitOverride_BeatsTheKindDefault_InBothDirections()
    {
        var config = new AppConfig();
        SensorPreferences.Update(config, "gpu0-vram", p => { p.ShowInWindow = true; p.ShowInTray = true; });
        SensorPreferences.Update(config, "cpu", p => { p.ShowInWindow = false; p.ShowInTray = false; });

        Assert.True(SensorDisplay.IsShownInWindow(config, Vram));
        Assert.True(SensorDisplay.IsShownInTray(config, Vram));
        Assert.False(SensorDisplay.IsShownInWindow(config, Cpu));
        Assert.False(SensorDisplay.IsShownInTray(config, Cpu));
    }

    [Fact]
    public void WindowAndTrayAreIndependent()
    {
        var config = new AppConfig();
        SensorPreferences.Update(config, "gpu0-hotspot", p => p.ShowInTray = false);

        Assert.True(SensorDisplay.IsShownInWindow(config, Hot));
        Assert.False(SensorDisplay.IsShownInTray(config, Hot));
    }

    [Fact]
    public void ResolveLabel_UsesCustomLabelOtherwiseTheDefault()
    {
        var config = new AppConfig();
        Assert.Equal("GPU0 Hot", SensorDisplay.ResolveLabel(config, Hot));

        SensorPreferences.Update(config, "gpu0-hotspot", p => p.Label = "MSI Hot Spot");
        Assert.Equal("MSI Hot Spot", SensorDisplay.ResolveLabel(config, Hot));
    }

    [Fact]
    public void ResolveLabel_WhitespaceLabelFallsBackToDefault()
    {
        var config = new AppConfig();
        config.Sensors["cpu"] = new SensorPreference { Label = "   " };

        Assert.Equal("CPU", SensorDisplay.ResolveLabel(config, Cpu));
    }

    [Fact]
    public void Notifications_AreOnByDefault_AndCanBeSwitchedOffPerSensor()
    {
        var config = new AppConfig();
        Assert.True(SensorDisplay.NotificationsEnabled(config, Vram));

        SensorPreferences.Update(config, "gpu0-vram", p => p.Notify = false);

        Assert.False(SensorDisplay.NotificationsEnabled(config, Vram));
        Assert.True(SensorDisplay.NotificationsEnabled(config, Hot));
    }

    [Fact]
    public void ReadingOverloads_MatchTheIdentityOverloads()
    {
        var config = new AppConfig();
        SensorPreferences.Update(config, "gpu0-core", p => { p.ShowInWindow = true; p.Label = "Core!"; p.Notify = false; });
        var reading = new SensorReading("gpu0-core", "GPU0 Core", SensorKind.GpuCore, 0, 50.0);

        Assert.True(SensorDisplay.IsShownInWindow(config, reading));
        Assert.False(SensorDisplay.IsShownInTray(config, reading));
        Assert.False(SensorDisplay.NotificationsEnabled(config, reading));
        Assert.Equal("Core!", SensorDisplay.ResolveLabel(config, reading));
    }
}
```

Create `Torrid.Tests/ConfigManagerTests.cs`:

```csharp
using Torrid.Core.Config;

namespace Torrid.Tests;

public class ConfigManagerTests
{
    private sealed class CountingStore : IConfigStore
    {
        public int Saves { get; private set; }
        public bool Throw { get; set; }
        public AppConfig Load() => new();
        public void Save(AppConfig config)
        {
            if (Throw) throw new IOException("disk full");
            Saves++;
        }
    }

    [Fact]
    public void Update_MutatesConfigRaisesChangedAndSavesImmediatelyWhenDelayIsZero()
    {
        var store = new CountingStore();
        var manager = new ConfigManager(store, new AppConfig(), TimeSpan.Zero);
        var raised = 0;
        manager.Changed += (_, _) => raised++;

        manager.Update(c => c.PollIntervalMs = 4000);

        Assert.Equal(4000, manager.Config.PollIntervalMs);
        Assert.Equal(1, raised);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public void Update_WithDelay_DefersSaveUntilFlush_AndCoalescesManyUpdates()
    {
        var store = new CountingStore();
        var manager = new ConfigManager(store, new AppConfig(), TimeSpan.FromMinutes(5));

        manager.Update(c => c.PollIntervalMs = 1000);
        manager.Update(c => c.PollIntervalMs = 2000);
        manager.Update(c => c.PollIntervalMs = 3000);
        Assert.Equal(0, store.Saves);

        manager.Flush();

        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public void Flush_WithNothingPending_DoesNotSave()
    {
        var store = new CountingStore();
        var manager = new ConfigManager(store, new AppConfig(), TimeSpan.FromMinutes(5));

        manager.Flush();

        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public void SaveFailure_DoesNotThrow_AndIsReported()
    {
        var store = new CountingStore { Throw = true };
        var manager = new ConfigManager(store, new AppConfig(), TimeSpan.Zero);

        var exception = Record.Exception(() => manager.Update(c => c.PollIntervalMs = 1500));

        Assert.Null(exception);
        Assert.Contains("disk full", manager.LastSaveError);
    }

    [Fact]
    public void Dispose_FlushesPendingSave()
    {
        var store = new CountingStore();
        var manager = new ConfigManager(store, new AppConfig(), TimeSpan.FromMinutes(5));
        manager.Update(c => c.StartMinimizedToTray = false);

        manager.Dispose();

        Assert.Equal(1, store.Saves);
    }
}
```

In `Torrid.Tests/JsonConfigStoreTests.cs`: delete every test that references `VisibleSensorIds`, `CustomLabels`, or `NotificationDisabledSensorIds` (they no longer exist), and add:

```csharp
    [Fact]
    public void Load_WhenSensorsIsNullOrHasNullEntries_NormalizesToUsableDictionary()
    {
        File.WriteAllText(_path, "{\"Sensors\": null}");
        Assert.NotNull(new JsonConfigStore(_path).Load().Sensors);

        File.WriteAllText(_path, "{\"Sensors\": {\"cpu\": null, \"gpu0-vram\": {\"ShowInTray\": true}}}");
        var config = new JsonConfigStore(_path).Load();

        Assert.False(config.Sensors.ContainsKey("cpu"));
        Assert.True(config.Sensors["gpu0-vram"].ShowInTray);
    }

    [Fact]
    public void Load_OldSchemaFileStillLoads_IgnoringRemovedFields()
    {
        File.WriteAllText(_path, "{\"PollIntervalMs\": 3000, \"VisibleSensorIds\": [\"cpu\"], \"CustomLabels\": {\"cpu\": \"x\"}}");

        var config = new JsonConfigStore(_path).Load();

        Assert.Equal(3000, config.PollIntervalMs);
        Assert.Empty(config.Sensors);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsSensorPreferences()
    {
        var store = new JsonConfigStore(_path);
        var config = new AppConfig();
        config.Sensors["gpu0-core"] = new SensorPreference { Label = "MSI Core", ShowInWindow = true, Notify = false };

        store.Save(config);
        var loaded = store.Load();

        Assert.Equal("MSI Core", loaded.Sensors["gpu0-core"].Label);
        Assert.True(loaded.Sensors["gpu0-core"].ShowInWindow);
        Assert.False(loaded.Sensors["gpu0-core"].Notify);
    }
```

Also in `JsonConfigStoreTests.cs` add `using Torrid.Core.Config;` if missing.

- [ ] **Step 7: Run to verify failure**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter "SensorPreferencesTests|SensorDisplayTests|ConfigManagerTests|JsonConfigStoreTests"`
Expected: FAIL to build (new types/members missing).

- [ ] **Step 8: Implement preferences, display rules, config changes and ConfigManager**

Create `Torrid.Core/Config/SensorPreference.cs`:

```csharp
namespace Torrid.Core.Config;

public sealed class SensorPreference
{
    public string? Label { get; set; }
    public bool? ShowInWindow { get; set; }
    public bool? ShowInTray { get; set; }
    public bool? Notify { get; set; }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Label) && ShowInWindow is null && ShowInTray is null && Notify is null;
}
```

Create `Torrid.Core/Config/SensorPreferences.cs`:

```csharp
namespace Torrid.Core.Config;

public static class SensorPreferences
{
    public static void Update(AppConfig config, string sensorId, Action<SensorPreference> change)
    {
        var preference = config.Sensors.TryGetValue(sensorId, out var existing) ? existing : new SensorPreference();
        change(preference);

        if (preference.IsEmpty)
        {
            config.Sensors.Remove(sensorId);
        }
        else
        {
            config.Sensors[sensorId] = preference;
        }
    }
}
```

Replace `Torrid.Core/Config/SensorDisplay.cs`:

```csharp
namespace Torrid.Core.Config;

using Torrid.Core.Models;

public static class SensorDisplay
{
    public static bool IsShownInWindow(AppConfig config, SensorIdentity sensor) =>
        Preference(config, sensor.Id)?.ShowInWindow ?? SensorDefaults.ShownInWindow(sensor.Kind);

    public static bool IsShownInTray(AppConfig config, SensorIdentity sensor) =>
        Preference(config, sensor.Id)?.ShowInTray ?? SensorDefaults.ShownInTray(sensor.Kind);

    public static bool NotificationsEnabled(AppConfig config, SensorIdentity sensor) =>
        Preference(config, sensor.Id)?.Notify ?? SensorDefaults.Notify(sensor.Kind);

    public static string ResolveLabel(AppConfig config, SensorIdentity sensor) =>
        Preference(config, sensor.Id)?.Label is { } label && !string.IsNullOrWhiteSpace(label) ? label : sensor.DefaultLabel;

    public static bool IsShownInWindow(AppConfig config, SensorReading reading) =>
        IsShownInWindow(config, SensorIdentity.FromReading(reading));

    public static bool IsShownInTray(AppConfig config, SensorReading reading) =>
        IsShownInTray(config, SensorIdentity.FromReading(reading));

    public static bool NotificationsEnabled(AppConfig config, SensorReading reading) =>
        NotificationsEnabled(config, SensorIdentity.FromReading(reading));

    public static string ResolveLabel(AppConfig config, SensorReading reading) =>
        ResolveLabel(config, SensorIdentity.FromReading(reading));

    private static SensorPreference? Preference(AppConfig config, string sensorId) =>
        config.Sensors.TryGetValue(sensorId, out var preference) ? preference : null;
}
```

In `Torrid.Core/Config/AppConfig.cs` remove the three old properties (`VisibleSensorIds`, `CustomLabels`, `NotificationDisabledSensorIds`) and add:

```csharp
    public Dictionary<string, SensorPreference> Sensors { get; set; } = new();
```

In `Torrid.Core/Config/JsonConfigStore.cs` `Normalize`, replace the three `??=` lines for the removed collections with:

```csharp
        config.Sensors ??= new Dictionary<string, SensorPreference>();
        foreach (var key in config.Sensors.Where(p => p.Value is null).Select(p => p.Key).ToList())
        {
            config.Sensors.Remove(key);
        }
```

Create `Torrid.Core/Config/ConfigManager.cs`:

```csharp
namespace Torrid.Core.Config;

public sealed class ConfigManager : IDisposable
{
    private static readonly TimeSpan DefaultSaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly IConfigStore _store;
    private readonly TimeSpan _saveDelay;
    private readonly object _gate = new();
    private System.Threading.Timer? _timer;
    private bool _dirty;

    public ConfigManager(IConfigStore store, AppConfig? config = null, TimeSpan? saveDelay = null)
    {
        _store = store;
        Config = config ?? store.Load();
        _saveDelay = saveDelay ?? DefaultSaveDelay;
    }

    public AppConfig Config { get; }

    public string? LastSaveError { get; private set; }

    public event EventHandler? Changed;

    public void Update(Action<AppConfig> change)
    {
        change(Config);
        Changed?.Invoke(this, EventArgs.Empty);
        ScheduleSave();
    }

    public void Flush()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            if (!_dirty) return;
            SaveLocked();
        }
    }

    public void Dispose() => Flush();

    private void ScheduleSave()
    {
        lock (_gate)
        {
            _dirty = true;
            if (_saveDelay <= TimeSpan.Zero)
            {
                SaveLocked();
                return;
            }

            _timer?.Dispose();
            _timer = new System.Threading.Timer(_ => Flush(), null, _saveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void SaveLocked()
    {
        try
        {
            _store.Save(Config);
            LastSaveError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastSaveError = ex.Message;
        }
        finally
        {
            _dirty = false;
        }
    }
}
```

- [ ] **Step 9: Adapt and trim the App project so it builds against the new model**

This is a deliberately minimal adaptation — Tasks 3-5 replace these files; the goal here is only a green build and green tests.

1. Delete `Torrid.App/SettingsWindow.xaml`, `Torrid.App/SettingsWindow.xaml.cs`, `Torrid.App/ViewModels/SettingsViewModel.cs`, `Torrid.App/ViewModels/SensorSettingRowViewModel.cs`, `Torrid.App/ViewModels/ThresholdSettingRowViewModel.cs`, and `Torrid.Tests/SettingsViewModelTests.cs`.
2. `Torrid.App/ViewModels/MainWindowViewModel.cs`: change `SensorDisplay.IsVisible(_config, reading.Id)` to `SensorDisplay.IsShownInWindow(_config, reading)`. (`ResolveLabel(_config, reading)` keeps compiling unchanged.)
3. `Torrid.App/Tray/TrayIconGroupManager.cs`: change the `.Where(r => SensorDisplay.IsVisible(_config, r.Id))` filter to `.Where(r => SensorDisplay.IsShownInTray(_config, r))`.
4. `Torrid.App/MainWindow.xaml.cs`: remove the `OpenSettings()` method and the `SettingsRequested` handler body's call to it — make `SettingsRequested` simply bring the window forward (same three lines as the `TrayIconClicked` handler: `Show(); WindowState = WindowState.Normal; Activate();`); change `OnReadingsUpdatedForNotifications` to `readings.Where(r => SensorDisplay.NotificationsEnabled(_config, r)).Select(r => r with { Label = SensorDisplay.ResolveLabel(_config, r) }).ToList()` (use the `SensorDisplay` already imported through `Torrid.Core.Config`; drop the verbose `Core.Config.` prefixes).
5. `Torrid.Tests/MainWindowViewModelTests.cs`: rewrite the config setup in each test to the new model — a visible-by-default sensor is `cpu` or a `GpuHotSpot` reading; "hidden by default" is `gpu0-core`; a custom label is `config.Sensors["cpu"] = new SensorPreference { Label = "5950X" }`; hiding is `SensorPreferences.Update(config, "cpu", p => p.ShowInWindow = false)`; showing a normally hidden sensor is `p.ShowInWindow = true`. Keep the four existing test intents: custom label used, a sensor hidden by default gets no row, a sensor that becomes hidden loses its row, and `DeviceName` mapping (`CPU` / `GPU 0` — make the GPU reading a `gpu0-hotspot` so it is shown by default).

- [ ] **Step 10: Run the full suite**

Run: `& "C:\Program Files\dotnet\dotnet.exe" build Torrid.sln` then `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.sln`
Expected: build succeeds with 0 errors; all tests pass.

- [ ] **Step 11: Commit**

```bash
git add -A Torrid.Core Torrid.App Torrid.Tests
git commit -m "Unify per-sensor preferences (kind defaults, window/tray/alerts), add ConfigManager"
```

---

### Task 2: Anti-cheat aware hardware access (Torrid.Core)

**Why:** the user reports Torrid interfering with Vanguard "and maybe other anticheat systems". Findings from investigating this machine: LibreHardwareMonitorLib 0.9.6 uses the PawnIO kernel driver (not the old blocklisted WinRing0), PawnIO is installed and auto-starts independently of Torrid, Vanguard's `vgk` driver is running, Vanguard's own logs are encrypted and record no readable error, and Torrid additionally holds a privileged driver handle (CPU temperature) and polls the GPU through NVAPI/D3DKMT every 2 s — both things anti-cheats and games can object to. Torrid cannot unload PawnIO, but it can stop touching it, and stop polling, for as long as an anti-cheat is running.

**Files:**
- Create: `Torrid.Core/Sensors/AntiCheat.cs` (types + detector + policy + process lister)
- Create: `Torrid.Core/Sensors/IHardwareStatusSource.cs`
- Modify: `Torrid.Core/Config/AppConfig.cs`
- Modify: `Torrid.Core/Config/JsonConfigStore.cs`
- Modify: `Torrid.Core/Sensors/SensorReadingBuilder.cs`
- Modify: `Torrid.Core/Sensors/SensorService.cs`
- Modify (App): `Torrid.App/Sensors/FakeSensorSource.cs`, `Torrid.App/MainWindow.xaml.cs` (constructor call only)
- Tests: `Torrid.Tests/AntiCheatDetectorTests.cs`, `Torrid.Tests/HardwareAccessPolicyTests.cs`, `Torrid.Tests/SensorReadingBuilderTests.cs` (extend), `Torrid.Tests/JsonConfigStoreTests.cs` (extend)

**Interfaces:**
- Consumes: `SensorReading`, `SensorReadingBuilder`, `AppConfig`.
- Produces:
  - `enum AntiCheatBehavior { Pause, GpuOnly, Continue }` and `enum HardwareAccessLevel { Full, GpuOnly, Paused }` (namespace `Torrid.Core.Sensors`).
  - `sealed record AntiCheatMatch(string Product, string ProcessName)`; `sealed record HardwareStatus(HardwareAccessLevel Level, AntiCheatMatch? AntiCheat)` with `static HardwareStatus FullAccess`.
  - `static class AntiCheatDetector { AntiCheatMatch? Detect(IEnumerable<string> runningProcessNames, IEnumerable<string>? extraProcessNames = null); IReadOnlyList<string> KnownProducts }`.
  - `static class HardwareAccessPolicy { HardwareAccessLevel Decide(AntiCheatBehavior, AntiCheatMatch?); HardwareStatus Evaluate(AntiCheatBehavior, IEnumerable<string> runningProcessNames, IEnumerable<string>? extraProcessNames) }`.
  - `interface IProcessLister { IReadOnlyCollection<string> GetRunningProcessNames(); }` and `sealed class SystemProcessLister : IProcessLister`.
  - `interface IHardwareStatusSource { HardwareStatus Status { get; } event EventHandler? StatusChanged; }` — implemented by `SensorService` and `FakeSensorSource`.
  - `AppConfig.WhileAntiCheatActive : AntiCheatBehavior` (default `Pause`, serialized as a string) and `AppConfig.ExtraProtectedProcesses : List<string>` (default empty; treated as replace-wholesale-never-mutate).
  - `SensorService(int pollIntervalMs, Func<AntiCheatBehavior>? behavior = null, Func<IReadOnlyList<string>>? extraProcesses = null, IProcessLister? processLister = null)`.
  - `SensorReadingBuilder.WithCpuPlaceholder(IReadOnlyList<SensorReading>) -> IReadOnlyList<SensorReading>` (inserts an unavailable `cpu` reading first when none exists).

- [ ] **Step 1: Failing tests for detection and policy**

Create `Torrid.Tests/AntiCheatDetectorTests.cs`:

```csharp
using Torrid.Core.Sensors;

namespace Torrid.Tests;

public class AntiCheatDetectorTests
{
    [Theory]
    [InlineData("vgc", "Riot Vanguard")]
    [InlineData("VALORANT-Win64-Shipping", "Riot Vanguard")]
    [InlineData("EasyAntiCheat_EOS", "Easy Anti-Cheat")]
    [InlineData("BEService", "BattlEye")]
    [InlineData("FACEIT", "FACEIT Anti-Cheat")]
    [InlineData("SGuard64", "Tencent ACE")]
    [InlineData("PnkBstrA", "PunkBuster")]
    [InlineData("GameMon64", "nProtect GameGuard")]
    public void Detect_RecognisesKnownAntiCheatProcesses(string process, string product)
    {
        var match = AntiCheatDetector.Detect(new[] { "explorer", process, "chrome" });

        Assert.NotNull(match);
        Assert.Equal(product, match!.Product);
        Assert.Equal(process, match.ProcessName);
    }

    [Fact]
    public void Detect_IsCaseInsensitive_AndIgnoresExeSuffix()
    {
        Assert.NotNull(AntiCheatDetector.Detect(new[] { "VGC.EXE" }));
        Assert.NotNull(AntiCheatDetector.Detect(new[] { "easyanticheat" }));
    }

    [Fact]
    public void Detect_ReturnsNullWhenNothingMatches()
    {
        Assert.Null(AntiCheatDetector.Detect(new[] { "explorer", "chrome", "vgtray" }));
    }

    [Fact]
    public void Detect_VanguardTrayAloneIsNotEnough()
    {
        // vgtray runs whenever Vanguard is installed; only the vgc service means Vanguard is actively up.
        Assert.Null(AntiCheatDetector.Detect(new[] { "vgtray" }));
    }

    [Fact]
    public void Detect_HonoursUserSuppliedProcesses_NormalisingNames()
    {
        var match = AntiCheatDetector.Detect(new[] { "explorer", "MyGame" }, new[] { " mygame.exe ", "" });

        Assert.NotNull(match);
        Assert.Equal("Custom", match!.Product);
        Assert.Equal("MyGame", match.ProcessName);
    }

    [Fact]
    public void Detect_IgnoresBlankUserEntries()
    {
        Assert.Null(AntiCheatDetector.Detect(new[] { "explorer" }, new[] { "", "   " }));
    }

    [Fact]
    public void KnownProducts_ListsEveryBuiltInProduct()
    {
        var products = AntiCheatDetector.KnownProducts;

        Assert.Contains("Riot Vanguard", products);
        Assert.Contains("Easy Anti-Cheat", products);
        Assert.Contains("BattlEye", products);
    }
}
```

Create `Torrid.Tests/HardwareAccessPolicyTests.cs`:

```csharp
using Torrid.Core.Sensors;

namespace Torrid.Tests;

public class HardwareAccessPolicyTests
{
    private static readonly AntiCheatMatch Vanguard = new("Riot Vanguard", "vgc");

    [Theory]
    [InlineData(AntiCheatBehavior.Pause)]
    [InlineData(AntiCheatBehavior.GpuOnly)]
    [InlineData(AntiCheatBehavior.Continue)]
    public void NoAntiCheat_AlwaysFullAccess(AntiCheatBehavior behavior)
    {
        Assert.Equal(HardwareAccessLevel.Full, HardwareAccessPolicy.Decide(behavior, null));
    }

    [Theory]
    [InlineData(AntiCheatBehavior.Pause, HardwareAccessLevel.Paused)]
    [InlineData(AntiCheatBehavior.GpuOnly, HardwareAccessLevel.GpuOnly)]
    [InlineData(AntiCheatBehavior.Continue, HardwareAccessLevel.Full)]
    public void AntiCheatRunning_FollowsTheConfiguredBehavior(AntiCheatBehavior behavior, HardwareAccessLevel expected)
    {
        Assert.Equal(expected, HardwareAccessPolicy.Decide(behavior, Vanguard));
    }

    [Fact]
    public void Evaluate_CombinesDetectionAndPolicy()
    {
        var status = HardwareAccessPolicy.Evaluate(AntiCheatBehavior.Pause, new[] { "explorer", "vgc" }, null);

        Assert.Equal(HardwareAccessLevel.Paused, status.Level);
        Assert.Equal("Riot Vanguard", status.AntiCheat!.Product);
    }

    [Fact]
    public void Evaluate_WithNothingRunning_IsFullAccessWithNoMatch()
    {
        var status = HardwareAccessPolicy.Evaluate(AntiCheatBehavior.Pause, new[] { "explorer" }, null);

        Assert.Equal(HardwareStatus.FullAccess, status);
    }
}
```

Append to `Torrid.Tests/SensorReadingBuilderTests.cs` (inside the class):

```csharp
    [Fact]
    public void WithCpuPlaceholder_InsertsUnavailableCpuFirstWhenMissing()
    {
        var gpu = new SensorReading("gpu0-hotspot", "GPU0 Hot", SensorKind.GpuHotSpot, 0, 51.0);

        var readings = SensorReadingBuilder.WithCpuPlaceholder(new[] { gpu });

        Assert.Equal(2, readings.Count);
        Assert.Equal("cpu", readings[0].Id);
        Assert.Null(readings[0].ValueCelsius);
        Assert.Equal("gpu0-hotspot", readings[1].Id);
    }

    [Fact]
    public void WithCpuPlaceholder_LeavesAnExistingCpuReadingAlone()
    {
        var cpu = new SensorReading("cpu", "CPU", SensorKind.CpuPackage, null, 60.0);

        var readings = SensorReadingBuilder.WithCpuPlaceholder(new[] { cpu });

        Assert.Single(readings);
        Assert.Equal(60.0, readings[0].ValueCelsius);
    }
```

Append to `Torrid.Tests/JsonConfigStoreTests.cs` (inside the class):

```csharp
    [Fact]
    public void AntiCheatSettings_RoundTripAndDefaultToPause()
    {
        var defaults = new JsonConfigStore(_path).Load();
        Assert.Equal(AntiCheatBehavior.Pause, defaults.WhileAntiCheatActive);
        Assert.Empty(defaults.ExtraProtectedProcesses);

        var store = new JsonConfigStore(_path);
        store.Save(new AppConfig { WhileAntiCheatActive = AntiCheatBehavior.GpuOnly, ExtraProtectedProcesses = new List<string> { "mygame" } });
        var loaded = store.Load();

        Assert.Equal(AntiCheatBehavior.GpuOnly, loaded.WhileAntiCheatActive);
        Assert.Equal(new[] { "mygame" }, loaded.ExtraProtectedProcesses);
        Assert.Contains("GpuOnly", File.ReadAllText(_path));
    }

    [Fact]
    public void Load_NullExtraProcessesOrBogusBehavior_FallBackToSafeValues()
    {
        File.WriteAllText(_path, "{\"ExtraProtectedProcesses\": null, \"WhileAntiCheatActive\": 99}");

        var config = new JsonConfigStore(_path).Load();

        Assert.NotNull(config.ExtraProtectedProcesses);
        Assert.Equal(AntiCheatBehavior.Pause, config.WhileAntiCheatActive);
    }
```

Add `using Torrid.Core.Sensors;` to the top of `JsonConfigStoreTests.cs` if missing.

- [ ] **Step 2: Run to verify failure**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter "AntiCheatDetectorTests|HardwareAccessPolicyTests|SensorReadingBuilderTests|JsonConfigStoreTests"`
Expected: FAIL to build (types missing).

- [ ] **Step 3: Implement detection, policy and config**

Create `Torrid.Core/Sensors/AntiCheat.cs`:

```csharp
namespace Torrid.Core.Sensors;

using System.Diagnostics;

public enum AntiCheatBehavior { Pause, GpuOnly, Continue }

public enum HardwareAccessLevel { Full, GpuOnly, Paused }

public sealed record AntiCheatMatch(string Product, string ProcessName);

public sealed record HardwareStatus(HardwareAccessLevel Level, AntiCheatMatch? AntiCheat)
{
    public static HardwareStatus FullAccess { get; } = new(HardwareAccessLevel.Full, null);
}

public static class AntiCheatDetector
{
    // Process names of the anti-cheat *services/clients* that only run while protection is active.
    // vgtray (Vanguard's always-on tray icon) is deliberately absent; "vgc" is the Vanguard service.
    private static readonly (string Product, string[] Processes)[] Known =
    {
        ("Riot Vanguard", new[] { "vgc", "VALORANT-Win64-Shipping" }),
        ("Easy Anti-Cheat", new[] { "EasyAntiCheat", "EasyAntiCheat_EOS", "start_protected_game" }),
        ("BattlEye", new[] { "BEService", "BEService_x64" }),
        ("FACEIT Anti-Cheat", new[] { "FACEIT" }),
        ("Tencent ACE", new[] { "SGuard64", "SGuardSvc64" }),
        ("PunkBuster", new[] { "PnkBstrA", "PnkBstrB" }),
        ("nProtect GameGuard", new[] { "GameMon", "GameMon64", "npggsvc" }),
    };

    public static IReadOnlyList<string> KnownProducts { get; } = Known.Select(k => k.Product).ToList();

    public static AntiCheatMatch? Detect(IEnumerable<string> runningProcessNames, IEnumerable<string>? extraProcessNames = null)
    {
        var running = new HashSet<string>(runningProcessNames.Select(Normalize), StringComparer.OrdinalIgnoreCase);

        foreach (var (product, processes) in Known)
        {
            foreach (var process in processes)
            {
                if (running.Contains(process)) return new AntiCheatMatch(product, process);
            }
        }

        if (extraProcessNames is not null)
        {
            foreach (var extra in extraProcessNames.Select(Normalize).Where(n => n.Length > 0))
            {
                if (running.Contains(extra))
                {
                    var actual = runningProcessNames.Select(Normalize).First(n => string.Equals(n, extra, StringComparison.OrdinalIgnoreCase));
                    return new AntiCheatMatch("Custom", actual);
                }
            }
        }

        return null;
    }

    private static string Normalize(string name)
    {
        var trimmed = name.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? trimmed[..^4] : trimmed;
    }
}

public static class HardwareAccessPolicy
{
    public static HardwareAccessLevel Decide(AntiCheatBehavior behavior, AntiCheatMatch? match) =>
        match is null
            ? HardwareAccessLevel.Full
            : behavior switch
            {
                AntiCheatBehavior.Pause => HardwareAccessLevel.Paused,
                AntiCheatBehavior.GpuOnly => HardwareAccessLevel.GpuOnly,
                _ => HardwareAccessLevel.Full,
            };

    public static HardwareStatus Evaluate(AntiCheatBehavior behavior, IEnumerable<string> runningProcessNames, IEnumerable<string>? extraProcessNames)
    {
        var match = AntiCheatDetector.Detect(runningProcessNames, extraProcessNames);
        return match is null ? HardwareStatus.FullAccess : new HardwareStatus(Decide(behavior, match), match);
    }
}

public interface IProcessLister
{
    IReadOnlyCollection<string> GetRunningProcessNames();
}

public sealed class SystemProcessLister : IProcessLister
{
    public IReadOnlyCollection<string> GetRunningProcessNames()
    {
        var names = new List<string>();
        foreach (var process in Process.GetProcesses())
        {
            try { names.Add(process.ProcessName); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            finally { process.Dispose(); }
        }
        return names;
    }
}
```

Create `Torrid.Core/Sensors/IHardwareStatusSource.cs`:

```csharp
namespace Torrid.Core.Sensors;

public interface IHardwareStatusSource
{
    HardwareStatus Status { get; }

    event EventHandler? StatusChanged;
}
```

In `Torrid.Core/Config/AppConfig.cs` add (and `using System.Text.Json.Serialization;` and `using Torrid.Core.Sensors;`):

```csharp
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AntiCheatBehavior WhileAntiCheatActive { get; set; } = AntiCheatBehavior.Pause;

    public List<string> ExtraProtectedProcesses { get; set; } = new();
```

In `JsonConfigStore.Normalize` add before `return config;`:

```csharp
        config.ExtraProtectedProcesses ??= new List<string>();
        if (!Enum.IsDefined(config.WhileAntiCheatActive))
        {
            config.WhileAntiCheatActive = AntiCheatBehavior.Pause;
        }
```
(add `using Torrid.Core.Sensors;` to that file.)

Add to `SensorReadingBuilder`:

```csharp
    public static IReadOnlyList<SensorReading> WithCpuPlaceholder(IReadOnlyList<SensorReading> readings)
    {
        if (readings.Any(r => r.Id == "cpu")) return readings;

        var list = new List<SensorReading> { new("cpu", "CPU", SensorKind.CpuPackage, null, null) };
        list.AddRange(readings);
        return list;
    }
```

- [ ] **Step 4: Run to verify the new Core tests pass**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter "AntiCheatDetectorTests|HardwareAccessPolicyTests|SensorReadingBuilderTests|JsonConfigStoreTests"`
Expected: PASS. (If `Torrid.App` fails to build because of the not-yet-updated `SensorService` consumers, finish Step 5 first and re-run — Step 5 is part of this task.)

- [ ] **Step 5: Make `SensorService` anti-cheat aware (hardware wiring, verified by build + manual run, not unit tests)**

Replace `Torrid.Core/Sensors/SensorService.cs` in full:

```csharp
namespace Torrid.Core.Sensors;

using LibreHardwareMonitor.Hardware;
using Torrid.Core.Models;

public sealed class SensorService : ISensorSource, IHardwareStatusSource, IDisposable
{
    private readonly System.Timers.Timer _timer;
    private readonly UpdateVisitor _visitor = new();
    private readonly Func<AntiCheatBehavior> _behavior;
    private readonly Func<IReadOnlyList<string>> _extraProcesses;
    private readonly IProcessLister _processLister;
    private readonly object _gate = new();
    private Computer? _computer;
    private HardwareAccessLevel? _openLevel;
    private bool _disposed;

    public SensorService(
        int pollIntervalMs,
        Func<AntiCheatBehavior>? behavior = null,
        Func<IReadOnlyList<string>>? extraProcesses = null,
        IProcessLister? processLister = null)
    {
        _behavior = behavior ?? (() => AntiCheatBehavior.Pause);
        _extraProcesses = extraProcesses ?? (() => Array.Empty<string>());
        _processLister = processLister ?? new SystemProcessLister();

        _timer = new System.Timers.Timer(pollIntervalMs) { AutoReset = false };
        _timer.Elapsed += (_, _) => PollAndReschedule();

        Poll();
    }

    public IReadOnlyList<SensorReading> CurrentReadings { get; private set; } = Array.Empty<SensorReading>();

    public HardwareStatus Status { get; private set; } = HardwareStatus.FullAccess;

    public int PollIntervalMs
    {
        get => (int)_timer.Interval;
        set => _timer.Interval = Math.Max(1, value);
    }

    public event EventHandler<IReadOnlyList<SensorReading>>? ReadingsUpdated;

    public event EventHandler? StatusChanged;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    private void PollAndReschedule()
    {
        try
        {
            Poll();
        }
        finally
        {
            if (!_disposed) _timer.Start();
        }
    }

    private void Poll()
    {
        IReadOnlyList<SensorReading> readings;
        var statusChanged = false;

        lock (_gate)
        {
            if (_disposed) return;

            try
            {
                // Policy first: an already-running anti-cheat means the hardware (and its driver) is never opened.
                var status = HardwareAccessPolicy.Evaluate(
                    _behavior(), _processLister.GetRunningProcessNames(), _extraProcesses());
                statusChanged = !Equals(status, Status);
                Status = status;

                EnsureComputerFor(status.Level);
                readings = status.Level == HardwareAccessLevel.Paused
                    ? SensorReadingBuilder.ToUnavailable(CurrentReadings)
                    : ReadHardware(status.Level);
            }
            catch
            {
                readings = SensorReadingBuilder.ToUnavailable(CurrentReadings);
            }

            CurrentReadings = readings;
        }

        // Raised outside the lock: handlers marshal to the UI thread and must never be able to deadlock Dispose().
        if (statusChanged) StatusChanged?.Invoke(this, EventArgs.Empty);
        ReadingsUpdated?.Invoke(this, readings);
    }

    private void EnsureComputerFor(HardwareAccessLevel level)
    {
        if (_openLevel == level) return;

        CloseComputer();
        if (level == HardwareAccessLevel.Paused) return;

        var computer = new Computer { IsCpuEnabled = level == HardwareAccessLevel.Full, IsGpuEnabled = true };
        computer.Open();
        _computer = computer;
        _openLevel = level;
    }

    private IReadOnlyList<SensorReading> ReadHardware(HardwareAccessLevel level)
    {
        _computer!.Accept(_visitor);
        var readings = SensorReadingBuilder.Build(_computer.Hardware.Select(ToSnapshot).ToList());
        return level == HardwareAccessLevel.GpuOnly ? SensorReadingBuilder.WithCpuPlaceholder(readings) : readings;
    }

    private void CloseComputer()
    {
        var computer = _computer;
        _computer = null;
        _openLevel = null;
        try { computer?.Close(); }
        catch { /* releasing hardware must never take the app down */ }
    }

    private static HardwareSnapshot ToSnapshot(IHardware hardware) => new(
        hardware.HardwareType,
        hardware.Name,
        hardware.Sensors.Select(s => new SensorSnapshot(s.Name, s.SensorType, s.Value)).ToList(),
        hardware.SubHardware.Select(ToSnapshot).ToList());

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _timer.Dispose();
            CloseComputer();
        }
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

Update `Torrid.App/Sensors/FakeSensorSource.cs` so it also implements `IHardwareStatusSource` — add `, IHardwareStatusSource` to the class declaration, a settable `public HardwareStatus Status { get; set; } = HardwareStatus.FullAccess;`, and `public event EventHandler? StatusChanged { add { } remove { } }` (the demo status never changes while running). Also give each fake reading plausible `HardwareName` / `SourceSensorName` values (`"AMD Ryzen 9 5950X"` / `"Core (Tctl/Tdie)"` for the CPU; `"NVIDIA GeForce RTX 3090"` with `"GPU Core"`, `"GPU Hot Spot"`, `"GPU Memory Junction"` for each GPU) by adding the two trailing arguments to each `new SensorReading(...)` in `BuildReadings`. If `Status.Level == Paused`, `BuildReadings` returns every reading with a `null` value.

In `Torrid.App/MainWindow.xaml.cs` change the one construction `new SensorService(_config.PollIntervalMs)` to:

```csharp
        _sensorSource = new SensorService(
            _config.PollIntervalMs,
            () => _config.WhileAntiCheatActive,
            () => _config.ExtraProtectedProcesses);
```

- [ ] **Step 6: Build, test, and sanity-run**

Run: `& "C:\Program Files\dotnet\dotnet.exe" build Torrid.sln` then `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.sln`
Expected: 0 errors, all tests pass.

Sanity run (Debug is non-elevated): launch the app with a throwaway config and confirm it starts and stays up without exceptions for ~10 s, then stop it:
```powershell
$p = Start-Process "Torrid.App\bin\Debug\net8.0-windows10.0.17763.0\Torrid.App.exe" -PassThru
Start-Sleep 10; $p.HasExited; Stop-Process -Id $p.Id -Force
```
Expected: `False` (still running) before the stop. Then, to exercise the pause path without a real anti-cheat, temporarily run a harmless process named `vgc` (copy `notepad.exe` to `$env:TEMP\vgc.exe` and start it), start Torrid, confirm it stays up and — by attaching a quick throwaway console harness or logging — that `SensorService.Status.Level` becomes `Paused` within one poll interval and returns to `Full` after you stop that process. Remove the temporary exe and any harness afterwards. Report exactly what you observed.

- [ ] **Step 7: Commit**

```bash
git add -A Torrid.Core Torrid.App Torrid.Tests
git commit -m "Back off from hardware access while an anti-cheat is running (pause/GPU-only/continue)"
```

---

### Task 3: Theme, app shell, AppHost, and the Home dashboard (Torrid.App)

**Files:**
- Create: `Torrid.App/Themes/Theme.xaml`
- Modify: `Torrid.App/App.xaml`, `Torrid.App/App.xaml.cs`
- Create: `Torrid.App/StartupOptions.cs`, `Torrid.App/AppHost.cs`, `Torrid.App/UiThread.cs`
- Create: `Torrid.App/Interop/DarkTitleBar.cs`
- Replace: `Torrid.App/MainWindow.xaml`, `Torrid.App/MainWindow.xaml.cs`
- Create: `Torrid.App/Views/HomeView.xaml`, `Torrid.App/Views/HomeView.xaml.cs`
- Create: `Torrid.App/ViewModels/ObservableObject.cs`, `RelayCommand.cs`, `ShellViewModel.cs`, `HomeViewModel.cs`, `DeviceGroupViewModel.cs`, `SensorCardViewModel.cs`
- Modify: `Torrid.App/ColorPalette.cs`, `Torrid.App/Converters/ColorZoneToBrushConverter.cs` (only if needed), `Torrid.App/Tray/SingleValueBitmapRenderer.cs` (gray for unavailable)
- Delete: `Torrid.App/ViewModels/MainWindowViewModel.cs`, `Torrid.App/ViewModels/SensorRowViewModel.cs`, `Torrid.Tests/MainWindowViewModelTests.cs`
- Tests: `Torrid.Tests/StartupOptionsTests.cs`, `Torrid.Tests/ShellViewModelTests.cs`, `Torrid.Tests/HomeViewModelTests.cs`, `Torrid.Tests/ColorPaletteTests.cs` (update values)

**Interfaces:**
- Consumes: Task 1 (`ConfigManager`, `SensorDisplay`, `SensorIdentity`, `SensorReading` names) and Task 2 (`IHardwareStatusSource`, `HardwareStatus`, `SensorService`).
- Produces (Tasks 4-6 rely on these):
  - `Torrid.App.StartupOptions(bool ShowWindow, bool Demo, bool DemoPaused, string? ConfigPath)` with `static StartupOptions Parse(IReadOnlyList<string> args)` — flags `--show`, `--demo`, `--demo-paused` (implies `--demo`), `--config <path>`.
  - `Torrid.App.UiThread.Post(Action)` — `Application.Current?.Dispatcher.BeginInvoke(action)`, or runs inline when there is no `Application` (unit tests).
  - `ObservableObject : INotifyPropertyChanged` with `protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)` and `protected void Raise(string name)`; `RelayCommand : ICommand` (`RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)`, plus `RaiseCanExecuteChanged()`).
  - `enum PageId { Home, Sensors, Thresholds, Settings }`; `sealed class NavItem(PageId id, string title, string glyph, object page)` with matching read-only properties `Id`, `Title`, `Glyph`, `Page`; `sealed class ShellViewModel : ObservableObject` — `ShellViewModel(IEnumerable<NavItem> items)`, `IReadOnlyList<NavItem> Items`, `NavItem Selected { get; set; }`, `object CurrentPage`, `void Navigate(PageId id)` (no-op for an unregistered id).
  - `HomeViewModel(ISensorSource sensors, IHardwareStatusSource hardware, ConfigManager config)` — `ObservableCollection<DeviceGroupViewModel> Groups`, `string? StatusMessage`, `bool HasStatusMessage`, `bool IsEmpty`; `DeviceGroupViewModel { string Title; string? Subtitle; ObservableCollection<SensorCardViewModel> Cards }`; `SensorCardViewModel { string Id; string Label; string? Caption; string ValueText; bool IsAvailable; ColorZone Zone; double Fill }` (all `INotifyPropertyChanged`).
  - `AppHost(StartupOptions options)` composition root exposing `ConfigManager ConfigManager`, `ISensorSource Sensors`, `IHardwareStatusSource Hardware`, `ShellViewModel Shell`, `NotificationService Notifications`, `TrayIconGroupManager Tray`, `void ShowMainWindow(PageId? page = null)`, `void Exit()`, `IDisposable`. Tasks 4-5 add pages, tray menu behaviour and notification wiring through it.

**Design spec (implement to this; look at your screenshots and iterate until it matches):**

*Window*: title "Torrid", default 1000x680, `MinWidth` 780, `MinHeight` 520, background `#20232A`, standard (dark, see below) title bar, `UseLayoutRounding`/`SnapsToDevicePixels`. Dark title bar: in the window's `SourceInitialized` call `DwmSetWindowAttribute(hwnd, 20 /*DWMWA_USE_IMMERSIVE_DARK_MODE*/, ref int one, 4)` (put the P/Invoke in `Interop/DarkTitleBar.cs`).

*Shell layout*: two columns. Left sidebar 200 px wide, background `#181B20`: a "Torrid" wordmark (20 px semibold white) with a small thermometer glyph on top (margin 20), then the nav items as `RadioButton`s styled as full-width rows (44 px tall, 20 px left padding): a Segoe Fluent Icons glyph (18 px) + title (14 px); selected row has background `#FFFFFF14` and a 3 px accent-colour bar on its left edge; hover `#FFFFFF0C`; text secondary colour, white when selected. Glyphs: Home `&#xE80F;`, Sensors `&#xE9CA;`, Thresholds `&#xE9D9;`, Settings `&#xE713;`. Right side: a `ContentControl` bound to `Shell.CurrentPage` with `DataTemplate`s mapping each page view model type to its view.

*Home page*: 28 px page title "Home" (semibold white) with a 13 px secondary subtitle "Live temperatures — choose what shows up on the Sensors page." under it. A status banner (only when `HasStatusMessage`): rounded 8 px, background `#3A2F12`, border `#7A5F1C`, an info glyph + the message in `#FFD98A`. Then the device groups: each group has a header row (device title in 13 px semibold uppercase secondary colour, letter-spaced; subtitle e.g. "AMD Ryzen 9 5950X" in 12 px muted beside it) followed by a `WrapPanel` of cards. If `IsEmpty`, show a centered muted message "No sensors are switched on. Open Sensors to choose what to show." Page scrolls vertically when it overflows.

*Sensor card* (`Border`, 220 x 150, margin 0 14 14 0, CornerRadius 10, background `#14306B`, border `#2B4A8F` 1 px, padding 16): label (14 px, secondary text colour) top-left; the value dominant — a 56 px semibold number coloured by `Zone` (blue/green/yellow/red hex above; `#8A94A6` and the text `--` when unavailable) with a smaller 20 px `°C` suffix baseline-aligned in the same colour at 70 % opacity; the `Caption` (the real sensor name, e.g. "GPU Hot Spot") in 11 px muted text under the label; and along the bottom a 6 px high rounded track (`#FFFFFF1A`) whose fill (width = `Fill` x track width, zone colour) shows how close the reading is to the red limit. Cards animate nothing; keep it crisp.

*Fill*: `Fill = Math.Clamp(value / (YellowMax * 1.15), 0, 1)` using the sensor's threshold band; `0` when unavailable.

*Palette*: update `ColorPalette` constants to the zone colours in Global Constraints and update `ColorPaletteTests` expectations to match (blue `0x5A,0xA9,0xFF`, green `0x4E,0xD3,0x6B`, yellow `0xFF,0xC9,0x3C`, red `0xFF,0x5C,0x4D`, unknown `0x8A,0x94,0xA6`). `SingleValueBitmapRenderer.Render` must draw the unavailable colour (`ColorPalette.Unknown`) when `valueCelsius` is null (keep `ResolveZone`'s existing behaviour and tests).

*Theme.xaml*: define the colour and brush resources named in Global Constraints (`WindowBackgroundBrush`, `SidebarBackgroundBrush`, `CardBackgroundBrush`, `CardBorderBrush`, `TextPrimaryBrush`, `TextSecondaryBrush`, `TextMutedBrush`, `AccentBrush`, `ZoneBlueBrush`, `ZoneGreenBrush`, `ZoneYellowBrush`, `ZoneRedBrush`, `ZoneUnknownBrush`), implicit `TextBlock` foreground/font, the `NavButtonStyle` (RadioButton), `CardStyle`, `PageTitleStyle`, `SectionTitleStyle`. Later tasks add input-control styles to this same file.

*Startup behaviour*: `App.xaml` loses nothing it needs but gains `Theme.xaml` in `Application.Resources` merged dictionaries; `App.OnStartup` builds `StartupOptions.Parse(e.Args)`, creates the `AppHost`, creates `MainWindow` with `DataContext = host.Shell`, and shows the window only if `options.ShowWindow || !host.ConfigManager.Config.StartMinimizedToTray`. `App.OnExit` disposes the host (which flushes config). Keep the existing `SetCurrentProcessExplicitAppUserModelID` call and `ShutdownMode="OnExplicitShutdown"`. `MainWindow` keeps the hide-on-close behaviour: `Closing` cancels and hides unless exiting; expose `ExitApplication()` which sets the flag and calls `Application.Current.Shutdown()`. `AppHost.ShowMainWindow(page)` navigates the shell if `page` is given, then `Show(); WindowState = Normal; Activate();`. `AppHost.Exit()` calls `MainWindow.ExitApplication()`.

*AppHost wiring (functionally equal to today's `MainWindow` constructor, relocated)*: `ConfigManager = new ConfigManager(new JsonConfigStore(options.ConfigPath))`; sensor source = `FakeSensorSource` (with `Status` set to a Paused/"Riot Vanguard" `HardwareStatus` when `DemoPaused`) when `options.Demo`, else `SensorService(config.PollIntervalMs, () => ConfigManager.Config.WhileAntiCheatActive, () => ConfigManager.Config.ExtraProtectedProcesses)`; `NotificationService(new WindowsToastNotifier(), new SystemClock(), config.NotificationRefireMinutes)`; subscribe `ReadingsUpdated` → `UiThread.Post` → evaluate notifications for readings where `SensorDisplay.NotificationsEnabled`, with labels resolved; `Tray = new TrayIconGroupManager(sensors, ConfigManager.Config)` (still the current constructor — Task 5 changes it) with `TrayIconClicked`/`SettingsRequested` → `ShowMainWindow()`, `ExitRequested` → `Exit()`; `Start()` starts the sensor source. Page registration: only Home in this task.

- [ ] **Step 1: Failing tests (pure logic)**

Create `Torrid.Tests/StartupOptionsTests.cs`:

```csharp
using Torrid.App;

namespace Torrid.Tests;

public class StartupOptionsTests
{
    [Fact]
    public void NoArguments_UsesDefaults()
    {
        var options = StartupOptions.Parse(Array.Empty<string>());

        Assert.False(options.ShowWindow);
        Assert.False(options.Demo);
        Assert.False(options.DemoPaused);
        Assert.Null(options.ConfigPath);
    }

    [Fact]
    public void Flags_AreParsedCaseInsensitively()
    {
        var options = StartupOptions.Parse(new[] { "--SHOW", "--demo", "--config", @"C:\temp\c.json" });

        Assert.True(options.ShowWindow);
        Assert.True(options.Demo);
        Assert.Equal(@"C:\temp\c.json", options.ConfigPath);
    }

    [Fact]
    public void DemoPaused_ImpliesDemo()
    {
        var options = StartupOptions.Parse(new[] { "--demo-paused" });

        Assert.True(options.Demo);
        Assert.True(options.DemoPaused);
    }

    [Fact]
    public void ConfigFlagWithoutValue_IsIgnored()
    {
        Assert.Null(StartupOptions.Parse(new[] { "--config" }).ConfigPath);
    }
}
```

Create `Torrid.Tests/ShellViewModelTests.cs`:

```csharp
using Torrid.App.ViewModels;

namespace Torrid.Tests;

public class ShellViewModelTests
{
    private static ShellViewModel Make(out object home, out object sensors)
    {
        home = new object();
        sensors = new object();
        return new ShellViewModel(new[]
        {
            new NavItem(PageId.Home, "Home", "H", home),
            new NavItem(PageId.Sensors, "Sensors", "S", sensors),
        });
    }

    [Fact]
    public void StartsOnTheFirstPage()
    {
        var shell = Make(out var home, out _);

        Assert.Equal(PageId.Home, shell.Selected.Id);
        Assert.Same(home, shell.CurrentPage);
    }

    [Fact]
    public void Navigate_SwitchesPageAndRaisesPropertyChanged()
    {
        var shell = Make(out _, out var sensors);
        var raised = new List<string?>();
        shell.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        shell.Navigate(PageId.Sensors);

        Assert.Same(sensors, shell.CurrentPage);
        Assert.Contains(nameof(ShellViewModel.CurrentPage), raised);
    }

    [Fact]
    public void Navigate_ToAnUnregisteredPage_IsIgnored()
    {
        var shell = Make(out var home, out _);

        shell.Navigate(PageId.Settings);

        Assert.Same(home, shell.CurrentPage);
    }
}
```

Create `Torrid.Tests/HomeViewModelTests.cs` (uses the `FakeSensorSourceForTests` helper style from the deleted `MainWindowViewModelTests`; define a local fake that implements both `ISensorSource` and `IHardwareStatusSource` with `Publish(readings)` and `SetStatus(status)` methods that raise the events synchronously). Cover at least:
- readings for `cpu`, `gpu0-core`, `gpu0-hotspot`, `gpu0-vram` with a default config produce two cards: a "CPU" group and a "GPU 0" group each containing one card (core and VRAM are hidden by default);
- group titles are `CPU` and `GPU 0`, and the subtitle is the reading's `HardwareName`;
- a card's `Label` honours a custom label, its `Caption` is `SourceSensorName`, its `ValueText` is the value rounded to a whole number (`"68"` for 67.6), `Zone` follows the thresholds, and a `null` value gives `ValueText == "--"`, `IsAvailable == false`, `Fill == 0`;
- `Fill` equals `Math.Clamp(value / (YellowMax * 1.15), 0, 1)` for a sample value;
- switching a sensor on/off or renaming it through `ConfigManager.Update` (use `new ConfigManager(store, config, TimeSpan.Zero)` with an in-memory `IConfigStore`) changes the cards immediately, without a new reading being published, and an emptied group disappears;
- `IsEmpty` is true when nothing is shown and false otherwise;
- `StatusMessage` is `null` for `HardwareStatus.FullAccess`, mentions the product name for `Paused` ("Monitoring paused while Riot Vanguard is running…") and for `GpuOnly` ("CPU temperature is hidden while Riot Vanguard is running…"), and updates when the status source raises `StatusChanged`.

Write the tests first, run them (`dotnet test Torrid.Tests --filter "StartupOptionsTests|ShellViewModelTests|HomeViewModelTests"`) and confirm they FAIL to build, then implement.

- [ ] **Step 2: Implement the view-model layer, `StartupOptions`, `UiThread`, `ObservableObject`/`RelayCommand`**

Implement exactly the contracts listed under Interfaces. Notes: `HomeViewModel` rebuilds/updates its groups from `sensors.CurrentReadings` in an `Apply()` method that is called on construction, on `ReadingsUpdated` (via `UiThread.Post`), and on `ConfigManager.Changed`; it reuses existing card/group view-model instances by id so WPF bindings stay stable and only changes values; group order is CPU first, then GPUs by index; card order inside a group is `SensorKind` order. Delete `MainWindowViewModel`, `SensorRowViewModel` and `MainWindowViewModelTests` (superseded). Update `ColorPalette` and `ColorPaletteTests`.

- [ ] **Step 3: Run the tests**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter "StartupOptionsTests|ShellViewModelTests|HomeViewModelTests|ColorPaletteTests|SingleValueBitmapRendererTests"`
Expected: PASS.

- [ ] **Step 4: Commit the logic layer**

```bash
git add -A Torrid.App Torrid.Tests
git commit -m "Add shell/home view models, startup options and UI-thread helper"
```

- [ ] **Step 5: Build the theme, shell, Home view, AppHost and app startup**

Implement the design spec above (`Theme.xaml`, `MainWindow.xaml`/`.cs`, `Views/HomeView.xaml`, `Interop/DarkTitleBar.cs`, `AppHost.cs`, `App.xaml`/`App.xaml.cs`). `MainWindow.xaml.cs` becomes thin: no composition; it only applies the dark title bar, hides on close, and exposes `ExitApplication()`.

- [ ] **Step 6: Build, test, and look at it**

Run: `& "C:\Program Files\dotnet\dotnet.exe" build Torrid.sln` (0 errors) and `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.sln` (all pass).

Then screenshot with deterministic fake data and a throwaway config, and read the PNGs:
```powershell
$cfg = Join-Path $env:TEMP "torrid-shot-config.json"; Remove-Item $cfg -ErrorAction SilentlyContinue
powershell -NoProfile -File tools\Capture-Window.ps1 -Launch -Out "$env:TEMP\home-demo.png" -AppArgs @('--show','--demo','--config',$cfg)
powershell -NoProfile -File tools\Capture-Window.ps1 -Launch -Out "$env:TEMP\home-demo-paused.png" -AppArgs @('--show','--demo-paused','--config',$cfg)
```
Look at both images and iterate on the XAML until: the sidebar and cards match the spec, the numbers are large and clearly readable, the zone colours are correct, only CPU and the GPU hot spot cards appear by default, and the paused screenshot shows the banner with the unavailable `--` cards. Fix any clipping, overlap or contrast problem you can see. Confirm no `Torrid.App` process remains. Describe what you saw in your report and attach the PNG paths.

- [ ] **Step 7: Commit**

```bash
git add -A Torrid.App Torrid.Tests
git commit -m "Add dark card theme, sidebar shell, AppHost composition root and Home dashboard"
```

---

### Task 4: Sensors, Thresholds and Settings pages with live-applying edits (Torrid.App)

**Files:**
- Create: `Torrid.App/ViewModels/SensorsViewModel.cs`, `SensorGroupViewModel.cs`, `SensorListItemViewModel.cs`
- Create: `Torrid.App/ViewModels/ThresholdsViewModel.cs`, `ThresholdRowViewModel.cs`
- Create: `Torrid.App/ViewModels/SettingsPageViewModel.cs`
- Create: `Torrid.App/Views/SensorsView.xaml(.cs)`, `ThresholdsView.xaml(.cs)`, `SettingsView.xaml(.cs)`
- Modify: `Torrid.App/Themes/Theme.xaml` (input-control styles), `Torrid.App/MainWindow.xaml` (page templates), `Torrid.App/AppHost.cs` (register pages; apply poll-interval and refire changes live)
- Tests: `Torrid.Tests/SensorsViewModelTests.cs`, `Torrid.Tests/ThresholdsViewModelTests.cs`, `Torrid.Tests/SettingsPageViewModelTests.cs`

**Interfaces:**
- Consumes: Task 1 `ConfigManager`/`SensorPreferences`/`SensorDisplay`/`SensorIdentity`/`SensorDefaults`; Task 2 `AntiCheatBehavior`/`AntiCheatDetector.KnownProducts`/`IHardwareStatusSource`; Task 3 `ObservableObject`/`RelayCommand`/`UiThread`/`NavItem`/`PageId`/`AppHost`.
- Produces:
  - `SensorsViewModel(ISensorSource sensors, ConfigManager config)` — `ObservableCollection<SensorGroupViewModel> Groups`, `ICommand ResetToDefaultsCommand`.
  - `SensorGroupViewModel { string Title; ObservableCollection<SensorListItemViewModel> Items }`.
  - `SensorListItemViewModel` — `string Id`, `string DefaultLabel`, `string Label { get; set; }` (shows the resolved name; setting a blank value or the default name clears the custom label), `string Caption` (the real sensor/hardware name, e.g. "GPU Hot Spot · NVIDIA GeForce RTX 3090", or "Not detected right now" for a configured sensor with no live reading), `string ValueText`, `ColorZone Zone`, `bool IsDetected`, `bool ShowInWindow { get; set; }`, `bool ShowInTray { get; set; }`, `bool Notify { get; set; }` (a setter stores an explicit override only when it differs from the kind default, otherwise clears it), `ICommand ResetLabelCommand`.
  - `ThresholdsViewModel(ConfigManager config)` — `IReadOnlyList<ThresholdRowViewModel> Rows`; `ThresholdRowViewModel { SensorKind Kind; string Title; double BlueMax; double GreenMax; double YellowMax (setters apply immediately when the ordering is valid); string? Error; GridLength BlueWidth/GreenWidth/YellowWidth/RedWidth; ICommand ResetCommand }`.
  - `SettingsPageViewModel(ConfigManager config, IHardwareStatusSource hardware)` — `int PollIntervalMs` (clamped 500-60000), `int RefireMinutes` (clamped 0-1440), `bool StartMinimizedToTray`, `AntiCheatBehavior WhileAntiCheatActive`, `IReadOnlyList<AntiCheatChoice> AntiCheatChoices` (`AntiCheatChoice(AntiCheatBehavior Value, string Title, string Description)`), `ObservableCollection<string> ExtraProcesses`, `string NewProcessName`, `ICommand AddProcessCommand`, `ICommand RemoveProcessCommand`, `string HardwareStatusText`, `string KnownProductsText`.

**Behaviour spec**

*Live-apply + persistence*: every edit goes through `ConfigManager.Update(...)` (so Home, tray, alerts and the debounced save all react); no Save/Cancel buttons anywhere. `AppHost` subscribes to `ConfigManager.Changed` and (a) sets `SensorService.PollIntervalMs` when it is a `SensorService` and the configured value changed, (b) sets `NotificationService.RefireMinutes`.

*Sensors page*: title "Sensors", subtitle "Choose what appears in the window, in the tray and in alerts. Rename a sensor to tell identical cards apart." Rows grouped by device (CPU, GPU 0, GPU 1…). The row set is the union of the currently detected sensor ids and every key in `config.Sensors` that `SensorIdentity.TryParse` accepts (an unparsable key is ignored); ordering CPU first, then GPUs by index, then `SensorKind` order. The row set is rebuilt when the detected ids or the config change; per-tick value updates only touch the existing rows. Each row (a card-style strip): an editable name `TextBox` (the resolved label), under it the caption with the **actual** sensor/hardware names (use the live reading's `SourceSensorName`/`HardwareName`; for a sensor not detected right now show the default label's kind name and "Not detected right now"), then on the right the live value (zone-coloured, small) and three labelled toggle switches `Window`, `Tray`, `Alerts`. A "Reset to defaults" button (secondary style) at the top right clears all sensor preferences (after the click it is immediately effective; no confirmation dialog). Review Focus 3 applies.

*Thresholds page*: title "Thresholds", subtitle "Set the temperatures where colours change. Edits apply instantly." Four cards (CPU temperature, GPU core, GPU hot spot, GPU memory junction (VRAM)); each card has three number boxes labelled "Blue below", "Green below", "Yellow up to" (red above), a four-segment preview bar built from the proportions (blue/green/yellow/red zone colours; red segment width = the headroom `YellowMax * 0.25`), a small error line, and a "Reset" button restoring `AppConfig.DefaultThresholds()` for that kind. Validation: apply only when `0 <= BlueMax < GreenMax < YellowMax <= 150`; otherwise leave the config untouched and set `Error` to a short message ("Each limit must be higher than the one before."); clearing the error once valid. Review Focus 4 applies.

*Settings page*: sections as cards. **Monitoring**: poll interval (a `Slider` 500-60000 step 500 plus the numeric value shown, e.g. "2.0 s") and "Repeat alerts every N minutes (0 = alert once)" number box. **Startup**: "Start minimized to the tray" toggle. **Anti-cheat compatibility**: a short explanation ("Some games' anti-cheat (Riot Vanguard, Easy Anti-Cheat, BattlEye, FACEIT…) object to monitoring tools that use a hardware driver. Torrid can step aside automatically."), a three-option choice list bound to `AntiCheatChoices` (titles: "Pause monitoring (recommended)", "GPU only — no driver access", "Keep monitoring"; with one-line descriptions), a live status line (`HardwareStatusText`: "Monitoring normally." / "Paused — Riot Vanguard is running (vgc)." / "GPU only — Riot Vanguard is running (vgc)."), the list of `ExtraProcesses` ("Also treat these programs as anti-cheat") with a text box + "Add" button and a remove button per entry, and the "Detects: …" line from `KnownProductsText`. Adding trims and de-duplicates (case-insensitive), strips a trailing `.exe`, ignores blanks. Updating the extra-process list REPLACES `config.ExtraProtectedProcesses` with a new `List<string>` (never mutate it in place — the sensor thread reads it).

*Controls*: add to `Theme.xaml`: a toggle-switch `CheckBox` style (46x24 pill, accent track when on, white knob), dark `TextBox` and number-box look (bg `#0E2250`, border `#2B4A8F`, focus border accent, 6 px radius), a dark `Slider`, primary/secondary `Button` styles, a radio-card style for the anti-cheat choices. Make sure keyboard focus is visible and nothing becomes unreadable (white on white) in any state.

- [ ] **Step 1: Failing tests**

Write view-model tests first (all WPF-free; `GridLength` is a value type from `System.Windows` — the test project already targets `net8.0-windows` so it is available). Use an in-memory `IConfigStore` and `new ConfigManager(store, new AppConfig(), TimeSpan.Zero)`.

`SensorsViewModelTests` must cover: one group per device with items in the specified order; an undetected-but-configured id (`config.Sensors["gpu1-hotspot"] = new SensorPreference { Label = "EVGA Hot" }` with no gpu1 readings) still yields a row whose caption says "Not detected right now" and whose label is "EVGA Hot"; an unparsable key (`"garbage"`) yields no row and no exception; setting `ShowInWindow`/`ShowInTray`/`Notify` to the kind default removes the override while a non-default value stores it (assert on `config.Sensors`); setting `Label` to a custom string stores it, setting it back to the default name or to whitespace clears it; the caption for a detected sensor contains its `SourceSensorName` and `HardwareName`; `ResetToDefaultsCommand` empties `config.Sensors`; the row set updates when a new reading id appears (publish a second GPU) and per-tick publishes do not recreate existing row instances (same object identity).

`ThresholdsViewModelTests` must cover: one row per `SensorKind` seeded from the config; a valid edit updates `config.Thresholds[kind]` and raises no error; each invalid ordering (`Blue >= Green`, `Green >= Yellow`, negative, above 150) leaves the config untouched and sets `Error`; fixing it clears `Error`; `ResetCommand` restores the default band; the preview widths are proportional to the band values.

`SettingsPageViewModelTests` must cover: poll interval clamps (0 -> 500, 120000 -> 60000) and writes the clamped value; refire clamps to 0..1440; `StartMinimizedToTray` and `WhileAntiCheatActive` write through; adding an extra process trims, strips `.exe`, de-duplicates case-insensitively and ignores blanks, and replaces the config list instance (assert `!ReferenceEquals(before, after)`); removing works; `HardwareStatusText` reflects `FullAccess`, `Paused` and `GpuOnly` statuses and updates on `StatusChanged`; `KnownProductsText` contains "Riot Vanguard".

Run them and confirm they fail to build, then implement the view models.

- [ ] **Step 2: Implement the view models**

Per the Interfaces and Behaviour spec above.

- [ ] **Step 3: Run the tests**

Run: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter "SensorsViewModelTests|ThresholdsViewModelTests|SettingsPageViewModelTests"`
Expected: PASS.

- [ ] **Step 4: Commit the logic**

```bash
git add -A Torrid.App Torrid.Tests
git commit -m "Add Sensors, Thresholds and Settings page view models with live-applying edits"
```

- [ ] **Step 5: Build the three views, control styles, page registration and live application**

Implement the views per the Behaviour spec, register the three pages in `AppHost` (nav order Home, Sensors, Thresholds, Settings), add the `DataTemplate`s in `MainWindow.xaml`, and add the `ConfigManager.Changed` handler for poll interval and refire.

- [ ] **Step 6: Build, test, and look at every page**

Run build + full test suite (0 errors, all pass). Then screenshot each page. To land on a given page, add (in this task) an undocumented-but-harmless `--page <Home|Sensors|Thresholds|Settings>` option to `StartupOptions` (parsed case-insensitively, invalid values ignored, covered by a test) which `AppHost` uses to select the initial page:
```powershell
$cfg = Join-Path $env:TEMP "torrid-shot-config.json"; Remove-Item $cfg -ErrorAction SilentlyContinue
foreach ($page in 'Sensors','Thresholds','Settings') {
  powershell -NoProfile -File tools\Capture-Window.ps1 -Launch -Out "$env:TEMP\page-$page.png" -AppArgs @('--show','--demo','--page',$page,'--config',$cfg)
}
```
Read each PNG, check against the spec (readable contrast everywhere, toggles obviously on/off, nothing clipped at the 1000x680 default or when the window is narrowed to its 780 px minimum — screenshot once at minimum size too by passing a smaller size through whatever means you choose, e.g. a temporary `--size 780x520` option you do NOT need to keep), and iterate. Also run once with `--demo-paused` on the Settings page to check the anti-cheat status line. Verify persistence end to end WITHOUT clicking: run the app with a throwaway `--config` file, then write a tiny script that uses `JsonConfigStore` semantics or simply inspect the file after a `ConfigManager.Flush()` path — or launch with the config pre-seeded (e.g. `{"PollIntervalMs":4000,"Sensors":{"gpu0-vram":{"ShowInWindow":true,"Label":"VRAM hot"}}}`) and confirm in a screenshot that the seeded values appear (Sensors page shows "VRAM hot" with Window on, Settings shows 4.0 s). Confirm no `Torrid.App` process remains. Report what you saw and attach PNG paths.

- [ ] **Step 7: Commit**

```bash
git add -A Torrid.App Torrid.Tests
git commit -m "Add Sensors, Thresholds and Settings pages with instant-apply edits"
```

---

### Task 5: Tray refinements, alerts on the UI thread, working toasts (Start Menu shortcut), clean exit (Torrid.App)

**Why toasts:** proven in `tools/ToastProbe/FINDINGS.md` — Windows drops an unpackaged app's toasts unless a Start Menu shortcut carrying the app's AppUserModelID exists (with it: accepted; deleted: dropped). `tools/ToastProbe/ShortcutHelper.cs` is the working reference interop.

**Files:**
- Create: `Torrid.App/AppIdentity.cs`, `Torrid.App/Notifications/StartMenuShortcut.cs`
- Modify: `Torrid.App/App.xaml.cs`, `Torrid.App/AppHost.cs`, `Torrid.App/StartupOptions.cs`
- Modify: `Torrid.App/Tray/TrayIconGroupManager.cs`, `Torrid.App/Tray/SensorTrayIcon.cs`
- Modify: `Torrid.App/Views/SettingsView.xaml`, `Torrid.App/ViewModels/SettingsPageViewModel.cs`
- Create: `tools/Verify-Toast.ps1`
- Tests: `Torrid.Tests/StartupOptionsTests.cs` (extend), `Torrid.Tests/SettingsPageViewModelTests.cs` (extend), `Torrid.Tests/TooltipTextBuilderTests.cs` (extend if the builder changes)

**Interfaces:**
- Consumes: everything from Tasks 1-4.
- Produces: `AppIdentity.Aumid` (`"Torrid.App"` in Release, `"Torrid.App.Dev"` in Debug via `#if DEBUG`), `AppIdentity.ShortcutName` (`"Torrid.lnk"` / `"Torrid (Dev).lnk"`), `StartMenuShortcut.EnsureForToasts()` (idempotent: creates `%APPDATA%\Microsoft\Windows\Start Menu\Programs\<ShortcutName>` pointing at `Environment.ProcessPath` with `System.AppUserModel.ID = AppIdentity.Aumid`; leaves an existing shortcut with the same target and id alone, recreates it when the target or id differ, never throws — failures go to `Debug.WriteLine`); `StartupOptions.TestToast` (`--test-toast`); `AppHost.SendTestNotification()`; `TrayIconGroupManager(ISensorSource sensors, ConfigManager config, IHardwareStatusSource hardware)` with events `OpenRequested`, `SettingsRequested`, `ExitRequested`, `TrayIconClicked`; `SettingsPageViewModel.SendTestNotificationCommand` (constructor gains an `Action sendTestNotification` parameter).

**Behaviour spec**
- *Tray*: shows an icon per sensor where `SensorDisplay.IsShownInTray` is true (live: `ConfigManager.Changed` triggers a redraw, so toggling Tray on the Sensors page adds/removes icons immediately); label via `ResolveLabel`; the existing red-blink gating and GDI handle hygiene stay exactly as they are. Context menu items, in order: "Open Torrid" (`OpenRequested` → `ShowMainWindow()`), "Settings" (`SettingsRequested` → `ShowMainWindow(PageId.Settings)`), separator, "Exit" (`ExitRequested`). Left click still opens the window (Home). While `hardware.Status.Level` is `Paused` every tooltip reads `"{label}: paused ({product} is running)"`, and unavailable values render grey (done in Task 3). Subscribe to `IHardwareStatusSource.StatusChanged` (marshal with `UiThread.Post`) and redraw.
- *Alerts on the UI thread*: `AppHost` evaluates notifications inside `UiThread.Post` (never on the sensor timer thread), `NotificationService.RefireMinutes` follows config changes, labels are resolved before `Evaluate`, sensors with `NotificationsEnabled == false` are skipped, null readings never alert (existing behaviour).
- *Toast delivery*: `App.OnStartup` calls `SetCurrentProcessExplicitAppUserModelID(AppIdentity.Aumid)` (replace the hard-coded `"Torrid.App"`) and then `StartMenuShortcut.EnsureForToasts()` before anything can show a toast. `WindowsToastNotifier` keeps its try/catch. Note Windows needs up to roughly a minute to pick up a brand-new shortcut, so the very first toast right after the very first run can still be dropped — that is expected; mention it in the Settings description text under the test button ("The first test after installing can take up to a minute to appear.").
- *Test notification*: a "Send test notification" button in Settings (secondary style, under a "Notifications" section card) calls `AppHost.SendTestNotification()`, which shows a toast via the same `WindowsToastNotifier` with title "Torrid test notification" and a line "Alerts are working."; `--test-toast` makes the app, once started and the shortcut ensured, wait 15 s (so a freshly created shortcut can be indexed), send that toast, wait 4 s and exit the process (the console-less equivalent of the probe), used by `tools/Verify-Toast.ps1`.
- *Verify script*: `tools/Verify-Toast.ps1` records `HKCU:\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\Torrid.App.Dev` `LastNotificationAddedTime`, runs the Debug exe with `--test-toast --config <temp>` (waiting for it to exit, 60 s timeout), re-reads the value, prints `PASS: Windows accepted the notification` if it changed or `FAIL: ...` otherwise, and exits non-zero on FAIL.
- *Exit*: tray "Exit" → `AppHost.Exit()` → `ConfigManager.Flush()` first, then `MainWindow.ExitApplication()`; `App.OnExit` also disposes the host (flushing again is harmless). X on the window still only hides.

- [ ] **Step 1: Failing tests for the pure parts**

Extend `StartupOptionsTests` (`--test-toast` sets `TestToast`; `--page` parsing from Task 4 is already covered) and `SettingsPageViewModelTests` (constructing with a counting `Action`, executing `SendTestNotificationCommand` calls it once). If you change `TooltipTextBuilder`, extend its tests. Run the filter, confirm it fails, implement, confirm it passes:
`& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests --filter "StartupOptionsTests|SettingsPageViewModelTests|TooltipTextBuilderTests"`

- [ ] **Step 2: Implement identity, shortcut helper, startup flags, tray changes, alert threading, Settings button, verify script**

Port `tools/ToastProbe/ShortcutHelper.cs` into `Torrid.App/Notifications/StartMenuShortcut.cs` (same COM interop; add the read-back used for idempotence via `IShellLinkW.GetPath` / `IPropertyStore.GetValue` on a freshly loaded `IPersistFile`, or — simpler and acceptable — store nothing and recreate only when the file is missing or older than the exe; document whichever you choose in a short comment because the reason is non-obvious).

- [ ] **Step 3: Build, test**

Run: `& "C:\Program Files\dotnet\dotnet.exe" build Torrid.sln` and `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.sln`
Expected: 0 errors, all tests pass.

- [ ] **Step 4: Prove toasts now work, automatically**

Run: `powershell -NoProfile -File tools\Verify-Toast.ps1`
Expected: `PASS: Windows accepted the notification`. If the first run FAILs because the shortcut was only just created, run it again once (the shortcut now exists and has had time to be indexed) and report both results honestly. Confirm no `Torrid.App` process remains afterwards.

- [ ] **Step 5: Look at the tray-related UI**

Screenshot the Sensors and Settings pages (`--show --demo --page ... --config <temp>`) and confirm the new "Send test notification" section looks right. The tray itself cannot be screenshotted by the capture tool; instead run the app with `--demo` and a throwaway config for ~10 s and confirm via `Get-Process` that it stays up, and describe in your report which tray behaviours you verified by code reading only (menu items, tooltip text, icon add/remove on config change).

- [ ] **Step 6: Commit**

```bash
git add -A Torrid.App Torrid.Tests tools
git commit -m "Tray menu and live tray icons, alerts on the UI thread, Start Menu shortcut so toasts are delivered"
```

---

### Task 6: Documentation, publish, and final verification

**Files:**
- Modify: `README.md`
- Modify: `PROJECT_PLAN.md` (status note only)
- Create: `docs/anti-cheat.md`

- [ ] **Step 1: Write `docs/anti-cheat.md`** — what Torrid does and why, in plain language: which hardware paths exist (CPU temperature needs the PawnIO kernel driver and administrator rights; GPU temperatures use NVIDIA's user-mode APIs), what the three behaviours do, how detection works (process names, list of products, user-extendable list), that PawnIO itself is a separate system-wide driver Torrid cannot unload (if an anti-cheat objects to PawnIO itself it must be uninstalled, after which Torrid still shows GPU temperatures), what to try if a specific game still complains (switch to "GPU only", add the game's process to the list, or exit Torrid from the tray), and an honest statement that Torrid is unsigned and cannot be tested against every anti-cheat here.
- [ ] **Step 2: Update `README.md`** — current status, features (sidebar UI, per-sensor tray icons, alerts, anti-cheat safety), the dev loop (non-elevated Debug builds, `tools/Capture-Window.ps1`, `--show`/`--demo`/`--demo-paused`/`--config`/`--page`/`--test-toast` flags, `tools/Verify-Toast.ps1`, `tools/SensorProbe`), publishing (`dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` → `publish\`), and the Start Menu shortcut side effect. Add a one-line status note to `PROJECT_PLAN.md` pointing at the README.
- [ ] **Step 3: Full verification** — `dotnet build Torrid.sln` (Debug and `-c Release`), `dotnet test Torrid.sln`, publish to `publish\`, confirm the published exe embeds `requireAdministrator` (`Select-String -Pattern requireAdministrator -Encoding utf8` against the exe), screenshot all four pages one last time with `--demo`, and confirm no stray `Torrid.App` processes or leftover temp files (`vgc.exe` test copies, probe shortcuts) remain.
- [ ] **Step 4: Commit**

```bash
git add README.md PROJECT_PLAN.md docs
git commit -m "Document anti-cheat behaviour, dev loop and publishing"
```
