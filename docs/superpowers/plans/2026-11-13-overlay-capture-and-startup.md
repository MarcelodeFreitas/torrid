# Overlay Hidden From Capture and Start With Windows Implementation Plan

> **For agentic workers:** Follow the tasks in order, one at a time (they share `OverlaySettingsViewModel`, `SettingsPageViewModel`, `AppHost.cs` and `README.md`). Steps use checkbox (`- [ ]`) syntax. Do not re-decide design; if something in this plan does not match the code you find, stop and report instead of improvising.

**Goal:** (A) The sensor overlay is excluded from screen sharing and recording by default, with a checkbox on the Overlay page to turn that off. (B) A "Start with Windows" switch in Settings > Startup (default OFF) that registers Torrid to launch at logon without a UAC prompt.

## Choices made (tell the planner if you want any changed)

**A. Hidden from capture**

1. New `AppConfig.OverlayHiddenFromCapture`, default `true`. Old config files without the key load as `true` (property initialiser), so existing users get the safe default.
2. The only pure logic is the flag-to-constant mapping. It goes in `Torrid.Core/Overlay/OverlayCapture.cs`: `DisplayAffinity(bool hidden)` returns `0x11` (`WDA_EXCLUDEFROMCAPTURE`) when hidden, `0x0` (`WDA_NONE`) when not. The P/Invoke itself stays in `OverlayWindow.xaml.cs`.
3. `OverlayWindow.SetHiddenFromCapture(bool)` calls `new WindowInteropHelper(this).EnsureHandle()` first, so it works before the first `Show()` and again live whenever the config changes. `OverlayController.Refresh()` already runs before `Show()` (in `Toggle`) and on every config change, so it calls this one method; no other controller change. The return value of `SetWindowDisplayAffinity` is ignored (Windows older than 10 2004 does not support `0x11`; the overlay then simply stays visible in captures, no crash).
4. The Overlay page gets one checkbox row (same `ToggleSwitchStyle` row layout as the other cards), bound to `OverlaySettingsViewModel.HiddenFromCapture`.

**B. Start with Windows**

5. **Mechanism: a Task Scheduler task** (trigger: at logon of the current user; run level: Highest), because the exe's manifest is `requireAdministrator` and an `HKCU\...\Run` entry would raise a UAC prompt at every login (and Windows skips elevated Run entries silently on some setups). Created by shelling out to `schtasks.exe /Create /XML <tempfile>` (no new NuGet package, no COM interop). XML (not command-line flags) because `schtasks /Create` flags give a task that stops after 72 hours and does not start on battery; the XML sets `ExecutionTimeLimit` to `PT0S`, `DisallowStartIfOnBatteries` and `StopIfGoingOnBatteries` to `false`.
6. **Task name depends on the build** via `AppIdentity.StartupTaskName` (`"Torrid (Dev)"` in Debug, `"Torrid"` in Release), same `#if DEBUG` pattern as `Aumid` and `ShortcutName`. A Debug build can never overwrite or delete the real release task.
7. **The scheduled task is the source of truth, not the config file.** The switch state is read from `schtasks /Query /TN <name>` (exit code 0 = registered). No new `AppConfig` field. Reason: the user can delete the task in Task Scheduler, and a config flag would then lie.
8. **The registration is behind a small interface**, `IStartupRegistration { bool IsEnabled(); string? SetEnabled(bool enable); }` (returns `null` on success, otherwise a short message for the UI). Real implementation: `TaskSchedulerStartup`. Tests use a fake and never run `schtasks`. `SettingsPageViewModel` gets the interface as a new required constructor parameter (3 test constructor calls are updated).
9. **Failure handling:** `SetEnabled` never throws (catches everything, returns a message, and treats a non-zero `schtasks` exit as failure with its stderr text trimmed). Debug builds run non-elevated, so creating a Highest-run-level task will usually fail there with "Access is denied"; the view model then leaves the switch where it was (re-raises `StartWithWindows`), and shows the message in a red caption under the row. No retry, no fallback to a Run key (that would reintroduce the UAC prompt at login).
10. **`StartMenuShortcut.cs` as a model:** only partly useful. Copy its conventions (static class in `Torrid.App`, `AppIdentity` supplies the Debug/Release name, never throws, `Debug.WriteLine` on failure, small `Outcome` style). Do not copy its COM interop. It also shows a pure-builder + thin-wrapper split is how the repo tests this kind of thing (`StartMenuShortcutTests` checks the pure parts and never writes the real shortcut).
11. The task launches the exe with no arguments, so `StartMinimizedToTray` (default true) decides whether a window shows at login. No extra `--minimized` flag.
12. Known limit, accepted: if the user moves the exe, the task still points at the old path until the switch is toggled off and on (`SetEnabled(true)` uses `/F`, so it overwrites). Add a code comment `shortcut: path is captured when the switch is turned on`.

## Files

**Create**
- `Torrid.Core/Overlay/OverlayCapture.cs`
- `Torrid.App/Startup/IStartupRegistration.cs` (interface only)
- `Torrid.App/Startup/TaskSchedulerStartup.cs` (the real implementation and `BuildTaskXml`)
- `Torrid.Tests/OverlayCaptureTests.cs`
- `Torrid.Tests/TaskSchedulerStartupTests.cs`

**Modify**
- `Torrid.Core/Config/AppConfig.cs` (one property)
- `Torrid.App/Overlay/OverlayWindow.xaml.cs`, `Torrid.App/Overlay/OverlayController.cs`
- `Torrid.App/ViewModels/OverlaySettingsViewModel.cs`, `Torrid.App/Views/OverlayView.xaml`
- `Torrid.App/AppIdentity.cs`
- `Torrid.App/ViewModels/SettingsPageViewModel.cs`, `Torrid.App/Views/SettingsView.xaml`, `Torrid.App/AppHost.cs`
- `Torrid.Tests/OverlaySettingsViewModelTests.cs`, `Torrid.Tests/SettingsPageViewModelTests.cs`, `Torrid.Tests/ConfigManagerTests.cs` or `Torrid.Tests/JsonConfigStoreTests.cs` (one default test, see Task 1)
- `README.md`

Do not touch other files. The working tree has many unrelated uncommitted edits (several of these files are among them): make small edits on top, never revert or reformat.

## Global Constraints

- dotnet is at `"C:\Program Files\dotnet\dotnet.exe"`. Build: `& "C:\Program Files\dotnet\dotnet.exe" build`. Tests: `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests`. Both must be green after EACH task before moving on.
- **Do NOT commit and do NOT publish.** Leave all changes uncommitted.
- Do NOT run real hardware scans. Never launch `publish\Torrid.App.exe`. Leave no `Torrid.App` process running (if one is ALREADY running, do not start a second copy; skip the live check and say so). Any app run must use `--demo` with a throwaway `--config` in `%TEMP%`. If a run re-created `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Torrid (Dev).lnk`, delete it.
- **No test or run may create a real scheduled task.** Only `TaskSchedulerStartup` touches `schtasks`, and no test constructs it except for the pure `BuildTaskXml`. Do not toggle the new switch in a live run (a Debug run would try to create the task named `Torrid (Dev)`); if any real task named `Torrid` or `Torrid (Dev)` exists afterwards, report it, do not delete it silently.
- No new projects or NuGet packages. Reuse existing styles (`SectionCardStyle`, `SettingTitleStyle`, `CaptionTextStyle`, `DividerStyle`, `ToggleSwitchStyle`).
- Minimal change: no options beyond the two requests. Tests first for each new piece of logic: write them, see them fail (a compile failure counts), then implement.
- All existing tests keep passing.

## Review Focus

1. A fresh config (and an old one with no such key) has `OverlayHiddenFromCapture == true`; the affinity is set before the first `Show()` and changes live from the checkbox.
2. The Debug task name can never equal the Release task name.
3. `SetEnabled` cannot throw; a failure leaves the switch where it was and shows a message.
4. No test path can run `schtasks`.

---

### Task 1: Config flag and pure affinity mapping (A, core)

**Files to create:** `Torrid.Core/Overlay/OverlayCapture.cs`, `Torrid.Tests/OverlayCaptureTests.cs`. **Files to modify:** `Torrid.Core/Config/AppConfig.cs`, `Torrid.Tests/JsonConfigStoreTests.cs`.

**Tests first (exact names):**
- [ ] `OverlayCaptureTests.DisplayAffinity_Hidden_IsExcludeFromCapture` (`OverlayCapture.DisplayAffinity(true) == 0x11`)
- [ ] `OverlayCaptureTests.DisplayAffinity_NotHidden_IsNone` (`DisplayAffinity(false) == 0`)
- [ ] `OverlayCaptureTests.AppConfig_DefaultsToHiddenFromCapture` (`new AppConfig().OverlayHiddenFromCapture` is true)
- [ ] `JsonConfigStoreTests.Load_OldFileWithoutTheCaptureKey_DefaultsToHidden` (follow the existing style in that file for loading JSON text with other overlay keys but no `OverlayHiddenFromCapture`; assert true; also a file with `"OverlayHiddenFromCapture": false` loads as false, if the file's existing helper makes that a one-liner)

**Implementation:**
- [ ] `OverlayCapture` (static, namespace `Torrid.Core.Overlay`): `public static uint DisplayAffinity(bool hidden) => hidden ? 0x11u : 0u;` with a one-line comment naming `WDA_EXCLUDEFROMCAPTURE`.
- [ ] `AppConfig`: after `OverlayTextColor` add `public bool OverlayHiddenFromCapture { get; set; } = true;`.

**Acceptance:** build, then `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests` (all green).

---

### Task 2: View model and Overlay page checkbox (A, UI model)

**Files to modify:** `Torrid.App/ViewModels/OverlaySettingsViewModel.cs`, `Torrid.App/Views/OverlayView.xaml`, `Torrid.Tests/OverlaySettingsViewModelTests.cs`.

**Tests first (use the existing `Make()` helper in that file):**
- [ ] `HiddenFromCapture_DefaultsToTrue`
- [ ] `HiddenFromCapture_Set_WritesTheConfig` (set false: `config.Config.OverlayHiddenFromCapture == false`)
- [ ] `HiddenFromCapture_ComesBackFromTheConfig` (write the config directly, as the other tests in the file do for text size, then check the property)

**Implementation:**
- [ ] `OverlaySettingsViewModel`: add `HiddenFromCapture` next to `TextColor`, same shape as `TextSize` (read `_config.Config`, write only when changed via `_config.Update`, then `Raise(nameof(HiddenFromCapture))`). Add `Raise(nameof(HiddenFromCapture));` to `Refresh()`.
- [ ] `OverlayView.xaml`: add a `Border Style="{StaticResource DividerStyle}"` and a row with the exact two-column layout used by "Text size" (left `StackPanel`: title `Hide from screen sharing and recording`, caption `The overlay will not show in captures, streams or screenshots. Turn off to show it there.`; right `CheckBox Style="{StaticResource ToggleSwitchStyle}" IsChecked="{Binding HiddenFromCapture}" AutomationProperties.Name="Hide the overlay from screen sharing and recording"`). Place it directly after the text colour list, before "Sensors to show".

**Acceptance:** build, then `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests`.

---

### Task 3: Apply the affinity to the overlay window (A, wiring)

No unit test (needs a real HWND; the mapping is covered in Task 1). **Files to modify:** `Torrid.App/Overlay/OverlayWindow.xaml.cs`, `Torrid.App/Overlay/OverlayController.cs`.

- [ ] `OverlayWindow`: add `[DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint hWnd, uint dwAffinity);` and
  `public void SetHiddenFromCapture(bool hidden) => SetWindowDisplayAffinity(new WindowInteropHelper(this).EnsureHandle(), OverlayCapture.DisplayAffinity(hidden));` (comment: result ignored; Windows older than 10 2004 does not support it).
- [ ] `OverlayController.Refresh()`: inside the existing `try`, before `_window.Apply(...)`, add `_window.SetHiddenFromCapture(config.OverlayHiddenFromCapture);`. This runs before the first `Show()` and on every config change; the existing `try/catch` already keeps a failure from crashing.
- [ ] Check with `Grep` that `Refresh()` is the only place the window is configured (so nothing else needs the call).

**Acceptance:** build, then `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests`. Live check is optional and demo-only: `--demo --page Overlay --config "$env:TEMP\torrid-capture\config.json"`, press the checkbox, look for exceptions only; a real capture test (screen recorder or Snipping Tool showing the overlay absent) is a manual step for the user. Stop the process afterwards.

---

### Task 4: Startup registration (B, logic)

**Files to create:** `Torrid.App/Startup/IStartupRegistration.cs`, `Torrid.App/Startup/TaskSchedulerStartup.cs`, `Torrid.Tests/TaskSchedulerStartupTests.cs`. **Files to modify:** `Torrid.App/AppIdentity.cs`.

**Tests first:**
- [ ] `TaskSchedulerStartupTests.BuildTaskXml_RunsTheGivenExe_AtLogon_WithHighestPrivileges` (result contains `<LogonTrigger>`, `<RunLevel>HighestAvailable</RunLevel>`, and `<Command>` equal to the exe path)
- [ ] `TaskSchedulerStartupTests.BuildTaskXml_HasNoTimeLimitAndIgnoresBattery` (contains `<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>`, `<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>`, `<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>`)
- [ ] `TaskSchedulerStartupTests.BuildTaskXml_EscapesXmlInThePath` (path `C:\A & B\Torrid <1>.exe` produces well-formed XML: parse the result with `System.Xml.Linq.XDocument.Parse` and read `Command` back equal to the path)
- [ ] `TaskSchedulerStartupTests.TaskName_IsNotEmpty` is not needed; instead the Debug/Release difference is by construction (`#if DEBUG`), reviewed by reading.

**Implementation:**
- [ ] `AppIdentity`: in the `#if DEBUG` branch add `public const string StartupTaskName = "Torrid (Dev)";`, in the `#else` branch `"Torrid"`; update the type summary by a few words ("and its Windows startup task").
- [ ] `IStartupRegistration` (namespace `Torrid.App.Startup`): `bool IsEnabled(); string? SetEnabled(bool enable);` with a doc line: returns null on success, otherwise a message fit to show the user; never throws.
- [ ] `TaskSchedulerStartup : IStartupRegistration`:
  - `public static string BuildTaskXml(string exePath)`: Task Scheduler 1.2 XML with `LogonTrigger` (enabled), `Principal` (`LogonType` `InteractiveToken`, `RunLevel` `HighestAvailable`), the three `Settings` values above plus `MultipleInstancesPolicy` `IgnoreNew`, and `Exec` with `Command` = exe path and `WorkingDirectory` = its folder. Build with `XElement` (escaping comes free) and return `ToString()`; declare `UTF-16` is not needed if written with `Encoding.Unicode` to the temp file (schtasks accepts UTF-16 XML; write the file as UTF-16 with BOM via `File.WriteAllText(path, xml, Encoding.Unicode)` and make the XML declaration say `utf-16`).
  - `IsEnabled()`: run `schtasks.exe /Query /TN "<AppIdentity.StartupTaskName>"`, `true` when exit code is 0, `false` on any other result or exception.
  - `SetEnabled(true)`: write the XML to a temp file, run `schtasks.exe /Create /TN "<name>" /XML "<file>" /F`, delete the temp file in `finally`.
  - `SetEnabled(false)`: if `!IsEnabled()` return null (already off); else `schtasks.exe /Delete /TN "<name>" /F`.
  - One private `Run(string arguments)` helper: `ProcessStartInfo` with `UseShellExecute=false`, `CreateNoWindow=true`, redirect stdout and stderr, `WaitForExit(10000)` then kill on timeout; returns `(int ExitCode, string Text)`. On non-zero exit `SetEnabled` returns `"Couldn't change the startup task: " + text.Trim()` (first line only), on exception the same prefix plus `ex.Message`. Whole body in `try/catch (Exception)`; `Debug.WriteLine` the failure (same style as `StartMenuShortcut`). The exe path is `Environment.ProcessPath`; if empty return `"Couldn't find Torrid's own path."`.

**Acceptance:** build, then `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests`. Do NOT call `TaskSchedulerStartup.SetEnabled` from any test or from a run.

---

### Task 5: Settings toggle (B, view model, UI, wiring)

**Files to modify:** `Torrid.App/ViewModels/SettingsPageViewModel.cs`, `Torrid.App/Views/SettingsView.xaml`, `Torrid.App/AppHost.cs`, `Torrid.Tests/SettingsPageViewModelTests.cs`.

**Tests first.** In `SettingsPageViewModelTests.cs` add a private nested `FakeStartup : IStartupRegistration` (fields: `Enabled`, `ErrorToReturn`, `Calls` list) and update the three existing constructor calls (lines ~17, ~26, ~286) to pass a `new FakeStartup()` as the new last argument (so existing tests compile unchanged otherwise). New tests:
- [ ] `StartWithWindows_ReflectsTheRegistration` (fake `Enabled = true` before construction: `vm.StartWithWindows` is true; default fake: false)
- [ ] `StartWithWindows_TurnOn_CallsTheRegistration_AndKeepsItOn` (set true: fake got `SetEnabled(true)`, property true, `StartupError` null)
- [ ] `StartWithWindows_TurnOff_CallsTheRegistration`
- [ ] `StartWithWindows_Failure_KeepsTheOldValue_AndShowsTheMessage` (fake returns `"Access is denied"`: property stays false, `StartupError == "Access is denied"`, `HasStartupError` true)
- [ ] `StartWithWindows_SuccessAfterFailure_ClearsTheMessage`
- [ ] `StartWithWindows_FailureRaisesPropertyChanged` (so the bound switch snaps back; assert a `PropertyChanged` for `StartWithWindows` fires on the failed set)

**Implementation:**
- [ ] `SettingsPageViewModel`: add `IStartupRegistration startup` as the last constructor parameter; field `_startup`; field `_startWithWindows = startup.IsEnabled();` (read once, in the constructor); `string? _startupError`.
- [ ] Property `StartWithWindows`: getter returns the field; setter, when the value differs, calls `_startup.SetEnabled(value)`; on `null` set the field and clear the error, otherwise keep the field and set `StartupError`; always `Raise(nameof(StartWithWindows))` (the re-raise makes the switch snap back after a failure, same trick as `TextColor`). Plus `StartupError` (`string?`) and `HasStartupError`, raised together.
- [ ] `AppHost.cs` line ~81: add `new TaskSchedulerStartup()` as the last argument, plus `using Torrid.App.Startup;`. In `--demo` runs use the real one too (it only queries until the user toggles; the constraints above forbid toggling in live runs). Do not add a demo fake: smallest change.
- [ ] `SettingsView.xaml`, Startup card: after the existing row add `<Border Style="{StaticResource DividerStyle}"/>` and a second row with the identical two-column layout: title `Start with Windows`, caption `Torrid starts when you sign in, without a UAC prompt. Uses a Task Scheduler task named "Torrid".`; `CheckBox Style="{StaticResource ToggleSwitchStyle}" IsChecked="{Binding StartWithWindows}" AutomationProperties.Name="Start with Windows"`. Under the row, a `TextBlock` bound to `StartupError` with `CaptionTextStyle`, red text (reuse an existing error brush; `Grep` `OverlayView.xaml`'s hotkey error `TextBlock` and copy its style and `HasHotkeyError`-style visibility trigger), `Margin="0,8,0,0"`.

**Acceptance:** build, then `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests` (all green).

---

### Task 6: README

**File to modify:** `README.md` (only these lines; leave the rest of the uncommitted README edits alone).

- [ ] Line ~91 currently says "Windows will show a UAC prompt each time it starts." Replace with: "Windows shows a UAC prompt each time you start Torrid yourself. To avoid that at login, turn on **Settings, Startup, Start with Windows**: it adds a Task Scheduler task named `Torrid` (run at logon, highest privileges), so Torrid starts at sign-in with no prompt. Turn the switch off to remove the task. Debug builds use a task named `Torrid (Dev)`, and creating it needs an elevated terminal." Keep the sentence about the Start Menu shortcut after it.
- [ ] Line ~141 ("Launching the exe triggers a UAC prompt.") stays true; leave it.
- [ ] In the `## Overlay` section (line ~68-79) add one bullet: "The overlay is hidden from screen sharing and recording by default; the Overlay page has a switch to show it in captures (needs Windows 10 version 2004 or newer)."
- [ ] Line ~24 (pages overview) mentions Settings contents? If it lists Settings options, add "Start with Windows" there; otherwise leave it.

**Acceptance:** `git diff README.md` shows only these changes on top of what was already modified.

---

### Final verification

- [ ] `& "C:\Program Files\dotnet\dotnet.exe" build` then `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests`, both green.
- [ ] Optional demo run for a visual check only: `--demo --page Settings --config "$env:TEMP\torrid-final\config.json"`; look at the Startup card and Overlay page, do not click the new switch, close the app, confirm no `Torrid.App` process remains, delete `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Torrid (Dev).lnk` if it appeared, delete the temp config folder.
- [ ] Report to the user: the manual steps still needed on a real release build (toggle Start with Windows once elevated, sign out and in; open a screen recorder to confirm the overlay is absent), because they were not run here.
