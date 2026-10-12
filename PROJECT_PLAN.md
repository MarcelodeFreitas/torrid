# Torrid — Project Plan

> **Status:** this is the original design document. The app is built (phases 1-4: sensor service, tray, notifications, sidebar UI, anti-cheat safety); see [README.md](README.md) for what is actually implemented and how to build, run and publish it, and [docs/anti-cheat.md](docs/anti-cheat.md) for anti-cheat behaviour.

A Windows desktop app that monitors CPU and GPU (core/hotspot/VRAM) temperatures in real time, shows them as color-coded numbers in a tray icon and an optional window, and fires a native Windows notification when any sensor crosses into the critical zone.

This document is self-contained — everything needed to start implementation is here. Paste it into a new conversation to kick off the build.

---

## 1. Background / why this exists

The user bought two used RTX 3090s (an MSI GAMING X TRIO and an EVGA FTW3 ULTRA GAMING) for a local-LLM inference rig (AMD Ryzen 9 5950X, MSI B550-A PRO, 64GB RAM). During validation testing (OCCT, Superposition, FurMark) it became clear that:
- GPU Hot Spot and Memory Junction temps (not just core temp) are the metrics that actually matter for catching thermal problems
- One card (EVGA) showed an uneven VRAM chip temperature spread and a fast temperature climb under heavy load — a specific, watchable failure pattern
- Sustained real-world LLM inference workloads will run much longer than any synthetic test, so ongoing live monitoring with alerts is valuable, not just one-off stress tests

The user wants a lightweight, always-on tool for this, rather than relying on manually checking HWiNFO.

## 2. Product name

**Torrid.** Checked against GitHub for collisions — no existing project uses this name (candidates "Redline," "Caldera," "Watchtower," "Ember," "TempWatch," "SensorGuard," "ThermoPulse" were all found to be taken by similar-concept projects and rejected). Confirm again on github.com directly before publishing, since this was a web-search-based check, not exhaustive.

## 3. Full requirements

**Must monitor:**
- CPU temperature (package/core)
- GPU Core temperature — for **both** GPUs simultaneously, labeled distinctly (system has 2x RTX 3090: MSI GAMING X TRIO and EVGA FTW3 ULTRA GAMING)
- GPU Hot Spot temperature — per GPU
- GPU Memory Junction (VRAM) temperature — per GPU

**Display:**
- A **system tray icon** showing a compact, custom-rendered readout (not plain OS-drawn text) — e.g. two small numbers like `45 52`, similar to what FanControl's tray display does, but enhanced:
  - Numbers/digits colored per threshold: **blue → green → yellow → red**
  - Consider a colored background chip or fill-bar behind the number for faster at-a-glance reading than color alone
  - Icon should visually change (e.g. pulse/blink) while any monitored sensor is in the red zone, as an ongoing ambient signal — separate from the one-time toast notification
  - Rich multi-line tooltip on hover showing full breakdown (CPU, both GPUs' Core/Hotspot/VRAM) since the tray icon itself can't fit everything
- A **default main window** — opened by clicking the tray icon — showing the same data in full, larger text, one row per sensor, each colored the same way. Example layout:
  ```
  CPU            54°C
  GPU0 (MSI)  Core   67°C
  GPU0 (MSI)  Hot    74°C
  GPU0 (MSI)  VRAM   70°C
  GPU1 (EVGA) Core   61°C
  GPU1 (EVGA) Hot    78°C
  GPU1 (EVGA) VRAM   72°C
  ```
- Everything must be **customizable**: thresholds per sensor, poll interval, which sensors are visible, tray-vs-window behavior — via an in-app Settings panel, not just hand-edited config.

**Alerting:**
- When any monitored value crosses into **red**, fire a real **Windows toast notification** (not just an in-app color change) naming the sensor and its value.
- Notifications need **debounce/hysteresis** — do not fire on every poll tick while sustained in red. Fire once on crossing into red; don't re-fire until it drops back below red and re-enters (or optionally, re-fire every N minutes if sustained — make this configurable).

**Persistence:**
- All settings (thresholds, poll interval, visible sensors, window preferences) persist across restarts via a JSON config file.

## 4. Default threshold values (starting point — must be user-editable)

| Sensor | Blue (cool) | Green (normal) | Yellow (caution) | Red (critical) |
|---|---|---|---|---|
| CPU temp | < 40°C | 40–70°C | 70–85°C | > 85°C |
| GPU Core temp | < 40°C | 40–75°C | 75–85°C | > 85°C |
| GPU Hot Spot temp | < 50°C | 50–85°C | 85–95°C | > 95°C |
| GPU Memory Junction (VRAM) temp | < 50°C | 50–85°C | 85–95°C | > 95°C |

These were calibrated against real data: the MSI card plateaued at 97°C hotspot under sustained synthetic load (borderline yellow/red boundary), and the EVGA card spiked to 96°C VRAM in 60 seconds under FurMark (should trigger red). Ambient during that testing was ~30°C, warmer than typical — thresholds may need slight adjustment once real usage patterns are established.

## 5. Tech stack (decided)

**C# / .NET, WPF UI, LibreHardwareMonitorLib for sensor access.**

Rationale:
- LibreHardwareMonitor is itself a .NET library — using it from C# avoids any interop/bridging layer
- FanControl (a mature, widely-used open source tool doing similar sensor work) is built exactly this way — confirmed via research: C#/.NET with WPF UI, LibreHardwareMonitor as the sensor/driver backend. This validates the approach.
- WPF's data binding makes "value changes color live" straightforward via an `IValueConverter`
- Windows toast notifications are well-supported for WPF via `CommunityToolkit.WinUI.Notifications` (or `Microsoft.Toolkit.Uwp.Notifications` depending on current package naming — verify current recommended package at implementation time, this ecosystem has had naming churn)
- Decision made explicitly: **build new rather than contribute to FanControl** — a notification/alerting feature is tangential to FanControl's fan-curve focus and would be subject to a maintainer's review/scope judgment and timeline, whereas this needs to be usable on the user's own two GPUs promptly, with their own specific thresholds and layout.

**Known technical requirements/gotchas to design around:**
- **Must run elevated (Administrator).** LibreHardwareMonitor needs this for GPU Hot Spot/Memory Junction access (via NVAPI) and CPU package temp (via MSR). Embed a manifest requesting `requireAdministrator` so the user isn't manually right-click-running-as-admin every launch.
- **Sensor names are not guaranteed and must be verified at runtime**, not hardcoded blind. LibreHardwareMonitor's exact sensor label strings for GPU Hot Spot / Memory Junction can vary; the first implementation task must enumerate and log all available sensors for the actual hardware (both RTX 3090s + the 5950X) before building anything on top.
- **Two GPUs must be distinguished**, not just aggregated — loop over all `IHardware` where `HardwareType == Gpu` and label each (likely by GPU name string, since "MSI" vs "EVGA" branding won't appear in the sensor API — matching may need to rely on index/order or a name substring check, confirm what's actually available at enumeration time).
- **Tray icon must be custom-rendered as a bitmap** (e.g. via `System.Drawing`/GDI+), not relying on OS-drawn taskbar text — this is what gives full control over color, background chips, fill bars, and blink state. Confirm rendering looks correct at different Windows DPI scaling settings.
- **Background polling must not block the UI thread** — timer-driven poll on a background thread/task, marshal results to the UI thread via `Dispatcher.Invoke` for WPF binding updates and tray icon redraws.

## 6. Architecture / components

1. **Sensor Service** — wraps `Computer` from LibreHardwareMonitorLib (CPU + GPU + Memory enabled), polls on a timer, exposes current values for: CPU temp, and per-GPU Core/Hotspot/Memory-Junction temps, as an observable data model.
2. **Config Model + Persistence** — JSON-backed settings object: per-sensor threshold bands, poll interval, visible sensors, window/tray preferences, notification re-fire interval. Load at startup, save on change.
3. **Threshold/Color Logic** — pure function `GetColor(value, thresholds) -> Blue/Green/Yellow/Red`, used both by WPF binding (via `IValueConverter`) and by the tray icon renderer.
4. **Tray Icon Renderer** — draws a small bitmap (numbers, color, optional background chip/fill bar, blink state when any sensor is red) and sets it as the `NotifyIcon` icon; builds the multi-line hover tooltip text from all current sensor values.
5. **Main Window (WPF)** — bound list of sensor rows (label + colored value), opened from tray icon click; reflects the same live data.
6. **Settings Window (WPF)** — UI to edit thresholds/poll interval/visible sensors/notification behavior; writes back to Config Model.
7. **Notification Service** — watches for a sensor's color transitioning into Red, applies debounce/hysteresis (fire once on entry, track per-sensor "already notified" state, reset when it drops below red), fires a Windows toast via the chosen notifications package.
8. **App shell / entry point** — wires the above together, hosts the `NotifyIcon`, handles startup (admin manifest), and app lifecycle (minimize-to-tray instead of close).

## 7. Suggested project structure

```
Torrid/
  Torrid.sln
  Torrid.App/                  (WPF project, entry point)
    App.xaml / App.xaml.cs
    app.manifest                (requireAdministrator)
    MainWindow.xaml/.cs
    SettingsWindow.xaml/.cs
    TrayIconManager.cs
  Torrid.Core/                 (class library, no UI dependency)
    SensorService.cs
    SensorReading.cs            (model: name, value, kind)
    ThresholdConfig.cs
    ColorThresholdEvaluator.cs
    NotificationService.cs
    ConfigStore.cs               (JSON load/save)
  Torrid.Tests/                (optional, unit tests for threshold logic / config parsing)
  config.default.json
  README.md
```

## 8. Build plan — phased, with subagent dispatch

**Phase 0 (sequential, must happen first, do not parallelize):**
Sensor probe. Build a minimal console app referencing `LibreHardwareMonitorLib`, run it **elevated**, enumerate `Computer.Hardware` for CPU + both GPUs + Memory, print every available sensor name/type/value every 2 seconds. Confirm:
- Exact sensor label strings for CPU package temp, and for each GPU's Core/Hot Spot/Memory Junction temps
- That both GPUs enumerate distinctly and can be told apart
- That running non-elevated actually fails/degrades as expected (documents the admin requirement concretely)

This phase blocks everything else — the UI, tray, and notification layers all need to bind to real, confirmed sensor names, not assumed ones. **Do this yourself first before dispatching any subagents.**

**Phase 1 (parallelizable — dispatch these as independent subagents once Phase 0's sensor names are confirmed):**
These four tasks touch different files/subsystems and have no shared state between them, so they're good candidates for parallel subagents (see `superpowers:dispatching-parallel-agents` if using that skill):

- **Subagent A — Sensor Service + Config Model** (`Torrid.Core`): build `SensorService` (wraps confirmed LHM access from Phase 0), `SensorReading` model, `ThresholdConfig`, `ColorThresholdEvaluator`, `ConfigStore` (JSON load/save with the default thresholds from section 4). Deliverable: a working, tested Core library with no UI dependency.
- **Subagent B — Main Window UI** (`Torrid.App`): WPF `MainWindow` with a bound list of sensor rows, `IValueConverter` for color, using a *stub/fake* data source until Subagent A's real Sensor Service is ready (define the interface/contract between them upfront so both can build against it independently).
- **Subagent C — Tray Icon Renderer** (`Torrid.App`): `TrayIconManager` that renders the custom bitmap (colored digits, optional fill bar/chip, blink-on-red), builds the hover tooltip, against the same stub data contract as Subagent B.
- **Subagent D — Notification Service** (`Torrid.Core` or `Torrid.App`): debounce/hysteresis logic for "just entered red," Windows toast integration (confirm current recommended NuGet package for WPF toast notifications at implementation time — this space has had package-naming churn), unit-testable independent of real sensor data.

**Phase 2 (sequential integration, after Phase 1 subagents complete):**
Wire real `SensorService` (Subagent A) into MainWindow (B), TrayIconManager (C), and NotificationService (D). Replace stub data sources with the real one. Test end-to-end against the actual hardware (both RTX 3090s + 5950X), elevated.

**Phase 3 (sequential, after Phase 2 works):**
- Settings Window: UI to edit thresholds/poll interval/visible sensors, wired to `ConfigStore`
- Verify persistence across app restarts
- Packaging: `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`, embed the admin manifest so it self-elevates on launch without manual "Run as Administrator"

## 9. Acceptance criteria / definition of done

- App runs elevated automatically (manifest), no manual admin prompt workaround needed
- Both GPUs' Core/Hotspot/VRAM temps display correctly and distinctly, alongside CPU temp
- Tray icon shows live colored numbers, updates on the configured poll interval, tooltip shows full breakdown
- Crossing into red fires exactly one toast notification per crossing event (verified: does not spam every poll tick while sustained in red)
- Opening the main window from the tray shows the same live data, larger/full layout
- Settings window can change thresholds/poll interval/visible sensors, and changes persist across app restart
- Tested under real sustained load (e.g. re-running the OCCT/FurMark stress tests from the GPU validation work while Torrid runs) to confirm it correctly flags the same hotspot/VRAM spikes already observed manually

## 10. Known open questions to resolve during implementation

- Exact current NuGet package name for WPF toast notifications (verify at build time, don't assume)
- Whether LibreHardwareMonitor's GPU naming lets you cleanly label "MSI" vs "EVGA" automatically, or whether that needs to be a user-assigned label in settings
- Exact visual design of the tray icon at small sizes / different Windows DPI scaling — likely needs some manual visual iteration once Subagent C's renderer is running
