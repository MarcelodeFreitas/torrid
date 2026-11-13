# Overlay Page and Editable Sensor Labels Implementation Plan

> **For agentic workers:** Follow the tasks in order, one at a time (no parallel tasks: they share `AppHost.cs`, `SettingsView.xaml` and the Overlay view model). Steps use checkbox (`- [ ]`) syntax. Do not re-decide design; if something in this plan does not match the code you find, stop and report instead of improvising.

**Goal:** (1) The user can rename each sensor from the new Overlay page. (2) The side menu becomes Home, Sensors, Thresholds, **Overlay**, Settings; the overlay card (shortcut, text size, text colour, sensors to show) moves from the Settings page to the new Overlay page. Settings keeps everything else.

**User request (translated):** the label of each sensor should be editable; add an Overlay section to the side menu and move the overlay settings there.

## Choices made (tell the planner if you want any changed)

1. **A label editor already exists:** the Sensors page (`Torrid.App/Views/SensorsView.xaml`, line ~76) has a name `TextBox` per sensor bound to `SensorListItemViewModel.Label`, with a reset button. It writes `SensorPreference.Label` through `SensorPreferences.Update`. `SensorDisplay.ResolveLabel` is what Home, tray, alerts and the overlay all read, so that edit already changes everything.
2. **Label location (new):** the Overlay page's sensor list (`OverlaySensorItem`) shows today a read-only label. It becomes the same kind of editable box (empty or the default name = default), writing the same `SensorPreference.Label`. One value, two places to edit it. No new preference field, no new config.
3. **`OverlaySensorItem.Label` setter copies the logic of `SensorListItemViewModel.Label`** (trim; blank or default name stores `null`; write only when the stored value changes). Not extracted into a shared helper (about 8 lines, two users; extract only if a third appears).
4. **New `PageId.Overlay`** between Thresholds and Settings. Nav item glyph `"\uE7F4"` (Segoe MDL2 "TVMonitor"), title `"Overlay"`. The page object is the existing `OverlaySettingsViewModel` (no new view model). A `DataTemplate` for it in `MainWindow.xaml` maps it to a new `OverlayView`.
5. **`SettingsPageViewModel.Overlay` is REMOVED**, with its optional constructor parameter. Nothing in the tests uses it (all three `new SettingsPageViewModel(...)` calls in `SettingsPageViewModelTests` pass 5 arguments), so no test changes there.
6. **The overlay card is moved, not rewritten:** same controls, same styles; bindings lose the `Overlay.` prefix. The card's inner "Overlay" title is dropped (the page header carries it). `HotkeyBox_PreviewKeyDown` moves to `OverlayView.xaml.cs` and calls `(DataContext as OverlaySettingsViewModel)?.TrySetHotkey(...)`.
7. Tray "Settings" still opens the Sensors page. No other navigation changes.

## Global Constraints

- dotnet is at `"C:\Program Files\dotnet\dotnet.exe"` (PowerShell: `& "C:\Program Files\dotnet\dotnet.exe" build Torrid.sln`). Run `dotnet build Torrid.sln` and `dotnet test Torrid.sln` after EACH task; both must be green before moving on.
- **Do NOT commit and do NOT publish.** Leave all changes uncommitted. The working tree already has many unrelated uncommitted edits (see `git status`). Do not revert or reformat them; make your edits on top.
- Do NOT run real-hardware scans. To run the app use `--demo --show` with a throwaway `--config` in `%TEMP%`. Never launch `publish\Torrid.App.exe`. Do not kill any Torrid process. If a `Torrid.App` process is ALREADY running, do NOT start a second copy: skip the live capture and say so in your report. If a run re-created `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Torrid (Dev).lnk`, delete it.
- No new projects, no new NuGet packages. Reuse existing styles (`SectionCardStyle`, `SettingTitleStyle`, `DividerStyle`, `CaptionTextStyle`, `ToggleSwitchStyle`, `RadioCardListStyle`, `DarkSliderStyle`, `IconButtonStyle`).
- Minimal change: no features beyond the two requests.
- Write the tests first for Tasks 1-2, see them fail (compile failure counts), then implement.
- All existing tests keep passing.

## Review Focus

1. Renaming on the Overlay page and on the Sensors page write the same `SensorPreference.Label`, and a blank or default-name edit stores `null` (the entry disappears when nothing else is set).
2. The Settings page no longer mentions the overlay; no dead code is left (the `HotkeyBox_PreviewKeyDown` handler, the `using Torrid.Core.Overlay`, the `Overlay` property, the constructor parameter).
3. `--page Overlay` works (parsed by name from the `PageId` enum, so no parser change).
4. The hotkey box still records a combination and shows the registration error on the new page.

---

### Task 1: Editable label on the Overlay sensor rows (view model)

**Goal:** `OverlaySensorItem.Label` becomes read-write with the same rules as the Sensors page.

**Files to modify:** `Torrid.App/ViewModels/OverlaySettingsViewModel.cs`, `Torrid.Tests/OverlaySettingsViewModelTests.cs`.

**Steps:**
- [ ] `OverlaySensorItem`: add `public string DefaultLabel => Identity.DefaultLabel;`. Replace `public string Label { get => _label; private set => Set(ref _label, value); }` with a public property whose setter does exactly what `SensorListItemViewModel.Label` does (copy it from `Torrid.App/ViewModels/SensorListItemViewModel.cs`, lines ~49-68): `trimmed = value?.Trim() ?? ""`; `custom = trimmed.Length == 0 || trimmed == DefaultLabel ? null : trimmed`; read the stored label from `_config.Config.Sensors`; only when `stored != custom` call `_config.Update(c => SensorPreferences.Update(c, Id, p => p.Label = custom))`; then `_label = custom ?? DefaultLabel; Raise(nameof(Label));`. No `HasCustomLabel` and no reset command (not needed: clearing the box resets).
- [ ] `RefreshFromConfig` keeps working: change `Label = SensorDisplay.ResolveLabel(...)` to `Set(ref _label, SensorDisplay.ResolveLabel(_config.Config, Identity), nameof(Label));` (the setter must not write during a refresh).

**Tests to write (exact names, in `OverlaySettingsViewModelTests`, using the existing `Make()` and `Cpu()` helpers):**
- `Sensors_Label_Edit_StoresACustomLabel` (set `"5950X"`: `config.Config.Sensors["cpu"].Label == "5950X"`, `item.Label == "5950X"`)
- `Sensors_Label_BlankOrDefault_ClearsTheCustomLabel` (set `"5950X"`, then `"   "`: the `cpu` entry is gone and `item.Label == "CPU"`; set `"5950X"`, then `"CPU"`: same)
- `Sensors_Label_Edit_KeepsOtherPreferences` (set `IsShown = false` first, then a label: `ShowInOverlay` still false)
- `Sensors_Label_ComesBackFromTheConfig_WhenChangedElsewhere` (write `Label = "Renamed"` via `config.Update(...)`, call `sensors.Publish(new[] { Cpu() })`; `item.Label == "Renamed"`. Use the existing test style for UI-thread hops: look at how the other tests in the file trigger `Refresh`.)

**Acceptance check:** `dotnet build Torrid.sln` then `dotnet test Torrid.sln` (all green, only the 4 new tests added).

---

### Task 2: PageId.Overlay, OverlayView and navigation

**Goal:** A new sidebar item opens the Overlay page with the moved controls plus the label box. (The Settings page still has its card until Task 3; both bind the same view model, which is fine for one task.)

**Files to create:** `Torrid.App/Views/OverlayView.xaml`, `Torrid.App/Views/OverlayView.xaml.cs`.
**Files to modify:** `Torrid.App/ViewModels/ShellViewModel.cs` (the `PageId` enum is in this file), `Torrid.App/MainWindow.xaml`, `Torrid.App/AppHost.cs`, `Torrid.App/StartupOptions.cs` (doc comment only), `Torrid.Tests/StartupOptionsTests.cs`.

**Steps:**
- [ ] `PageId`: `{ Home, Sensors, Thresholds, Overlay, Settings }`. Enum order matters for nothing persisted; confirm with `Grep` that no code casts it to an int (if one does, stop and report).
- [ ] `AppHost.cs` nav list: insert `new NavItem(PageId.Overlay, "Overlay", "\uE7F4", OverlaySettings),` between Thresholds and Settings. Leave the `SettingsPage` construction alone for now.
- [ ] `MainWindow.xaml`: add `<DataTemplate DataType="{x:Type vm:OverlaySettingsViewModel}"><views:OverlayView/></DataTemplate>` after the Thresholds template.
- [ ] `OverlayView.xaml`: a `UserControl` `x:Class="Torrid.App.Views.OverlayView"`, same root attributes as `SettingsView.xaml` (`Background="Transparent" Focusable="True" IsTabStop="False" FocusVisualStyle="{x:Null}"`). Copy from `SettingsView.xaml`: the `SettingTitleStyle`, `DividerStyle` and `SectionCardStyle` resources; the page header `StackPanel` (`Grid.Row="0"`, title and subtitle: copy its structure, title text `Overlay`, subtitle `Show chosen sensor temperatures on screen, in games too.`); the outer `Grid`/`ScrollViewer`/`StackPanel` layout. Inside, ONE `SectionCardStyle` border holding the card content currently in `SettingsView.xaml` between `<!-- ===== Overlay ===== -->` and its closing `</Border>`, WITHOUT the `Overlay` card-title `TextBlock`, with every `Overlay.` binding prefix removed (`Overlay.HotkeyText` -> `HotkeyText`, and so on for `HotkeyError`, `HasHotkeyError`, `TextSize`, `ColorChoices`, `TextColor`, `Sensors`). Keep `x:Name="HotkeyBox"` and `PreviewKeyDown="HotkeyBox_PreviewKeyDown"`. Needs `xmlns:vm="clr-namespace:Torrid.App.ViewModels"`.
- [ ] Sensor row template (inside "Sensors to show"): replace the label `TextBlock` with `<TextBox Text="{Binding Label}" FontSize="14" MaxLength="40" AutomationProperties.Name="{Binding DefaultLabel, StringFormat='Name for {0}'}" ToolTip="Rename this sensor. Clear the name to use the default."/>` (same attributes as `SensorsView.xaml` line ~76), inside the existing left `StackPanel` above the `DeviceTitle` line; give the `TextBox` `MaxWidth="280" HorizontalAlignment="Left"`. Update the toggle's automation name to `StringFormat='Show {0} in the overlay'` bound to `DefaultLabel`. Change the "Sensors to show" caption to `Choose which sensors the overlay lists. You can rename a sensor here; the new name shows everywhere.`
- [ ] `OverlayView.xaml.cs`: `namespace Torrid.App.Views;` `public partial class OverlayView : UserControl` with the constructor from `SettingsView.xaml.cs` (`InitializeComponent(); MouseDown += (_, _) => Focus();`) and the `HotkeyBox_PreviewKeyDown` method copied from `SettingsView.xaml.cs`, with the last line changed to `(DataContext as OverlaySettingsViewModel)?.TrySetHotkey(new Hotkey(modifiers, KeyInterop.VirtualKeyFromKey(key)));`. Same `using`s. Do NOT delete the original in `SettingsView.xaml.cs` yet.
- [ ] `StartupOptions.cs`: in the doc comment change `Home|Sensors|Thresholds|Settings` to `Home|Sensors|Thresholds|Overlay|Settings`.

**Tests to write (in `StartupOptionsTests.Page_IsParsedCaseInsensitively`, add one `[InlineData("overlay", PageId.Overlay)]`):**
- `StartupOptionsTests.Page_IsParsedCaseInsensitively` (extended Theory row, shown above)
- `ShellViewModelTests`: no change (it builds its own two-item shell; the page count is not asserted anywhere).

**Acceptance check:** `dotnet build Torrid.sln` then `dotnet test Torrid.sln` (all green).

---

### Task 3: Remove the overlay card from Settings

**Goal:** Settings no longer shows or knows about the overlay.

**Files to modify:** `Torrid.App/Views/SettingsView.xaml`, `Torrid.App/Views/SettingsView.xaml.cs`, `Torrid.App/ViewModels/SettingsPageViewModel.cs`, `Torrid.App/AppHost.cs`.

**Steps:**
- [ ] `SettingsView.xaml`: delete the whole `<!-- ===== Overlay ===== -->` `Border` (from the comment to its closing `</Border>`, just before `<!-- ===== Startup ===== -->`). Do not touch other cards. If `RadioCardListStyle`, `DarkSliderStyle` or `vm:OverlayColorChoice` were only used by that card, leave the shared styles in `Theme.xaml` alone (the Overlay page uses them).
- [ ] `SettingsView.xaml.cs`: delete `HotkeyBox_PreviewKeyDown` and the now unused `using System.Windows.Input;` and `using Torrid.Core.Overlay;` (keep `using Torrid.App.ViewModels;` only if still used; the build warns if not). Keep the constructor.
- [ ] `SettingsPageViewModel.cs`: remove the `OverlaySettingsViewModel? overlay = null` constructor parameter, the `Overlay = overlay;` line and the `public OverlaySettingsViewModel? Overlay { get; }` property (and any doc comment that mentions the overlay).
- [ ] `AppHost.cs`: change the `SettingsPage = new SettingsPageViewModel(...)` call to drop the last `OverlaySettings` argument. Keep the `OverlaySettings` property and its construction (used by the nav item and by `OverlayController`).
- [ ] `Grep` the repo for `\.Overlay\b`, `Overlay\.` and `SettingsPageViewModel.*Overlay` in `Torrid.App` and `Torrid.Tests`; fix any leftover reference (none are expected).

**Tests:** none new. Existing `SettingsPageViewModelTests` must pass unchanged.

**Acceptance check:** `dotnet build Torrid.sln` then `dotnet test Torrid.sln` (all green, no new warnings from unused usings).

---

### Task 4: Docs and visual check

**Goal:** README matches the new menu; the new page is looked at once.

**Files to modify:** `README.md`, `docs/anti-cheat.md` (only if it says the overlay is configured in Settings; run `Grep` for `Settings, Overlay` and `Overlay` there).

**Steps:**
- [ ] `README.md` line ~24: list pages as Home, Sensors, Thresholds, **Overlay** (shortcut, text size, colour, which sensors, and renaming them) and Settings (poll interval, repeat alerts, start minimized, anti-cheat behaviour, PawnIO driver status, test notification). Line ~70: change `under **Settings, Overlay**` to `on the **Overlay** page`. Line ~79: `Settings shows a message` -> `the Overlay page shows a message`. Line ~122: `--page <Home|Sensors|Thresholds|Overlay|Settings>`. Also say in the Sensors description that sensors can be renamed on the Sensors page and on the Overlay page (one name, used everywhere).
- [ ] Visual check, only if NO `Torrid.App` process is running (`Get-Process Torrid.App -ErrorAction SilentlyContinue`): read the header of `tools\Capture-Window.ps1`, then run `powershell -File tools\Capture-Window.ps1 -Launch -Out "$env:TEMP\overlay-page.png" -AppArgs @("--show","--demo","--page","Overlay","--config","$env:TEMP\torrid-overlay-page.json")`, open the PNG with the Read tool and check: sidebar has 5 items with Overlay highlighted; shortcut box, text size slider, colour list and the sensor rows with an editable name box are visible and not clipped; nothing overlaps. Repeat with `--page Settings` and check the Overlay card is gone. Delete the temp PNGs/config and make sure no `Torrid.App` process of yours remains (the script closes the one it launched). If a Torrid process was already running, skip this step and report that it was skipped.

**Tests:** none. **Acceptance check:** `dotnet build Torrid.sln` then `dotnet test Torrid.sln` (all green); report the visual findings in one short paragraph.
