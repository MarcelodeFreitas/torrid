# Torrid

A Windows desktop app that monitors CPU and GPU (core / hot spot / VRAM) temperatures in real time, shows them as colour-coded numbers in system tray icons and a main window, and fires a native Windows notification when a sensor crosses into the critical zone.

Built on top of [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) for sensor access.

## Status

Working on the development machine (Ryzen 9 5950X, one RTX 3090 currently detected). The original design lives in [PROJECT_PLAN.md](PROJECT_PLAN.md); this README describes what is actually built.

Not done / not verified:

- The tray menu and tray icons are checked by reading the code and by unit tests of their logic, not by automated UI tests.
- Torrid has not been tested against Riot Vanguard or any other anti-cheat. See [docs/anti-cheat.md](docs/anti-cheat.md) for what it does and what is unknown.
- The app is unsigned.
- Two-GPU behaviour is covered by the demo data and unit tests; only one GPU is present on the development machine.

## Why

Most GPU monitoring tools show core temperature only. For thermal validation and long-running workloads (e.g. local LLM inference), Hot Spot and Memory Junction temperatures are the metrics that actually catch problems early. Torrid surfaces those front and centre with live colour-coded alerts instead of requiring you to keep a separate monitoring tool open.

## Features

- **One window with a sidebar.** Pages: **Home** (live dashboard of the sensors you chose), **Sensors** (every sensor found, with switches for Window / Tray / Alerts and renaming to tell identical cards apart), **Thresholds** (edit where blue, green, yellow and red begin per sensor type) and **Settings** (poll interval, repeat alerts, start minimized, anti-cheat behaviour, PawnIO driver status, test notification). Edits apply instantly and save automatically.
- **Per-sensor tray icons.** Each chosen sensor gets its own tray icon with a colour-coded number, a tooltip, and a blink while that sensor is in the red zone. Left-click opens the window on Home; the right-click menu has only Settings (opens the Sensors page) and Exit. Closing the window only hides it; Exit from the tray quits.
- **Alerts.** A Windows toast fires once when a sensor enters red, with optional repeat every N minutes while it stays red. A "Send test notification" button in Settings checks that alerts reach you.
- **Anti-cheat safety.** While a known anti-cheat is running, Torrid pauses (default), switches to GPU only, or carries on, as you choose. See [docs/anti-cheat.md](docs/anti-cheat.md).
- **Sensible defaults.** Only the CPU temperature and GPU hot spot are shown in the window and tray at first (alerts are on for every sensor); the other sensors Torrid knows about (per device: CPU package, GPU core, GPU hot spot, GPU VRAM / memory junction) can be switched on from the Sensors page.

Settings are stored in `%AppData%\Torrid\config.json`.

## Running it

Torrid asks for administrator rights on launch (the published build embeds a `requireAdministrator` manifest). Reading the CPU temperature needs it; without it the CPU reads `0`. GPU temperatures read fine without it on the development machine.

### CPU temperature needs the PawnIO driver (install it separately)

The CPU temperature comes from a kernel driver called **PawnIO**, which Torrid does **not** include and **never installs by itself**. Install it yourself from the official release page: <https://github.com/namazso/PawnIO.Setup/releases/latest>. Without it the CPU temperature is simply unavailable; GPU temperatures still work.

Torrid shows the driver's status under **Settings, CPU temperature driver (PawnIO)** (installed version, or "isn't installed"), with an "Open download page" button (it only opens the page in your browser) and a "Re-check" button to use after you install it. When PawnIO is missing, Home also shows a short note pointing to Settings. Torrid recommends **PawnIO 2.1.0 or newer** (the latest known is 2.2.0): older versions were signed with a certificate that was reported to be shared with cheat software and that FACEIT's anti-cheat blocked. That report concerned FACEIT (see [namazso/PawnIO.Setup issue #1](https://github.com/namazso/PawnIO.Setup/issues/1)); how other anti-cheats, Riot Vanguard included, treat PawnIO is unconfirmed. Details in [docs/anti-cheat.md](docs/anti-cheat.md).

Windows will show a UAC prompt each time it starts. Separately, Torrid creates a **Start Menu shortcut** so that Windows delivers its notifications (see below).

### Start Menu shortcut side effect

On Windows, an unpackaged desktop app's toast notifications are silently dropped unless a Start Menu shortcut carrying the app's AppUserModelID exists. Torrid therefore creates or refreshes `%AppData%\Microsoft\Windows\Start Menu\Programs\Torrid.lnk` (pointing at the running exe) every time it starts. Debug builds use `Torrid (Dev).lnk` and a different id so they do not fight the published app. Windows can take up to about a minute to pick up a brand-new shortcut, so the very first notification after a fresh install may be missed. To undo the side effect, delete the shortcut. Details: [tools/ToastProbe/FINDINGS.md](tools/ToastProbe/FINDINGS.md).

## Tech stack

C# / .NET 8, WPF (plus WinForms `NotifyIcon` for the tray), `LibreHardwareMonitorLib` 0.9.6, `Microsoft.Toolkit.Uwp.Notifications` for toasts, xUnit for tests. Projects: `Torrid.Core` (UI-free logic), `Torrid.App` (WPF), `Torrid.Tests`.

## Development

Prerequisite: .NET 8 SDK.

```powershell
dotnet build Torrid.sln
dotnet test Torrid.sln
```

Debug builds use a manifest that does **not** request administrator, so you can run them straight from the IDE or a normal terminal (the CPU temperature will read `0`). Release builds use the `requireAdministrator` manifest.

Run the Debug build from `Torrid.App\bin\Debug\net8.0-windows10.0.17763.0\Torrid.App.exe`.

### Command-line flags

| Flag | Effect |
|---|---|
| `--show` | Open the main window at start instead of starting in the tray |
| `--demo` | Use fake two-GPU data instead of real hardware (deterministic). Without `--config` nothing is read from or saved to your real config |
| `--demo-paused` | Like `--demo`, but simulates an anti-cheat being active |
| `--config <path>` | Use this config file instead of `%AppData%\Torrid\config.json` |
| `--page <Home\|Sensors\|Thresholds\|Settings>` | Page to open first (handy for screenshots) |
| `--test-toast` | Send one test notification about 15 seconds after start, then exit (used by `tools\Verify-Toast.ps1`) |

When testing, always pass `--config` with a temporary path so your real settings are not touched.

### Tools

- `tools\Capture-Window.ps1` screenshots the Torrid window to a PNG, so UI changes can be checked without clicking. Example: `powershell -NoProfile -File tools\Capture-Window.ps1 -Launch -Out shot.png`. Pass different arguments with `-AppArgs` (an array, so call the script from a PowerShell session rather than through `powershell -File` when you need more than one), for example `-AppArgs '--show','--demo','--page','Sensors','--config',"$env:TEMP\t.json"`.
- `tools\Verify-Toast.ps1` runs the Debug exe with `--test-toast` and checks the Windows registry to see whether Windows accepted the notification. It prints `PASS` or `FAIL`.
- `tools\SensorProbe` is a console app that lists every sensor LibreHardwareMonitor can see; findings are in [tools/SensorProbe/FINDINGS.md](tools/SensorProbe/FINDINGS.md).
- `tools\ToastProbe` is the experiment that found the Start Menu shortcut requirement; see [tools/ToastProbe/FINDINGS.md](tools/ToastProbe/FINDINGS.md).

## Publishing

```powershell
dotnet publish Torrid.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The result is a single self-contained `Torrid.App.exe` in `publish\` (git-ignored), with `.pdb` debug-symbol files written beside it. Launching the exe triggers a UAC prompt.

## Anti-cheat

Some game anti-cheats dislike monitoring tools that use a kernel driver. Torrid detects a list of known anti-cheats (and any process names you add) and backs off. It cannot be tested against them here, so read [docs/anti-cheat.md](docs/anti-cheat.md) for what is known, what is not, and what to try if a game still complains.

## License

TBD.
