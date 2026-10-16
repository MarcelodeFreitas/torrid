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

- **One window with a sidebar.** Pages: **Home** (live dashboard of the sensors you chose), **Sensors** (every sensor found, with switches for Window / Tray / Alerts and renaming to tell identical cards apart), **Thresholds** (set where blue, green, yellow and red begin for each sensor) and **Settings** (poll interval, repeat alerts, start minimized, anti-cheat behaviour, PawnIO driver status, test notification). Edits apply instantly and save automatically.
- **Per-sensor tray icons.** Each chosen sensor gets its own tray icon with a colour-coded number, a tooltip, and a blink while that sensor is in the red zone. Left-click opens the window on Home; the right-click menu has only Settings (opens the Sensors page) and Exit. Closing the window only hides it; Exit from the tray quits.
- **Alerts.** A Windows toast fires once when a sensor enters red, with optional repeat every N minutes while it stays red. A "Send test notification" button in Settings checks that alerts reach you.
- **Anti-cheat safety.** While a known anti-cheat is running, Torrid pauses (default), switches to GPU only, or carries on, as you choose. See [docs/anti-cheat.md](docs/anti-cheat.md).
- **Sensible defaults.** Only the CPU temperature and GPU hot spot are shown in the window and tray at first (alerts are on for every sensor); the other sensors Torrid knows about (per device: CPU package, GPU core, GPU hot spot, GPU VRAM / memory junction) can be switched on from the Sensors page.
- **Add any temperature sensor.** **Add sensor** on the Sensors page scans for every temperature sensor LibreHardwareMonitor can see (motherboard and VRM, chipset, storage, memory, extra CPU sensors such as CCDs or SoC, extra GPU sensors) and adds the ones you pick. Added sensors appear on Home, in the tray and in alerts like the built-in ones, each with its own thresholds. See [Adding more sensors](#adding-more-sensors).

Settings are stored in `%AppData%\Torrid\config.json`.

## Adding more sensors

The built-in sensors are the CPU temperature and, per GPU, core / hot spot / memory junction. For anything else (motherboard VRM, chipset, an NVMe drive, memory modules, CPU CCDs or SoC, other GPU sensors):

1. Open **Sensors** and press **Add sensor**. A panel opens above the list.
2. Press **Scan for sensors**. Torrid lists every temperature sensor it can read, grouped by hardware, with its current value. Sensors the built-in cards already read (for example the CPU's `Core (Tctl/Tdie)` or a GPU's hot spot) are marked **Already shown** and cannot be added twice.
3. Press **Add** next to the ones you want. They appear in the list straight away, switched on for the window (tray off, alerts on); change that with the usual switches, and rename them like any other sensor.
4. To remove one, press the **×** at the end of its row. Built-in sensors cannot be removed, only switched off.

Added sensors go into their own groups on Home and on the Sensors page (**Motherboard**, **Memory**, **Storage**, **Other**; extra CPU sensors join the CPU group, and extra GPU sensors join their GPU when Torrid can tell which card they belong to). Every sensor, added or built in, has its own limits on the **Thresholds** page (see [Thresholds](#thresholds)).

Honest limits:

- **What you can add depends on your hardware.** Torrid only offers what LibreHardwareMonitor can read. Motherboard temperatures come from the board's Super I/O (or embedded controller) chip, and only chips LibreHardwareMonitor supports report anything.
- **VRM temperature is only available on boards whose chip exposes it.** Many boards have no VRM sensor, or report it under a generic name such as "Temperature #3".
- **Motherboard, CPU and memory sensors need administrator rights and the PawnIO driver** (see below). Without them the scan may list nothing for those parts, or values read `0` or `--`. Storage and GPU sensors do not use PawnIO (storage may still need administrator rights). A real scan has only been run without administrator rights on one machine: it found only the CPU `Core (Tctl/Tdie)` (reading 0.0) and three GPU sensors, all of which the built-in cards already read, so nothing addable showed up on that machine without administrator rights (no motherboard, CCD, memory or storage sensors). Everything else is verified with demo data only.
- **There is no separate "CPU hot spot" sensor.** The CPU card reads the CPU temperature (on AMD Tctl/Tdie; on Intel the package or hottest core). Extra AMD sensors such as CCD1/CCD2 and SoC can be added (administrator rights needed).
- **"Other sensors" are best effort.** Sensors of hardware Torrid does not classify are shown when the hardware they belong to is already being read, but Torrid does not open extra hardware just for them.
- **Scanning is manual.** Torrid never scans by itself; a scan briefly opens every hardware group the current anti-cheat setting allows (including PawnIO-backed ones). Scanning is **unavailable while an anti-cheat is running** with the default Pause setting, and covers only GPU and storage in GPU-only mode.
- **Added motherboard or memory sensors make Torrid use PawnIO for those groups too.** Torrid only opens the hardware groups it needs: with nothing added that is CPU and GPU, exactly as before. Adding a motherboard or memory sensor adds that group (both use PawnIO), and only at the **Keep monitoring** / no-anti-cheat level; **GPU only** never opens them. Details in [docs/anti-cheat.md](docs/anti-cheat.md).
- An added sensor that cannot be read right now (hardware removed, monitoring paused, group not allowed) keeps its row, shows `--` and "Not detected right now", and never raises an alert.

## Thresholds

Every sensor has its own limits: where its colour changes from blue to green, green to yellow and yellow to red. A sensor you have not changed follows the **defaults for its type** (CPU temperature, GPU core, Motherboard / VRM and so on).

- **Default or Custom.** A sensor shows **Default** while it follows its type's defaults, and **Custom** once you set its own limits. **Reset** brings it back to the defaults.
- **Limits.** Set blue below, green below and yellow up to, in °C. They must rise (blue < green < yellow) and stay between 0 and 150. A valid change applies when you leave the box (press Tab or click away); an invalid one is marked with an error and is not saved.
- **Alerts.** A sensor's alert fires when its value goes above its **Yellow up to** limit, which is when it turns red. Alerts are switched per sensor on the Sensors page.
- **Defaults by sensor type.** Open by default, below the device sections. New sensors start from these limits, and changing one only affects sensors you have not customised.
- **Cards or List.** The Cards / List toggle switches between a card per sensor (with a preview bar) and one compact row per sensor. The choice is remembered. The search box filters by sensor name, hardware name or device.
- **Not shown.** Sensors with Window, Tray and Alerts all off are listed in a collapsed **Not shown** section at the end.
- **Advanced.** The last section lists every other temperature sensor your hardware reports (the other CPU sensors, motherboard chips, drives and so on) that is not on your Sensors page. Press **Scan for sensors** to fill it (Torrid never scans on its own); sensors are grouped by device and each has its own limits, Default/Custom badge and Reset, and the search box covers them. Setting limits here does not add the sensor to the Sensors page; the limits apply once you add it there. The values shown are from the last scan.
- **From the Sensors page.** Each sensor row has a **Limits** link that opens the Thresholds page on that sensor.

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
| `--open-add-sensor` | Demo only (ignored without `--demo` / `--demo-paused`): open the Sensors page's Add sensor panel and scan the demo hardware at start (for screenshots) |

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
