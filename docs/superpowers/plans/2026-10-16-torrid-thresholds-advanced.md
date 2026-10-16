# Torrid Thresholds: Defaults open + Advanced section for every sensor Implementation Plan

> **For agentic workers:** Follow the tasks in order. Steps use checkbox (`- [ ]`) syntax. Do not re-decide design; if something in this plan does not match the code you find, stop and report instead of improvising.

**Goal:** Fix "thresholds are missing" on the Thresholds page. Today the page lists only what the Sensors page lists (`SensorRows.Build`: live built-in readings + `config.Sensors` keys + `config.AddedSensors`). The hardware reports many more temperature sensors (for example the other CPU sensors besides "Core (Tctl/Tdie)"), but they only exist in the Add sensor scan (`ISensorCatalogSource` -> `SensorCatalogBuilder`), so they get no row and no way to set limits. Also: **Defaults by sensor type** opens expanded, and a new **Advanced** section at the very bottom lists every other temperature sensor the hardware reports, each with its own Blue/Green/Yellow limits, Default/Custom status and Reset, with search.

**User request (verbatim):** "There are thresholds missing: in the CPU there are other CPU sensors and there should be a way to set the thresholds." and "we can have the default by sensor type open and the after at the bottom the advanced mode where someone can be even more specific."

## Choices made (tell the planner if you want any of these changed)

1. **Root cause confirmed.** `ThresholdsViewModel` uses `SensorRows.Build`; `SensorReadingBuilder.Build` only creates the fixed CPU reading, three GPU readings per GPU and added sensors. Every other temperature sensor is only in the catalog scan.
2. **The catalog is a snapshot, not live.** `ISensorCatalogSource.ScanAsync` opens/reads the hardware tree on demand (it reuses the live `Computer` when the open groups match, otherwise it closes it and opens a temporary one). So Advanced never scans by itself: it has a **Scan for sensors** button (same rule as the Add sensor panel: "never scans by itself"). Scan results are kept in memory for the session. Values shown in Advanced are the values at scan time ("Scan again" refreshes them); live values are not wired in, to avoid putting a second sensor read path behind an advanced list.
3. **Advanced lists the catalog sensors that have no row above.** It excludes (a) sensors already added (they already have a row in the device sections, and a second editor for the same id would be a duplicate), and (b) catalog entries that are the very sensor a built-in card reads (same hardware name + sensor name; the built-in CPU/GPU limits are stored under `cpu` / `gpuN-*`, so a band stored under the `hw:` id of the same physical sensor would silently do nothing). This is the same exclusion `AddSensorViewModel` uses ("Already shown"), reused through a shared helper. Net effect: device sections + Advanced together cover every temperature sensor, without duplicates. The user's wording said "every sensor"; this is how that is achieved without two editors for one sensor.
4. **Advanced is collapsed by default; Defaults by sensor type is expanded by default.** Advanced needs a scan first, and it is the long list. Searching with a match in Advanced expands it.
5. **Page order stays:** device sections, then Defaults (now open), then Advanced last. Moving Defaults above the device sections is a one-element move in the XAML if the user wants it.
6. **Setting limits in Advanced does not add the sensor to the Sensors page and changes nothing shown anywhere.** It only writes `config.Sensors[id].Thresholds` (via `ThresholdResolver.SetCustom`). The limits take effect when the sensor is later added (Add merges into the same preference, it does not replace it). The section text says so.
7. **Persistence is unchanged.** Verified: `SensorPreferences.Update`, `ThresholdResolver`, and `JsonConfigStore.Normalize` do not key anything off `AddedSensors` for `config.Sensors` entries, so a preference holding only `Thresholds` for an unadded `hw:` id survives save/load, and `ClearCustom` removes the entry when empty. `SensorRows.Build` ignores unadded `hw:` keys (`SensorDisplay.Identity` returns null for them), so no stray row appears above. Task 1 pins this with tests. Known and kept: removing an added sensor on the Sensors page (`AddSensorViewModel.Remove`) also drops its preference, so its custom limits are forgotten; when it reappears in Advanced it shows Default.
8. **No new startup flag for screenshots.** The scanned state of Advanced cannot be screenshotted without clicking; it is covered by view-model tests and not visually verified.
9. Advanced device groups reuse `ThresholdGroupViewModel` and the existing `CardGroupTemplate` / `ListGroupTemplate`; item rows reuse `SensorThresholdViewModel` (identity built straight from the catalog entry). No new view-model types, projects or dependencies.

## Decisions carried over from Phase 6 (binding, unchanged)

`docs/superpowers/plans/2026-10-12-torrid-phase6-per-sensor-thresholds.md` still applies: effective band = own valid band else kind default (`ThresholdResolver.For`), edits go through `ConfigManager.Update` + `ThresholdResolver.SetCustom/ClearCustom`, `SensorRows` is the shared row helper, band validity `0 <= BlueMax < GreenMax < YellowMax <= 150`, `UiThread.Post` for sensor-thread events, thresholds read on the UI thread only. No consumer may index `Thresholds[kind]` directly for a sensor (this plan adds none).

## Global Constraints

- dotnet is at `"C:\Program Files\dotnet\dotnet.exe"` (PowerShell: `& "C:\Program Files\dotnet\dotnet.exe" build Torrid.sln`). Run `dotnet build Torrid.sln` and `dotnet test Torrid.sln` after EACH task; both must be green before committing.
- Commit each task on `master` locally with the task's message, ending the message with the trailer line `Co-Authored-By: Claude Haiku 5.5 <noreply@anthropic.com>`. Do NOT push.
- Do NOT run real-hardware sensor scans. For anything that runs the app, use `--demo` with a throwaway `--config` in `%TEMP%`. Leave no `Torrid.App` process running. Never launch `publish\Torrid.App.exe`. If a run re-created `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Torrid (Dev).lnk`, delete it.
- `ConfigManager.Config` is mutated ONLY via `ConfigManager.Update`. Do not mutate `AddedSensors` in place (this plan does not touch it).
- No new projects, no new NuGet packages, no config/schema changes.
- All existing tests keep passing; the only intentional behaviour change is that the Defaults expander starts expanded (XAML only, no test).
- Write tests first, see them fail (for Tasks 2-3; Task 1 is characterization and should pass immediately), then implement.

## Review Focus

1. Setting limits in Advanced must not add the sensor, show it anywhere, or create any config entry besides `Sensors[id].Thresholds` (no `ShowInWindow`/`ShowInTray`/`Notify` set).
2. Advanced never duplicates a sensor that has a row in the device sections, and never offers the built-in CPU/GPU sensors.
3. Scan is only started by the button; a failed/refused/cancelled scan keeps the previous list and shows a message; `IsScanning` always returns to false.
4. When a sensor in Advanced is added on the Sensors page, it leaves Advanced and appears in its device section with the same limits (Custom). When it is removed again it returns to Advanced (Default, since remove clears the preference).
5. Per-tick updates still only touch live values of existing rows; Advanced instances are reused across scans and config changes.

---

### Task 1: Pin that limits for a not-yet-added sensor are kept (tests only)

**Files:** modify `Torrid.Tests/ThresholdResolverTests.cs`, `Torrid.Tests/JsonConfigStoreTests.cs`, `Torrid.Tests/AddSensorViewModelTests.cs`. No production code changes expected.

Use whatever helpers those files already use (read them first; copy their style for building configs, a temp-path `JsonConfigStore`, and `Make(...)` for the Add sensor VM). Use the id `"hw:/amdcpu/0/temperature/3"` (a valid generic id) as the not-added sensor; it is NOT in `AddedSensors` in any of these tests.

**Tests to add (names fixed):**
- `ThresholdResolverTests.SetCustom_OnAnIdThatIsNotAnAddedSensor_IsStoredAndResolved`: `SetCustom(config, id, band 40/60/80)` on a default `AppConfig`; assert `HasCustom(config, id)`, `For(config, id, SensorKind.CpuOther)` returns 40/60/80, `config.AddedSensors` is empty, and `config.Sensors[id]` has `ShowInWindow == null && ShowInTray == null && Notify == null && Label == null`.
- `ThresholdResolverTests.ClearCustom_OnThatId_RemovesTheEntryWhenNothingElseIsLeft`: after `SetCustom` then `ClearCustom`, `config.Sensors.ContainsKey(id)` is false and `For(...)` is the `CpuOther` default (40/70/85).
- `JsonConfigStoreTests.Thresholds_ForAnIdNotInAddedSensors_SurviveSaveAndLoad`: save a config whose `Sensors` has only that id with a valid band and `AddedSensors` empty; load it back with a new store; assert the band (40/60/80) and that no `AddedSensors` entry exists.
- `AddSensorViewModelTests.Add_KeepsLimitsAlreadySetForThatSensor`: build the config with `SetCustom(config, CcdId, 50/65/90)` (use the id of the `CcdId` catalog entry that file already defines), scan, `Add` the CCD item; assert `HasCustom` is still true with 50/65/90, `AddedSensors` contains it, and `config.Sensors[CcdId].ShowInWindow == true`.

- [ ] Steps: add the four tests -> they should pass on the current code. If any fails, STOP and report the failure; do not change production code. -> `dotnet build Torrid.sln` + `dotnet test Torrid.sln` -> commit `Pin that limits set for a sensor that is not added yet are kept`.

---

### Task 2: Share the "built-in sensor" test between Add sensor and Thresholds

**Files:** modify `Torrid.App/ViewModels/SensorRows.cs`, `Torrid.App/ViewModels/AddSensorViewModel.cs`. No new tests (pure move; `AddSensorViewModelTests` already cover "Already shown", keep them passing unchanged).

**Interfaces to add to `internal static class SensorRows` (same namespace `Torrid.App.ViewModels`, add `using Torrid.Core.Sensors;`):**
```csharp
/// <summary>The kinds of the built-in cards; a catalog entry they already read is "Already shown".</summary>
private static readonly SensorKind[] BuiltInKinds =
{
    SensorKind.CpuPackage, SensorKind.GpuCore, SensorKind.GpuHotSpot, SensorKind.GpuVram,
};

/// <summary>(hardware name, source sensor name) of every built-in card reading in <paramref name="readings"/>.</summary>
public static HashSet<(string Hardware, string Sensor)> BuiltInSources(IEnumerable<SensorReading> readings)

/// <summary>A built-in id, or the very sensor a built-in card reads (same hardware and sensor name).</summary>
public static bool IsBuiltIn(CatalogSensor sensor, HashSet<(string Hardware, string Sensor)> builtInSources)
```
Bodies: move the existing loop and expression verbatim from `AddSensorViewModel.BuiltInSources()` (the `foreach` over readings that skips `null`, generic ids and kinds not in `BuiltInKinds`, and adds `(HardwareName, SourceSensorName)` when both are non-null) and from `AddSensorViewModel.IsBuiltIn` (`!SensorIdentity.IsGenericId(sensor.Id) || builtInSources.Contains((sensor.HardwareName ?? "", sensor.Name ?? ""))`).

**In `AddSensorViewModel`:** delete the private `BuiltInKinds` field and the private static `IsBuiltIn`; keep the private instance `BuiltInSources()` but reduce it to the `try { readings = _currentReadings(); } catch { Debug.WriteLine(...); readings = null; }` part followed by `return SensorRows.BuiltInSources(readings ?? Array.Empty<SensorReading>());`. In `ScanAsync` call `SensorRows.IsBuiltIn(s, builtInSources)` where `IsBuiltIn(s, builtInSources)` was called. Remove any `using` that becomes unused (do not leave warnings if the build treats warnings as errors).

- [ ] Steps: edit -> `dotnet build Torrid.sln` + `dotnet test Torrid.sln` (all existing tests green, count unchanged) -> commit `Share the built-in sensor check between Add sensor and Thresholds`.

---

### Task 3: Advanced sensors in `ThresholdsViewModel` + wiring

**Files:** modify `Torrid.App/ViewModels/ThresholdsViewModel.cs`, `Torrid.App/AppHost.cs` (line ~78); tests in `Torrid.Tests/ThresholdsViewModelTests.cs`. `SensorThresholdViewModel`, `ThresholdGroupViewModel` and `ThresholdResolver` are used as they are (no changes).

**Constructor (backwards compatible):**
```csharp
public ThresholdsViewModel(ISensorSource sensors, ConfigManager config, ISensorCatalogSource? catalog = null)
```
AppHost: `new ThresholdsViewModel(Sensors, ConfigManager, Catalog)`.

**New public members of `ThresholdsViewModel` (add `using System.Diagnostics; using System.Windows.Input;`):**
```csharp
/// Device sections of catalog sensors that have no row above (empty until a scan).
public ObservableCollection<ThresholdGroupViewModel> AdvancedGroups { get; }
/// The Advanced section is collapsed at first; a search that matches inside it opens it. In-memory only.
public bool IsAdvancedExpanded { get; set; }          // Set(ref ...) with change notification
public bool IsScanning { get; private set; }          // raises ScanButtonText; RaiseCanExecuteChanged on the command
public bool HasScanned { get; private set; }          // true after the first scan that returned sensors (even if all are filtered out)
public string? ScanMessage { get; private set; }      // raises HasScanMessage
public bool HasScanMessage => ScanMessage is not null;
public string ScanButtonText => IsScanning ? "Scanning…" : HasScanned ? "Scan again" : "Scan for sensors";
/// True when a scan succeeded but every sensor it found already has a row above.
public bool ShowAllListed => HasScanned && ScanMessage is null && AdvancedGroups.Count == 0;
public const string AllListedMessage = "Every temperature sensor found is already on your Sensors page.";
public ICommand ScanCommand { get; }                  // new RelayCommand(_ => _ = ScanAsync(), _ => !IsScanning)
public Task ScanAsync()
```

**New private state:** `ISensorCatalogSource? _catalog`; `List<CatalogSensor> _catalogSensors = new()`; `Dictionary<string, SensorThresholdViewModel> _advancedById`; `Dictionary<string, ThresholdGroupViewModel> _advancedGroupsByKey`.

**`ScanAsync()` behaviour (mirror `AddSensorViewModel.ScanAsync`, reuse its public constants `AddSensorViewModel.NoCatalogMessage`, `.NothingFoundMessage`, `.CancelledMessage`):**
1. If `IsScanning` return. Set `IsScanning = true`.
2. `result = _catalog is null ? SensorCatalogResult.Unavailable(NoCatalogMessage) : await Task.Run(() => catalog.ScanAsync())` inside `try`; `catch (OperationCanceledException)` -> result null, error `CancelledMessage`; `catch (Exception ex)` -> `Debug.WriteLine`, result null, error `$"Couldn't scan hardware: {ex.Message}"`; `finally { IsScanning = false; }`.
3. If result is null: `ScanMessage = error`; return (previous list kept).
4. `var found = (result.Sensors ?? Array.Empty<CatalogSensor>()).Where(s => s is not null && SensorIdentity.IsGenericId(s.Id)).ToList();`
5. If `found.Count == 0`: `ScanMessage = result.UnavailableReason ?? NothingFoundMessage`; return (previous list kept; `HasScanned` unchanged).
6. Else `_catalogSensors = found; HasScanned = true; ScanMessage = null; SyncAdvanced();` then raise `ShowAllListed`.

**`SyncAdvanced()` (private; builds `AdvancedGroups`):**
1. `config = _config.Config; readings = CurrentReadings(); builtIn = SensorRows.BuiltInSources(readings.Where(r => r is not null)); gpuNames = DeviceGroups.GpuNames(readings.Where(r => r is not null));`
2. `wanted` = `_catalogSensors` where `!_itemsById.ContainsKey(s.Id)` and `!SensorRows.IsBuiltIn(s, builtIn)`, each paired with `DeviceGroups.For(s.Kind, null, s.HardwareName, gpuNames)`, stable `OrderBy(Group.Rank).ThenBy(Group.Index)` (keeps catalog order inside a device).
3. For each wanted entry: `identity = new SensorIdentity(s.Id, s.Name, s.Kind, null)`; reuse `_advancedById[s.Id]` when `item.Identity == identity`, else create `new SensorThresholdViewModel(identity, _config)` and store it. Then `item.DeviceTitle = group.Title; item.RefreshFromConfig(); item.UpdateReading(new SensorReading(s.Id, s.Name, s.Kind, null, s.ValueCelsius, s.HardwareName, s.Name));` (the scan value is shown as the live value; `SourceSensorName` is non-null so `SensorRows.Detected` keeps it).
4. Group items by `group.Key` into `ThresholdGroupViewModel` instances taken from `_advancedGroupsByKey` (create with `new ThresholdGroupViewModel(key, title)`; `isNotShown` false), `CollectionSync.Sync(group.Items, items)`, `group.Subtitle = DeviceGroups.Subtitle(items.Select(i => i.HardwareName))`. Then `CollectionSync.Sync(AdvancedGroups, orderedGroups)`. Groups no longer present: `Items.Clear()` (same as `Sync` does for the main groups). Remove `_advancedById` entries not in `wanted`.
5. `ApplyFilter(expandGroups: false);` then `Raise(nameof(ShowAllListed));`.

**Edits to existing methods:**
- `Sync(...)`: in the rebuild branch (after the `foreach stale` removal of `_itemsById` and `_layoutKeys = layout;`) call `SyncAdvanced();` immediately before the final `UpdateLiveValues(readings);`. The early-return tick path is unchanged (Advanced values are the scan values and do not change per tick).
- `ApplyFilter`: iterate items over `_itemsById.Values.Concat(_advancedById.Values)` and groups over `Groups.Concat(AdvancedGroups)` (same visibility rule as today). After the group loop add: `if (expandGroups && query.Length > 0 && AdvancedGroups.Any(g => g.Items.Any(i => i.IsVisible))) IsAdvancedExpanded = true;`. `Focus(...)` is unchanged (Advanced rows are not focus targets).
- `ThresholdsViewModel.Matches` unchanged (it already searches label, default label, caption, hardware name, device title).

**Tests (`Torrid.Tests/ThresholdsViewModelTests.cs`; add a private nested `FakeCatalog : ISensorCatalogSource` like the one in `AddSensorViewModelTests` with `Result`, `Throw`, `Calls`; extend `Make` with an optional `ISensorCatalogSource? catalog = null` passed as the third constructor argument; add helpers `Advanced(vm, id)` = `vm.AdvancedGroups.SelectMany(g => g.Items).Single(i => i.Id == id)`).** Catalog used by the tests (ids are valid `hw:` ids): `CcdId` "hw:/amdcpu/0/temperature/3" = `("Core #1 (CCD1)", Cpu5950X, CpuOther, 61.4)`; `TctlId` "hw:/amdcpu/0/temperature/2" = `("Core (Tctl/Tdie)", Cpu5950X, CpuOther, 60)` (same hardware + name as the built-in CPU reading from `Cpu()`); `VrmId` (existing const) = `("VRM", Board, Motherboard, 52)`. Tests:
1. `Advanced_BeforeAScan_IsEmpty_AndTheCatalogWasNeverCalled`: `Make(catalog: fake)`; `fake.Calls == 0`, `AdvancedGroups` empty, `!HasScanned`, `!IsAdvancedExpanded`, `ScanButtonText == "Scan for sensors"`; also after a tick (`source.Publish`) still `Calls == 0`.
2. `Scan_ListsTheCatalogSensorsThatHaveNoRow_GroupedByDevice`: readings = `CpuAndGpu0()`; config has `VrmId` as an added sensor (`WithAdded`); scan. Assert `AdvancedGroups.Select(g => g.Title)` equals `["CPU"]` and its items are exactly `[CcdId]` (Tctl excluded as built-in; VRM excluded because it is added and has a row in `Groups`), `ScanButtonText == "Scan again"`, `ScanMessage` null, `Item(vm, VrmId)` still found in `Groups`. Then a second test case: with an empty config and the same readings, titles are `["CPU", "Motherboard"]` and the CPU group subtitle is `Cpu5950X`.
3. `Scan_AllSensorsAlreadyListed_SetsShowAllListed`: catalog containing only `TctlId`; `HasScanned`, `AdvancedGroups` empty, `ShowAllListed` true, `ScanMessage` null.
4. `Scan_ShowsTheScanValueAndCaption`: `Advanced(vm, CcdId).ValueText == "61 °C"`, `HasValue`, `Caption` contains `Cpu5950X`, `Label == "Core #1 (CCD1)"`, zone is `ColorZone` of 61.4 under the CpuOther default band (40/70/85 -> `ColorZone.Green`).
5. `Scan_DoesNotAddAnythingOrChangeTheConfig`: after scan, `config.AddedSensors` empty, `config.Sensors` empty, `vm.Groups.SelectMany(g => g.Items).Select(i => i.Id)` unchanged from before the scan.
6. `EditingAnAdvancedSensor_StoresOnlyItsOwnBand`: `Advanced(vm, CcdId).GreenMax = 65` (with Blue 40 / Yellow 85 default), then assert `item.IsCustom`, `item.BadgeText == "Custom"`, `ThresholdResolver.HasCustom(config, CcdId)`, `config.Sensors[CcdId]` has `Thresholds` 40/65/85 and `ShowInWindow == null && ShowInTray == null && Notify == null`, `config.AddedSensors` empty, the sensor is NOT in `vm.Groups`, and another Advanced sensor (VRM, with empty-config case) keeps Default and its kind default.
7. `ResetOnAnAdvancedSensor_RemovesTheEntryAndFollowsTheKindDefault`: edit then `ResetCommand.Execute(null)`; `config.Sensors.ContainsKey(id)` false, `!IsCustom`, boxes equal the CpuOther default; then edit the CpuOther default row (`Row(vm, SensorKind.CpuOther).GreenMax = 72`) and assert the unedited Advanced sensor's `GreenMax == 72` while an edited one keeps its own.
8. `AnAdvancedSensorThatGetsAdded_MovesToItsDeviceSection_WithItsLimits`: edit CCD (Custom 40/65/85), then `manager.Update(c => { c.AddedSensors = new Dictionary<string, AddedSensor>(c.AddedSensors) { [CcdId] = new AddedSensor { Name = "Core #1 (CCD1)", HardwareName = Cpu5950X, Kind = SensorKind.CpuOther } }; SensorPreferences.Update(c, CcdId, p => p.ShowInWindow = true); })`; assert it is gone from `AdvancedGroups`, present in `Groups` with `IsCustom` and 40/65/85. Then remove it again the way the Sensors page does (`manager.Update(c => { c.AddedSensors = new Dictionary<string, AddedSensor>(); c.Sensors.Remove(CcdId); })`) and assert it is back in Advanced as Default (`!IsCustom`).
9. `Scan_Unavailable_KeepsThePreviousList_AndShowsTheReason`: first scan OK, then `fake.Result = SensorCatalogResult.Unavailable("Monitoring is paused while Riot Vanguard is running.")`, scan again: `ScanMessage` equals that text, `HasScanMessage`, list unchanged (same item instances), `!IsScanning`.
10. `Scan_ThatThrows_ShowsAMessage_AndIsNotLeftScanning`: `fake.Throw = new InvalidOperationException("boom")`; `ScanMessage == "Couldn't scan hardware: boom"`, `!IsScanning`.
11. `Scan_WithoutACatalog_ShowsNoCatalogMessage`: `Make()` (no catalog): `ScanMessage == AddSensorViewModel.NoCatalogMessage`.
12. `ScanningAgain_ReusesTheRowInstances`: two scans; `ReferenceEquals` on the same item across scans.
13. `Search_FiltersAdvancedRowsWithoutRecreatingThem_AndOpensTheSection`: after scan, `SearchText = "ccd1"` -> CCD visible, Advanced group visible, `IsAdvancedExpanded` true; `SearchText = "nomatch"` -> item `IsVisible` false and group `IsVisible` false; `SearchText = ""` -> all visible again and same instance.
14. `AdvancedValues_AreNotTouchedByATick`: publish new readings after the scan; the CCD item keeps `ValueText "61 °C"` and the same instance.

Existing tests must keep passing unchanged (the new constructor parameter is optional).

- [ ] Steps: add the tests (they won't compile until the members exist: add member stubs only as needed to see them fail, or accept compile failure as the "red" step) -> implement -> `dotnet build Torrid.sln` + `dotnet test Torrid.sln` -> commit `Thresholds: list every temperature sensor the hardware reports in an Advanced section`.

---

### Task 4: XAML, docs and a demo check

**Files:** modify `Torrid.App/Views/ThresholdsView.xaml`, `README.md`. (`ThresholdsView.xaml.cs` unchanged.)

**XAML changes (both the Cards `ScrollViewer` and the List `ScrollViewer`, which each contain a Defaults expander):**
1. Add `IsExpanded="True"` to BOTH `<Expander Style="{StaticResource DefaultsSectionStyle}" Header="Defaults by sensor type" ...>` elements (they currently start collapsed). Leave the `DefaultsSectionStyle` style as is.
2. Directly AFTER each Defaults expander (still inside that view's `StackPanel`, so Advanced is the last thing on the page) add an Advanced expander. Cards view uses `CardGroupTemplate`, List view uses `ListGroupTemplate`; everything else identical:
```xml
<Expander Style="{StaticResource DefaultsSectionStyle}" Header="Advanced: more sensors"
          HeaderTemplate="{StaticResource SectionTitleTemplate}"
          IsExpanded="{Binding IsAdvancedExpanded, Mode=TwoWay}">
    <StackPanel Margin="16,0,16,8">
        <TextBlock Style="{StaticResource CaptionTextStyle}" Margin="0,0,0,12"
                   Text="Every other temperature sensor your hardware reports, for fine-grained limits. Setting limits here does not add the sensor to the Sensors page; they apply once you add it there. Values are from the last scan."/>
        <StackPanel Orientation="Horizontal" Margin="0,0,0,14">
            <Button Style="{StaticResource SecondaryButtonStyle}" Content="{Binding ScanButtonText}"
                    Command="{Binding ScanCommand}" Padding="14,4" MinHeight="28" FontSize="13"
                    AutomationProperties.Name="Scan for sensors"/>
            <TextBlock Text="{Binding ScanMessage}" Style="{StaticResource CaptionTextStyle}" VerticalAlignment="Center"
                       Margin="12,0,0,0" Visibility="{Binding HasScanMessage, Converter={StaticResource BoolToVisibility}}"/>
            <TextBlock Text="Every temperature sensor found is already on your Sensors page." Style="{StaticResource CaptionTextStyle}"
                       VerticalAlignment="Center" Margin="12,0,0,0"
                       Visibility="{Binding ShowAllListed, Converter={StaticResource BoolToVisibility}}"/>
        </StackPanel>
        <ItemsControl ItemsSource="{Binding AdvancedGroups}" ItemTemplate="{StaticResource CardGroupTemplate}" Focusable="False"/>
    </StackPanel>
</Expander>
```
(in the List view use `{StaticResource ListGroupTemplate}`). `BoolToVisibility`, `SecondaryButtonStyle` and `CaptionTextStyle` are already used in this file / app resources; use them as written. The page subtitle in Row 0 stays as is.
3. Do not touch the search box, Cards/List toggle or the device-section templates.

**README.md (Thresholds section, lines ~55-65):** replace the "Defaults by sensor type" bullet with: `- **Defaults by sensor type.** Open by default, below the device sections. New sensors start from these limits, and changing one only affects sensors you have not customised.` and add a bullet after "Not shown": `- **Advanced.** The last section lists every other temperature sensor your hardware reports (the other CPU sensors, motherboard chips, drives and so on) that is not on your Sensors page. Press **Scan for sensors** to fill it (Torrid never scans on its own); sensors are grouped by device and each has its own limits, Default/Custom badge and Reset, and the search box covers them. Setting limits here does not add the sensor to the Sensors page; the limits apply once you add it there. The values shown are from the last scan.` Also change the page list in the first README bullet (line ~24) only if it says limits are per type; it already says "for each sensor", so leave it.

**Demo check (no hardware):** `dotnet build Torrid.sln`, then use `tools\Capture-Window.ps1` (read its header for usage) with `--demo --page Thresholds` and `--config` pointing at a new file in `%TEMP%`, at the default size and at the 780 px minimum width, in Cards and List views. Check with the Read tool on the PNG: Defaults expander open, Advanced header last and collapsed, nothing clipped. The scanned state cannot be reached without clicking, so it is not screenshot-verified (the view-model tests cover it). Kill any `Torrid.App` process afterwards and delete the `%TEMP%` config and the Dev `.lnk` if it was created.

- [ ] Steps: edit XAML -> README -> `dotnet build Torrid.sln` + `dotnet test Torrid.sln` -> demo check -> commit `Open Defaults by sensor type by default and add the Advanced section to the Thresholds page`.

---

### Final verification (controller)
Whole-branch review of the four commits, one fix wave if needed; manually open `--demo --page Thresholds`, press **Scan for sensors**, set limits on a CPU sensor in Advanced, confirm the Sensors page is unchanged, then add that sensor and confirm it appears under CPU as Custom.
