# Torrid Sensor Overlay Implementation Plan

> **For agentic workers:** Follow the tasks in order, one at a time (no parallel tasks: they share `AppHost.cs`, `AppConfig.cs` and the Settings files). Steps use checkbox (`- [ ]`) syntax. Do not re-decide design; if something in this plan does not match the code you find, stop and report instead of improvising.

**Goal:** An on-screen overlay (game HUD) in the top-right corner of the primary screen that lists chosen sensor temperatures, toggled on and off by a global hotkey (default Ctrl+Alt+O). The user can change the hotkey, the text size, the text colour and which sensors are listed, all on the Settings page.

**User request (translated):** overlay with sensor temperatures; a keybind toggles it; it sits top-right; the user can customise text size, text colour and which sensors are shown.

## Choices made (tell the planner if you want any changed)

1. **Hotkey = Win32 `RegisterHotKey` on a message-only window** (`HwndSource` with parent `HWND_MESSAGE`), never a keyboard hook (a hook looks like cheat software). `MOD_NOREPEAT` is set. If registration fails (key taken) Settings shows a message; nothing crashes and the previous hotkey stays unregistered.
2. **Hotkey is stored as one string in `AppConfig.OverlayHotkey`** (`"Ctrl+Alt+O"`), parsed into `Hotkey(Modifiers, VirtualKey)`. Same information as modifiers + key, human-editable in `config.json`, trivially unit-testable. Supported keys: A-Z, 0-9, F1-F24; at least one modifier is required (a bare key would steal normal typing).
3. **Which sensors = a new per-sensor `ShowInOverlay` preference**, exactly like `ShowInWindow` / `ShowInTray` (`bool?`, null = default). Default = the tray default (`SensorDefaults.ShownInTray`: CPU package and GPU hot spot). The checklist lives in Settings (as requested) and lists the same rows as the Sensors page (`SensorRows.Build`).
4. **Text size = integer slider 10..48, default 18** (same control as the poll interval slider). **Text colour = 6 presets** (White default, Green, Cyan, Yellow, Orange, Red) shown as a `RadioCardListStyle` list, stored as `#RRGGBB`. A free colour picker was rejected: WPF has none built in. One colour for all lines (not per temperature zone: not asked for).
5. **The overlay is hidden at every start** (visibility is not persisted). Only the hotkey shows it.
6. **Overlay shows only data already available**: `ISensorSource.CurrentReadings` + `IHardwareStatusSource.Status`. It opens no hardware and changes no access behaviour. While monitoring is Paused for an anti-cheat, lines read `paused` (same as the tray tooltip).
7. **Window:** WPF `Window`, `Topmost`, `ShowInTaskbar=False`, `WindowStyle=None`, `AllowsTransparency=True`, `ShowActivated=False`, `SizeToContent=WidthAndHeight`, with `WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` set in `OnSourceInitialized` (click-through, never takes focus, not in Alt-Tab). A translucent dark rounded panel (`#99000000`) sits behind the text so it stays readable on bright scenes.
8. **Position = primary screen work area, top-right, 16 px margin.** The pure maths is `OverlayPlacement.TopRight` (pixels in, pixels out). The window converts with `VisualTreeHelper.GetDpi` because the app is PerMonitorV2 DPI aware (see `app.manifest`). Other monitors are not handled (not asked for).
9. **Threading:** all overlay fields are read on the UI thread only (via `UiThread.Post`), so the poll-thread rule from `docs/superpowers/plans/2026-10-12-torrid-phase6-per-sensor-thresholds.md` is unaffected. Config is mutated only through `ConfigManager.Update`.
10. **Settings wiring:** a new `OverlaySettingsViewModel` is passed to `SettingsPageViewModel` through a new OPTIONAL last constructor parameter, so the existing test constructions keep compiling.

## Known limits (state them in the README, do not try to fix them)

- Exclusive-fullscreen games draw over every other window, including a topmost one; the overlay only shows in windowed and borderless-windowed modes.
- The hotkey fails to register if another program already owns it; the user picks another.
- Anti-cheat: a visible overlay window and `RegisterHotKey` are ordinary Win32; Torrid injects nothing and hooks nothing.

## Global Constraints

- dotnet is at `"C:\Program Files\dotnet\dotnet.exe"` (PowerShell: `& "C:\Program Files\dotnet\dotnet.exe" build Torrid.sln`). Run `dotnet build Torrid.sln` and `dotnet test Torrid.sln` after EACH task; both must be green before moving on.
- **Do NOT commit and do NOT publish.** Leave all changes uncommitted. The working tree already has unrelated uncommitted edits (README.md, MainWindow.xaml, Torrid.App.csproj, SettingsPageViewModel.cs, ThresholdsView.xaml, AppConfig.cs, JsonConfigStore.cs, SensorService.cs, several tests, docs/anti-cheat.md, `Torrid.App/Assets/`). Do not revert or reformat them; make your edits on top.
- Do NOT run real-hardware scans. To run the app use `--demo --show` with a throwaway `--config` in `%TEMP%`. Leave no `Torrid.App` process running. Never launch `publish\Torrid.App.exe`. If a run re-created `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Torrid (Dev).lnk`, delete it.
- No new projects, no new NuGet packages. P/Invoke only `user32` (`RegisterHotKey`, `UnregisterHotKey`, `GetWindowLongPtr`, `SetWindowLongPtr`).
- Pure logic goes in `Torrid.Core` (namespace `Torrid.Core.Overlay`, folder `Torrid.Core/Overlay/`). WPF code goes in `Torrid.App/Overlay/`.
- Write the tests first for Tasks 1-2 and 5, see them fail (compile failure counts), then implement.
- All existing tests keep passing.

## Review Focus

1. No keyboard hook anywhere; the hotkey is `RegisterHotKey` only, and it is unregistered on Dispose and before every re-register.
2. The overlay never takes focus and never intercepts the mouse.
3. The overlay reads no hardware: only `CurrentReadings` / `Status`.
4. A failed registration shows a Settings message and clears when a later registration succeeds.
5. Invalid values in `config.json` (hotkey, size, colour) fall back to defaults and never throw.

---

### Task 1: Pure overlay logic in Torrid.Core (hotkey, appearance, placement)

**Goal:** The testable maths and parsing, no wiring yet.

**Files to create:**
- `Torrid.Core/Overlay/Hotkey.cs`
- `Torrid.Core/Overlay/OverlayAppearance.cs`
- `Torrid.Core/Overlay/OverlayPlacement.cs`
- `Torrid.Tests/HotkeyTests.cs`
- `Torrid.Tests/OverlayAppearanceTests.cs`
- `Torrid.Tests/OverlayPlacementTests.cs`

**Interfaces (namespace `Torrid.Core.Overlay`, file-scoped, same style as `Torrid.Core/Config/*`):**
```csharp
[Flags] public enum HotkeyModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Win = 8 } // values equal the Win32 MOD_* flags

public readonly record struct Hotkey(HotkeyModifiers Modifiers, int VirtualKey)
{
    public static Hotkey Default { get; } = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4F); // Ctrl+Alt+O
    public bool IsValid => Modifiers != HotkeyModifiers.None && IsSupportedKey(VirtualKey);
    public static bool IsSupportedKey(int vk); // 0x30-0x39 (0-9), 0x41-0x5A (A-Z), 0x70-0x87 (F1-F24)
    public string Format();                    // "Ctrl+Alt+O": modifiers in the order Ctrl, Alt, Shift, Win; key as "O", "5" or "F9"
    public static bool TryParse(string? text, out Hotkey hotkey); // false for null/blank/invalid; never throws
}

public static class OverlayAppearance
{
    public const int MinTextSize = 10, MaxTextSize = 48, DefaultTextSize = 18;
    public const string DefaultColor = "#FFFFFF";
    public static int ClampTextSize(int size);          // Math.Clamp
    public static bool IsValidColor(string? hex);       // exactly #RRGGBB (hex digits, either case)
    public static IReadOnlyList<(string Name, string Hex)> Presets { get; } // White #FFFFFF, Green #4ED36B, Cyan #4FD8EB, Yellow #FFC93C, Orange #FF9F43, Red #FF5C4D
}

public static class OverlayPlacement
{
    /// Top-right inside the work area: Left = workLeft + workWidth - windowWidth - margin (never below workLeft), Top = workTop + margin.
    public static (double Left, double Top) TopRight(double workLeft, double workTop, double workWidth, double windowWidth, double margin);
}
```
`TryParse` rules: split on `+`, trim each part, case-insensitive; modifier tokens `ctrl`/`control`, `alt`, `shift`, `win`; the LAST token is the key (single letter or digit, or `F1`..`F24`); every earlier token must be a modifier (duplicates allowed); the result must satisfy `IsValid`.

**Tests to write (exact names):**
- `HotkeyTests.Default_FormatsAsCtrlAltO`
- `HotkeyTests.Format_OrdersModifiersCtrlAltShiftWin` (Win+Shift+Alt+Control+F9 -> `"Ctrl+Alt+Shift+Win+F9"`)
- `HotkeyTests.TryParse_RoundTripsFormat` (Theory over `Ctrl+Alt+O`, `Shift+F12`, `Ctrl+5`, `Win+Alt+Z`)
- `HotkeyTests.TryParse_IsCaseAndSpaceInsensitive` (`" ctrl + ALT + o "` equals Default)
- `HotkeyTests.TryParse_Rejects` (Theory: `null`, `""`, `"O"` (no modifier), `"Ctrl+"`, `"Ctrl+Alt"`, `"Ctrl+OO"`, `"Ctrl+F25"`, `"Ctrl+O+Alt"`, `"Foo+O"`, `"Ctrl+;"`) returns false
- `HotkeyTests.IsValid_RequiresAModifierAndASupportedKey`
- `OverlayAppearanceTests.ClampTextSize_KeepsTheRange` (9 -> 10, 49 -> 48, 20 -> 20)
- `OverlayAppearanceTests.IsValidColor_AcceptsOnlySixDigitHex` (`#FFFFFF`, `#ff00aa` true; `FFFFFF`, `#FFF`, `#GGGGGG`, `""`, `null`, `red` false)
- `OverlayAppearanceTests.Presets_AreAllValidColors_AndIncludeTheDefault`
- `OverlayPlacementTests.TopRight_HugsTheRightEdgeWithTheMargin` (work 0,0,1920 wide, window 200, margin 16 -> Left 1704, Top 16)
- `OverlayPlacementTests.TopRight_HonoursTheWorkAreaOffset` (workLeft 100, workTop 40)
- `OverlayPlacementTests.TopRight_WiderThanTheWorkArea_ClampsToTheLeftEdge`

**Acceptance check:** `dotnet build Torrid.sln` then `dotnet test Torrid.sln` (all green, only the new tests added).

---

### Task 2: Config fields, per-sensor selection and the overlay lines

**Goal:** Persisted settings, sensor selection and the text the overlay shows, still pure.

**Files to modify:** `Torrid.Core/Config/AppConfig.cs`, `Torrid.Core/Config/SensorPreference.cs`, `Torrid.Core/Config/SensorDefaults.cs`, `Torrid.Core/Config/SensorDisplay.cs`, `Torrid.Core/Config/JsonConfigStore.cs`.
**Files to create:** `Torrid.Core/Overlay/OverlayLines.cs`, `Torrid.Tests/OverlayLinesTests.cs`. **Tests to extend:** `Torrid.Tests/JsonConfigStoreTests.cs`, `Torrid.Tests/SensorDisplayTests.cs`, `Torrid.Tests/SensorPreferencesTests.cs`, `Torrid.Tests/SensorDefaultsTests.cs`.

**Steps:**
- [ ] `AppConfig`: add `using Torrid.Core.Overlay;` and
  ```csharp
  public string OverlayHotkey { get; set; } = "Ctrl+Alt+O";
  public int OverlayTextSize { get; set; } = OverlayAppearance.DefaultTextSize;
  public string OverlayTextColor { get; set; } = OverlayAppearance.DefaultColor;
  ```
- [ ] `SensorPreference`: add `public bool? ShowInOverlay { get; set; }` and include `&& ShowInOverlay is null` in `IsEmpty`.
- [ ] `SensorDefaults`: add `public static bool ShownInOverlay(SensorKind kind) => ShownInTray(kind);`.
- [ ] `SensorDisplay`: add `IsShownInOverlay(AppConfig, SensorIdentity)` (`Preference(...)?.ShowInOverlay ?? SensorDefaults.ShownInOverlay(kind)`) and the `SensorReading` overload, next to the Tray ones.
- [ ] `JsonConfigStore.Normalize` (before `return config`): `if (!Hotkey.TryParse(config.OverlayHotkey, out _)) config.OverlayHotkey = new AppConfig().OverlayHotkey;` `config.OverlayTextSize = OverlayAppearance.ClampTextSize(config.OverlayTextSize);` `if (!OverlayAppearance.IsValidColor(config.OverlayTextColor)) config.OverlayTextColor = OverlayAppearance.DefaultColor;` (add `using Torrid.Core.Overlay;`).
- [ ] `OverlayLines` (namespace `Torrid.Core.Overlay`):
  ```csharp
  public static class OverlayLines
  {
      public const string NoSensorsText = "No sensors selected";
      public static IReadOnlyList<string> Build(AppConfig config, IReadOnlyList<SensorReading> readings, HardwareStatus status);
  }
  ```
  Keeps the readings that are non-null and `SensorDisplay.IsShownInOverlay(config, r)`, in the order given. Line = `$"{SensorDisplay.ResolveLabel(config, r)}: {value}"` where value is `"paused"` when `status.Level == HardwareAccessLevel.Paused`, else `$"{v:F0}°C"` (same format as `TooltipTextBuilder`), else `"--"` for a null value. If nothing is selected return a single line `NoSensorsText`.

**Tests to write (exact names):**
- `JsonConfigStoreTests.OverlaySettings_HaveDefaults` (new `AppConfig`: `"Ctrl+Alt+O"`, 18, `"#FFFFFF"`)
- `JsonConfigStoreTests.OverlaySettings_SurviveSaveAndLoad` (custom hotkey `Shift+F9`, size 30, `#FF9F43`, one `ShowInOverlay = true` preference)
- `JsonConfigStoreTests.Normalize_FixesBadOverlayValues` (hotkey `"nonsense"`, size 500, colour `"red"` -> defaults / 48 / `#FFFFFF`)
- `SensorDisplayTests.IsShownInOverlay_DefaultsToTheTrayDefault_AndHonoursThePreference` (CpuPackage true, GpuCore false by default; explicit false/true override)
- `SensorPreferencesTests.ShowInOverlayAlone_KeepsThePreference_AndClearingItRemovesTheEntry`
- `SensorDefaultsTests.ShownInOverlay_MatchesShownInTray_ForEveryKind`
- `OverlayLinesTests.Build_ListsOnlyOverlaySensors_InReadingOrder_WithResolvedLabels` (use a custom `Label` preference)
- `OverlayLinesTests.Build_FormatsRoundedCelsius_AndDashesForNull` (`"CPU: 59°C"`, `"--"`)
- `OverlayLinesTests.Build_WhenPaused_SaysPaused`
- `OverlayLinesTests.Build_WhenNothingSelected_ReturnsTheNoSensorsLine`
- `OverlayLinesTests.Build_IgnoresNullReadings`

**Acceptance check:** `dotnet build Torrid.sln` then `dotnet test Torrid.sln` (all green).

---

### Task 3: The overlay window (no hotkey yet)

**Goal:** A click-through, no-focus, top-right window that can be given lines, size and colour.

**Files to create:** `Torrid.App/Overlay/OverlayWindow.xaml`, `Torrid.App/Overlay/OverlayWindow.xaml.cs`.

**Steps:**
- [ ] XAML: root `<Window x:Class="Torrid.App.Overlay.OverlayWindow" WindowStyle="None" AllowsTransparency="True" Background="Transparent" Topmost="True" ShowInTaskbar="False" ShowActivated="False" Focusable="False" ResizeMode="NoResize" SizeToContent="WidthAndHeight" Title="Torrid overlay">` containing `<Border Background="#99000000" CornerRadius="6" Padding="10,6"><TextBlock x:Name="LinesText" FontFamily="Segoe UI" FontWeight="SemiBold" TextWrapping="NoWrap"/></Border>`. Namespace `Torrid.App.Overlay` (like `Torrid.App.Views`).
- [ ] Code-behind, `public partial class OverlayWindow : Window`:
  - `public void Apply(IReadOnlyList<string> lines, int textSize, string colorHex)`: `LinesText.Text = string.Join("\n", lines)`; `LinesText.FontSize = OverlayAppearance.ClampTextSize(textSize)`; `LinesText.Foreground` = a frozen `SolidColorBrush` from `(Color)ColorConverter.ConvertFromString(colorHex)` when `OverlayAppearance.IsValidColor(colorHex)`, else white; then `Reposition()`.
  - `protected override void OnSourceInitialized`: call base, then `var hwnd = new WindowInteropHelper(this).Handle; var style = GetWindowLongPtr(hwnd, -20); SetWindowLongPtr(hwnd, -20, style | 0x20 | 0x80000 | 0x08000000 | 0x80);` (`WS_EX_TRANSPARENT 0x20`, `WS_EX_LAYERED 0x80000`, `WS_EX_NOACTIVATE 0x08000000`, `WS_EX_TOOLWINDOW 0x80`) via `[DllImport("user32.dll")] GetWindowLongPtrW` / `SetWindowLongPtrW` (`nint` args), and subscribe `SizeChanged += (_, _) => Reposition();`.
  - `private void Reposition()`: `var area = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea` (null -> return); `var scale = VisualTreeHelper.GetDpi(this).DpiScaleX`; `var (left, top) = OverlayPlacement.TopRight(area.Left, area.Top, area.Width, ActualWidth * scale, 16 * scale); Left = left / scale; Top = top / scale;`. Use fully qualified `System.Windows.Forms` (the project references both WPF and WinForms; do not add a `using` that makes `Window`/`Color` ambiguous).
- [ ] This task does not hook the window to anything; nothing in `AppHost` changes.

**Tests:** none (WPF visual). **Acceptance check:** `dotnet build Torrid.sln` and `dotnet test Torrid.sln` green. Visual check is done in Task 6.

---

### Task 4: Hotkey host, overlay controller and AppHost wiring

**Goal:** The hotkey toggles the overlay; the overlay follows readings, status and config.

**Files to create:** `Torrid.App/Overlay/HotkeyHost.cs`, `Torrid.App/Overlay/OverlayController.cs`. **Files to modify:** `Torrid.App/AppHost.cs`.

**Interfaces:**
```csharp
namespace Torrid.App.Overlay;

internal sealed class HotkeyHost : IDisposable
{
    public HotkeyHost();                          // creates an HwndSource: new HwndSourceParameters("TorridHotkey") { ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */, WindowStyle = 0 }, AddHook(WndProc)
    public event EventHandler? Pressed;           // raised for WM_HOTKEY (0x0312) with wParam == the single id used (1)
    public bool TryRegister(Hotkey hotkey);       // Unregister() first; RegisterHotKey(hwnd, 1, (uint)hotkey.Modifiers | 0x4000 /*MOD_NOREPEAT*/, (uint)hotkey.VirtualKey); false when it fails (never throws)
    public void Unregister();
    public void Dispose();                        // Unregister + dispose the HwndSource
}

public sealed class OverlayController : IDisposable
{
    public OverlayController(ISensorSource sensors, IHardwareStatusSource hardware, ConfigManager config, Action<string?> reportHotkeyError);
    public void Start();     // register the hotkey (from config), subscribe to events
    public void Toggle();    // visible -> Hide(); hidden -> Refresh() then Show()
    public void Dispose();
}
```
**Controller behaviour:**
- Owns one `OverlayWindow` (created lazily on first `Toggle`, `Show()` only from `Toggle`; `Hide()` to turn off; the window is never closed until `Dispose`) and one `HotkeyHost` (`Pressed += (_, _) => Toggle()`).
- `Refresh()` (no-op unless the window is visible): `window.Apply(OverlayLines.Build(config, sensors.CurrentReadings, hardware.Status), config.OverlayTextSize, config.OverlayTextColor)`.
- Subscriptions (all marshalled with `UiThread.Post`, with a `_disposed` guard like `TrayIconGroupManager`): `sensors.ReadingsUpdated` -> `Refresh`; `hardware.StatusChanged` -> `Refresh`; `config.Changed` -> `OnConfigChanged`.
- `OnConfigChanged`: if `config.OverlayHotkey` differs from the text last registered, `RegisterFromConfig()`; then `Refresh()`.
- `RegisterFromConfig()`: `Hotkey.TryParse(config.OverlayHotkey, out var hk)` else `Hotkey.Default`; remember the text; `if (host.TryRegister(hk)) reportHotkeyError(null) else reportHotkeyError($"{hk.Format()} is already used by another program. Choose a different shortcut.")`.
- Exceptions from the overlay window must not escape event handlers: wrap `Refresh` in try/catch with `Debug.WriteLine` (house style in `AppHost.Dispose`).

**AppHost changes:** create `OverlaySettings` first (see Task 5; for THIS task pass a no-op `_ => { }` as `reportHotkeyError`), build `Overlay = new OverlayController(Sensors, Hardware, ConfigManager, _ => { })` after `Tray`; `Start()` also calls `Overlay.Start()`; `Dispose()` disposes it in its own try/catch like `Tray`. Task 5 replaces the no-op.

**Tests:** none (Win32 and WPF; every decision they make is already covered by the Task 1-2 tests). **Acceptance check:** `dotnet build Torrid.sln` and `dotnet test Torrid.sln` green; then a manual smoke test: `Torrid.App\bin\Debug\net8.0-windows10.0.17763.0\Torrid.App.exe --demo --show --config %TEMP%\torrid-overlay.json`, press Ctrl+Alt+O twice: the overlay appears top-right with demo CPU and GPU lines and disappears again; clicks pass through it; the main window keeps focus. Kill the process and delete the temp config afterwards.

---

### Task 5: Settings view model for the overlay

**Goal:** Everything the Settings page binds to, fully unit-tested.

**Files to create:** `Torrid.App/ViewModels/OverlaySettingsViewModel.cs`, `Torrid.Tests/OverlaySettingsViewModelTests.cs`. **Files to modify:** `Torrid.App/ViewModels/SettingsPageViewModel.cs`, `Torrid.App/AppHost.cs`.

**Interfaces:**
```csharp
namespace Torrid.App.ViewModels;

public sealed record OverlayColorChoice(string Name, string Hex);

public sealed class OverlaySensorItem : ObservableObject      // one checkbox row
{
    public string Id { get; }
    public string Label { get; private set; }                  // SensorDisplay.ResolveLabel
    public string DeviceTitle { get; private set; }            // SensorRow.Group.Title
    public bool IsShown { get; set; }                          // setter: stored = value == SensorDefaults.ShownInOverlay(kind) ? (bool?)null : value; _config.Update(c => SensorPreferences.Update(c, Id, p => p.ShowInOverlay = stored)); then Set(...)  (same pattern as SensorListItemViewModel.ShowInTray)
}

public sealed class OverlaySettingsViewModel : ObservableObject
{
    public OverlaySettingsViewModel(ConfigManager config, ISensorSource sensors);
    public string HotkeyText { get; }                          // Hotkey.TryParse(config.OverlayHotkey) ?? Default, formatted
    public bool TrySetHotkey(Hotkey hotkey);                   // false (no change) when !hotkey.IsValid; else Update(c => c.OverlayHotkey = hotkey.Format()), Raise(HotkeyText), true
    public string? HotkeyError { get; }                        // plus bool HasHotkeyError
    public void SetHotkeyError(string? message);               // called by OverlayController through AppHost
    public int TextSize { get; set; }                          // clamped with OverlayAppearance.ClampTextSize, written only when changed
    public string TextColor { get; set; }                      // ignored when !IsValidColor, written only when changed
    public IReadOnlyList<OverlayColorChoice> ColorChoices { get; } // from OverlayAppearance.Presets
    public ObservableCollection<OverlaySensorItem> Sensors { get; } // SensorRows.Build(config, sensors.CurrentReadings), instances reused by id, CollectionSync.Sync
}
```
Refresh on `config.Changed` (`UiThread.Post`) and `sensors.ReadingsUpdated` (`UiThread.Post`): raise `HotkeyText`, `TextSize`, `TextColor`, rebuild `Sensors`, and call `RefreshFromConfig()` on each item. App-lifetime singleton, so subscriptions are never removed (same note as `SettingsPageViewModel`).

**Other steps:**
- [ ] `SettingsPageViewModel`: add a last optional constructor parameter `OverlaySettingsViewModel? overlay = null` and `public OverlaySettingsViewModel? Overlay { get; }`. Nothing else changes.
- [ ] `AppHost`: `OverlaySettings = new OverlaySettingsViewModel(ConfigManager, Sensors)` (public property) created before `SettingsPage`; pass it as `overlay:`; replace the Task 4 no-op with `OverlaySettings.SetHotkeyError`.

**Tests to write (exact names, using the `FakeSensorSourceForTests` / `InMemoryConfigStore` helpers the SettingsPageViewModel tests already use; `UiThread.Post` runs inline in tests):**
- `OverlaySettingsViewModelTests.HotkeyText_DefaultsToCtrlAltO`
- `OverlaySettingsViewModelTests.TrySetHotkey_Valid_StoresTheFormattedString`
- `OverlaySettingsViewModelTests.TrySetHotkey_WithoutAModifier_IsRejectedAndNothingChanges`
- `OverlaySettingsViewModelTests.HotkeyError_SetAndClear_RaisesHasHotkeyError`
- `OverlaySettingsViewModelTests.TextSize_IsClampedAndStored` (5 -> 10, 99 -> 48)
- `OverlaySettingsViewModelTests.TextColor_ValidIsStored_InvalidIsIgnored`
- `OverlaySettingsViewModelTests.Sensors_ListsTheSensorsPageRows_WithTheTrayDefaultChecked` (CPU package and GPU hot spot checked, others not)
- `OverlaySettingsViewModelTests.Sensors_ToggleWritesOnlyShowInOverlay_AndBackToDefaultRemovesTheEntry`
- `OverlaySettingsViewModelTests.Sensors_NewReadings_AddRowsWithoutRecreatingExistingOnes` (`ReferenceEquals`)
- `SettingsPageViewModelTests` existing tests stay unchanged and green (the new parameter is optional).

**Acceptance check:** `dotnet build Torrid.sln` and `dotnet test Torrid.sln` green.

---

### Task 6: Settings page UI, docs and visual check

**Goal:** The Overlay card on Settings, README and anti-cheat note, screenshot check.

**Files to modify:** `Torrid.App/Views/SettingsView.xaml`, `Torrid.App/Views/SettingsView.xaml.cs`, `README.md`, `docs/anti-cheat.md`.

**Steps:**
- [ ] `SettingsView.xaml`: add a new `SectionCardStyle` card titled **Overlay** (`CardTitleStyle`) between the Notifications card and the Startup card. All bindings go through `Overlay.` (e.g. `{Binding Overlay.TextSize}`) and the card has `Visibility` unaffected (when `Overlay` is null the bindings just show nothing). Rows, each separated by `DividerStyle` and laid out like the existing Poll interval row (title + `CaptionTextStyle` caption on the left, control on the right):
  1. **Shortcut**: caption "Press this shortcut anywhere, even in a game, to show or hide the overlay. Click the box and press a new combination." Control: `<TextBox x:Name="HotkeyBox" Width="170" IsReadOnly="True" Text="{Binding Overlay.HotkeyText, Mode=OneWay}" AutomationProperties.Name="Overlay shortcut"/>`. Under the row a `TextBlock` with `Foreground="{StaticResource ErrorTextBrush}"`, `Text="{Binding Overlay.HotkeyError}"`, `Visibility="{Binding Overlay.HasHotkeyError, Converter={StaticResource BoolToVisibility}}"`.
  2. **Text size**: `Slider` with `DarkSliderStyle` `Width="200"` `Minimum="10" Maximum="48" TickFrequency="1" IsSnapToTickEnabled="True" SmallChange="1" LargeChange="4" Value="{Binding Overlay.TextSize}"` plus a `TextBlock` `Text="{Binding Overlay.TextSize}"` width 56 (copy the Poll interval row).
  3. **Text colour**: a `ListBox` with `Style="{StaticResource RadioCardListStyle}"` `ItemContainerStyle="{StaticResource RadioCardItemStyle}"` `ItemsSource="{Binding Overlay.ColorChoices}"` `SelectedValuePath="Hex"` `SelectedValue="{Binding Overlay.TextColor}"`; item template (`DataType OverlayColorChoice`) = horizontal `StackPanel` with an `Ellipse` 14x14 `Fill="{Binding Hex}"` (string-to-brush works in WPF bindings) and a `TextBlock` `Text="{Binding Name}"` `Margin="10,0,0,0"`. Reuse the `AntiCheatChoiceTemplate` layout as the model.
  4. **Sensors to show**: caption "Choose which sensors the overlay lists." `ItemsControl ItemsSource="{Binding Overlay.Sensors}"`, each row a `Grid` with the label (`Label`, and `DeviceTitle` in `CaptionTextStyle`) and a `CheckBox Style="{StaticResource ToggleSwitchStyle}" IsChecked="{Binding IsShown}" AutomationProperties.Name="{Binding Label, StringFormat='Show {0} in the overlay'}"`.
- [ ] `SettingsView.xaml.cs`: hook `HotkeyBox.PreviewKeyDown` (in the constructor after `InitializeComponent`, or from XAML). Handler: `e.Handled = true`; `var key = e.Key == Key.System ? e.SystemKey : e.Key;` ignore pure modifier keys (`LeftCtrl, RightCtrl, LeftAlt, RightAlt, LeftShift, RightShift, LWin, RWin`) and `Key.None`; build `HotkeyModifiers` from `Keyboard.Modifiers` (`Control`, `Alt`, `Shift`, `Windows`); `var vk = KeyInterop.VirtualKeyFromKey(key);` then `(DataContext as SettingsPageViewModel)?.Overlay?.TrySetHotkey(new Hotkey(mods, vk))`. Unsupported combinations are silently ignored (the box keeps showing the stored shortcut). Check how the existing code-behind obtains its DataContext before writing this and follow it.
- [ ] `README.md`: add to the Settings page list (first feature bullet) "overlay"; add a short **Overlay** section: what it is, default Ctrl+Alt+O, top-right, click-through, the three customisations, hidden at start, shows only readings Torrid already has, and the two known limits (exclusive fullscreen; hotkey already taken -> message in Settings). Add `Torrid.App/Overlay` to any project-layout list that names folders.
- [ ] `docs/anti-cheat.md`: one short paragraph: the overlay is an ordinary topmost window and the shortcut uses `RegisterHotKey`, no keyboard hook, no injection; it reads only existing readings and shows `paused` while monitoring is paused.
- [ ] Visual check: `dotnet build Torrid.sln`, then `powershell -File tools\Capture-Window.ps1 -Launch -Out %TEMP%\settings.png -AppArgs @("--show","--demo","--page","Settings","--config","$env:TEMP\torrid-overlay.json")` (read the script header for exact usage). Open the PNG with Read; check the Overlay card is not clipped at the default size and at the minimum width. Then the manual hotkey test from Task 4 with a changed text size and colour (edit them in the Settings page by hand) and a second sensor ticked. Kill any `Torrid.App` process, delete the temp config and the Dev `.lnk` if created.

**Tests:** none new (view model is covered by Task 5). **Acceptance check:** `dotnet build Torrid.sln` and `dotnet test Torrid.sln` green; screenshot reviewed.

---

### Final verification (controller)
Review the whole diff once for Review Focus items 1-5. Run `dotnet build Torrid.sln` and `dotnet test Torrid.sln`. Manually: start the real app, press Ctrl+Alt+O with a windowed game or any maximized window in front, confirm the overlay is top-right, readable, not clickable and does not steal focus; set the shortcut to one another program owns and confirm the Settings message. Nothing is committed or published.
