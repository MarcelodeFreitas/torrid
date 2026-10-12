# Torrid Phase 6 (Per-sensor thresholds + advanced list view) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Thresholds become **per sensor** and editable. Every sensor (built-in or added, hidden or shown, readable or not) gets its own set of limits; when a new sensor appears, a card for it appears on the Thresholds page. The existing per-*kind* limits stay only as the **defaults** a sensor follows until the user customizes it. The page gets two views: the default **Cards** view and an **Advanced list** view (compact table, search, many sensors).

**User requests (verbatim):** "the thresholds should be per sensor and editable, when a new sensor shows up a new threshold shows up, currently it looks like a threshold may be for multiple sensors and hidden is that correct" → confirmed: today limits are per kind and shared. Then: "i would only add an advanced mode with a list instead of squares since its a lot of sensors so that if anyone wants further customizability it is available".

**Decisions already agreed with the user (rulings, binding):**
1. A sensor's effective band = its own band if it has one, otherwise its kind's default band (`AppConfig.Thresholds[kind]`, else `AppConfig.DefaultThresholds()[kind]`). Editing a sensor's limits creates its own band (shown as **Custom**); **Reset** removes it (back to **Default**). No data migration needed: existing kind bands remain as the defaults, so nothing the user tuned is lost.
2. The Thresholds page lists every sensor from the same row set as the Sensors page (detected + `config.Sensors` keys accepted by `SensorDisplay.Identity` + `AddedSensors`), grouped by device like the Sensors page; sensors that are not shown anywhere (Window, Tray and Alerts all off) go into a collapsed "Not shown" section.
3. A compact "Defaults by sensor type" section (the existing 10 kind rows, same editing behaviour as today) stays at the bottom of the page in both views — these are what unedited sensors follow.
4. Each Sensors-page row gets a small **Limits** link that opens the Thresholds page focused on that sensor's card/row.
5. The alert fires when a value goes above the sensor's "Yellow up to" (unchanged semantics); colour zones, tray and Home all use the sensor's effective band.
6. Advanced list view = a view option on the Thresholds page (persisted as `AppConfig.ThresholdsListView`, default false).

## Global Constraints

- `& "C:\Program Files\dotnet\dotnet.exe"` for dotnet. **Commit trailer, verbatim, on every commit:** `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. Do not push.
- Debug builds are non-elevated; leave no `Torrid.App` process running; delete `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Torrid (Dev).lnk` afterwards if a run re-created it; never launch `publish\Torrid.App.exe`; do NOT run real-hardware scans — use `--demo` for screenshots (`tools\Capture-Window.ps1` run IN-PROCESS from PowerShell, always with a throwaway `--config` in `%TEMP%`; view PNGs with the Read tool).
- `ConfigManager.Config` mutated ONLY via `ConfigManager.Update`; dictionaries read by the poll thread are replaced wholesale (the poll thread only reads `WhileAntiCheatActive`, `ExtraProtectedProcesses`, `AddedSensors`; thresholds are read on the UI thread only — keep it that way). `UiThread.Post` for sensor-thread events.
- Band validity everywhere: `0 <= BlueMax < GreenMax < YellowMax <= 150`. Invalid per-sensor bands in a hand-edited config are dropped on load (sensor falls back to its default); invalid edits in the UI never reach the config and show the same short error text as today ("Each limit must be higher than the one before.").
- Everything from Phases 1-5 keeps working; all existing tests keep passing (adjust only where behaviour intentionally changes, and say so).
- Visual bar: polished like the other pages (dark theme, contrast, visible keyboard focus), usable at 1000x680 and at the 780-wide minimum.

## Review Focus

1. The same sensor must get the same band everywhere: card colour/fill (Home), Sensors row value colour, tray icon colour/blink, alert evaluation. No consumer may still index `Thresholds[kind]` directly for a sensor.
2. Editing one sensor never changes another sensor's colours; resetting a sensor returns it to following the kind default (including when the default is later edited); editing the kind default changes only sensors without their own band.
3. A sensor that is not currently readable (not detected / paused) still has an editable card/row and a null live value (shows `--`), never a crash.
4. The list view with 50+ sensors stays responsive (virtualization or at least no per-tick rebuild of rows; per-tick updates touch only values) and search filtering is instant; collapsed sections don't lose edits in progress.
5. Config round-trip: `SensorPreference.Thresholds` serializes/deserializes; empty preferences are still removed (`IsEmpty` must account for the new field); old configs load unchanged.

---

### Task 1: Per-sensor bands in Torrid.Core + consumers

**Files:** modify `Torrid.Core/Config/SensorPreference.cs`, `SensorPreferences.cs` (if needed), `AppConfig.cs`, `JsonConfigStore.cs`, `Torrid.Core/Notifications/NotificationService.cs`; create `Torrid.Core/Config/ThresholdResolver.cs`; adapt consumers in `Torrid.App/AppHost.cs` (line ~221), `Torrid.App/Tray/TrayIconGroupManager.cs` (~99, ~118), `Torrid.App/ViewModels/HomeViewModel.cs` (~149), `Torrid.App/ViewModels/SensorsViewModel.cs` (~132); tests: new `Torrid.Tests/ThresholdResolverTests.cs`, extend `SensorPreferencesTests.cs`, `JsonConfigStoreTests.cs`, `NotificationServiceTests.cs`, `HomeViewModelTests.cs`, `SensorsViewModelTests.cs`.

**Interfaces (Task 2 relies on these exact names):**
- `SensorPreference.Thresholds : ThresholdBand?` (public settable; serialised; `IsEmpty` is false when it is non-null).
- `AppConfig.ThresholdsListView : bool` (default false).
- `static class ThresholdResolver`:
  - `ThresholdBand For(AppConfig config, string sensorId, SensorKind kind)` — own valid band, else `config.Thresholds[kind]`, else `AppConfig.DefaultThresholds()[kind]`. Never null, never throws (null config dictionaries tolerated). Returns the same instance as stored (no copying) so callers may compare references only if they want to.
  - `ThresholdBand For(AppConfig config, SensorReading reading)` and `ThresholdBand For(AppConfig config, SensorIdentity sensor)` convenience overloads.
  - `bool HasCustom(AppConfig config, string sensorId)` — true when the sensor has its own valid band.
  - `static bool IsValid(ThresholdBand? band)` — `0 <= Blue < Green < Yellow <= 150` (put the single validity rule here and have the existing `ThresholdRowViewModel` reuse it instead of its own copy if one exists).
  - `static void SetCustom(AppConfig config, string sensorId, ThresholdBand band)` / `static void ClearCustom(AppConfig config, string sensorId)` — go through `SensorPreferences.Update` so empty preferences are removed; `SetCustom` ignores an invalid band.
- `NotificationService.Evaluate(IReadOnlyList<SensorReading> readings, Func<SensorReading, ThresholdBand> bandFor)` as the primary overload; keep the existing `(readings, IReadOnlyDictionary<SensorKind, ThresholdBand>)` overload delegating to it (so existing tests keep working); `AppHost` passes `r => ThresholdResolver.For(config, r)`.
- `JsonConfigStore.Normalize`: drop per-sensor bands that fail `IsValid` (set `Thresholds = null`, then remove the preference if now empty); also existing normalisations unchanged.

**Tests required (write first, see them fail, then implement):** resolver precedence (own > kind > default; null dictionaries; invalid own band ignored), `HasCustom`, `SetCustom`/`ClearCustom` (entry removed when it becomes empty; other fields of the preference untouched; invalid band rejected), `IsEmpty` with only a band, JSON round-trip of a sensor with a band and an old config without the field, Normalize drops an invalid band but keeps label/other fields, `NotificationService` uses the per-sensor band (two sensors of the same kind with different bands: only the one above its own `YellowMax` alerts; fallback overload unchanged), HomeViewModel card zone/fill follow the sensor's own band (and the kind default for others), SensorsViewModel row value zone likewise. The tray's usage is adapted by reading (no hardware); add a unit test only if the logic can be isolated without `NotifyIcon` (e.g. extract the band lookup into a testable static).

- [ ] Steps: failing tests → implement → full `dotnet build Torrid.sln` + `dotnet test Torrid.sln` → commit `Add per-sensor threshold bands that fall back to the kind defaults`.

---

### Task 2: Thresholds page — Cards, Advanced list, defaults section, Limits link (Torrid.App) + docs

**Files:** rewrite `Torrid.App/ViewModels/ThresholdsViewModel.cs`, `ThresholdRowViewModel.cs` (kept for the defaults section; reuse), create `SensorThresholdViewModel.cs` (per-sensor row/card), `ThresholdGroupViewModel.cs`; modify `Torrid.App/Views/ThresholdsView.xaml(.cs)`, `Torrid.App/Views/SensorsView.xaml` + `SensorListItemViewModel.cs` (Limits link), `ShellViewModel.cs`/`AppHost.cs` (navigate with a focus target), shared row-set logic (extract from `SensorsViewModel` into a reusable helper so both pages use the same union/ordering/grouping — do not duplicate it); README + docs; tests `Torrid.Tests/ThresholdsViewModelTests.cs` (rewrite for the new model), `SensorsViewModelTests.cs` (Limits link), `ShellViewModelTests.cs`.

**Behaviour:**
- `ThresholdsViewModel(ISensorSource sensors, ConfigManager config)` (AppHost wiring updated): `ObservableCollection<ThresholdGroupViewModel> Groups` (device groups, same titles/order as the Sensors page), a `NotShown` group (collapsed by default) for sensors with Window, Tray and Alerts all off, `ObservableCollection<ThresholdRowViewModel> DefaultRows` (the 10 kind rows, titled as today, section title "Defaults by sensor type"), `bool IsListView { get; set; }` (writes `ThresholdsListView` through `ConfigManager.Update`; two-way with the toggle), `string SearchText` (filters by sensor name/device/sensor-source name, case-insensitive; empty = all; filtering never recreates row instances, it only changes a visibility flag/collection view filter), and `void Focus(string sensorId)` (expands the group containing it, sets `FocusedSensorId` so the view scrolls it into view and focuses its first box).
- `SensorThresholdViewModel` (one per sensor id; instances reused across ticks): `Id`, `Label` (resolved, live), `Caption` (source/hardware name or "Not detected right now"), `ValueText`/`Zone` (live, `--` when null), `BlueMax/GreenMax/YellowMax` (editable; apply immediately through `ConfigManager.Update` + `ThresholdResolver.SetCustom` as soon as the trio is valid, else `Error` set and config untouched — same rule and messages as the kind rows), `bool IsCustom` (badge text "Custom"/"Default"), `ResetCommand` (ClearCustom; boxes return to the kind default), four-segment preview widths (same maths as the kind rows), `bool IsFocused`. While a box is being edited, config-change refreshes must not overwrite the text the user is typing (same policy as the existing kind rows — mirror it).
- Cards view: per-sensor cards in a wrap/grid (like the old kind cards: title, live value pill, three boxes, preview bar, error line, Custom/Default badge, Reset) under device headers. List view: one dense row per sensor under the same group headers (collapsible, expanders remember state in memory only): columns Sensor (name bold + caption muted), Value (zone-coloured), Blue below, Green below, Yellow up to, Status (Default/Custom badge), Reset button; header row with the column titles; 36-40px row height; a search box and the Cards/List segmented toggle in the page header; works at 780 width (horizontal scroll not allowed — drop the caption column text to a tooltip if needed). Keyboard: Tab order row by row, visible focus.
- "Defaults by sensor type" section at the bottom in both views (collapsed `Expander` titled with a one-line explanation: "New sensors start from these limits; changing one only affects sensors you haven't customised.").
- Sensors page: each row gets a muted "Limits" link/button (right of the toggles or in the caption line — choose what fits the layout; must not break column alignment) that calls `ShellViewModel` navigation to the Thresholds page with `Focus(sensorId)`.
- Docs: README (Thresholds section: per-sensor limits, Default/Custom, advanced list, defaults section, Limits link, alert = above "Yellow up to"); update screenshots/text that mention "per kind".

**Tests required (view-model level, hardware-free):** group/ordering parity with the Sensors page (shared helper); a new sensor id appearing in readings produces a new row (instance) without disturbing others; editing a valid trio creates a custom band for only that sensor and flips `IsCustom`; invalid trio sets `Error` and leaves config unchanged; Reset clears and boxes follow the default again, including after the kind default is edited; editing the kind default changes unedited sensors' boxes but not customised ones; two sensors of the same kind keep independent bands; unreadable sensor row (not detected) exists with `--`; NotShown grouping when all three switches are off and the sensor moves out when one is turned on; search filters visibility without recreating instances and clears; `IsListView` writes the config flag and loads it; `Focus(id)` expands the right group and raises the focus request; Limits link on the Sensors row triggers navigation with the sensor id; garbage keys ignored.

- [ ] Steps: failing tests → view models → green → XAML (both views) → screenshots in `--demo` (Cards view, List view, search active, Not shown expanded, defaults section expanded, at default size and at the 780 minimum, plus the Sensors page showing the Limits link; the demo already includes ~10 sensors incl. added ones) and iterate until polished → full build + suite → docs → commit `Per-sensor Thresholds page with an advanced list view`.

---

### Final verification (controller)
Whole-branch review (opus), one fix wave if needed, rebuild `publish\`, collect `Ruling:` lines, delete the workspace.
