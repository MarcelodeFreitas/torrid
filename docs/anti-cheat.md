# Torrid and anti-cheat software

Some online games ship an anti-cheat (Riot Vanguard, Easy Anti-Cheat, BattlEye and others) that dislikes system monitoring tools, especially ones that load a kernel driver. Torrid tries to stay out of the way. This page explains what it does, what it cannot do, and what is still unknown.

## Short version

- By default, when Torrid sees an anti-cheat running it **keeps monitoring** and does not change anything. You can switch to **Pause monitoring** (it closes its hardware access completely and stops reading sensors until the anti-cheat is gone) or **GPU only** in Settings.
- Torrid cannot be tested against Vanguard (or any other anti-cheat) here, so this is a best-effort precaution, not a guarantee.
- If a game still complains, see [If a game still complains](#if-a-game-still-complains).

## Where the temperatures come from

Torrid reads sensors through [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (`LibreHardwareMonitorLib` 0.9.6). There are two separate paths:

| Sensor | How it is read | Needs |
|---|---|---|
| CPU temperature | Reads CPU model-specific registers through a kernel driver. LibreHardwareMonitorLib 0.9.6 uses the **PawnIO** driver for this (not the older WinRing0 driver). | Administrator rights, and the PawnIO driver installed on the system |
| GPU temperatures (core, hot spot, VRAM) on NVIDIA cards | NVIDIA's user-mode APIs. No kernel driver of Torrid's own is involved. | Nothing extra; on the development machine they read correctly even without administrator rights |
| Added motherboard / VRM / chipset sensors | The board's Super I/O chip (LPC bus) or embedded controller, through the **PawnIO** driver. | Administrator rights and PawnIO |
| Added memory sensors | Temperature sensors on the memory modules, over the SMBus, through the **PawnIO** driver. | Administrator rights and PawnIO |
| Added storage sensors | The drive's own reporting through Windows storage requests (user mode, no PawnIO). | Possibly administrator rights; not PawnIO |

This is why Torrid asks to run as administrator: without it the CPU temperature reads `0`.

## What Torrid does when it detects an anti-cheat

Torrid checks the list of running processes on every poll (every 2 seconds by default). The check runs **before** any hardware is opened, so if an anti-cheat is already running when Torrid starts, Torrid applies your chosen behaviour from the first poll (with Pause, it never opens the hardware at all).

You choose the behaviour under **Settings, Anti-cheat compatibility, When an anti-cheat is running**:

| Behaviour | What happens | Notes |
|---|---|---|
| **Pause monitoring** | Torrid closes the hardware entirely and stops reading. Sensors show as paused, the tray tooltip says `paused (<product> is running)`, and no alerts are raised. When the anti-cheat exits, monitoring resumes by itself. | The safest option. You get no temperatures while the game is running. |
| **GPU only** | Torrid keeps reading GPU temperatures and stops reading the CPU. It no longer enables the CPU, motherboard and memory hardware, which are the parts that use PawnIO. | You keep GPU temperatures during the game. Whether avoiding the CPU path is enough to satisfy a given anti-cheat is not known. |
| **Keep monitoring** (default) | Nothing changes. Torrid only records that an anti-cheat was detected so the UI can say so. | Some games may refuse to start or may remove you from a match. Use at your own risk. |

## Which hardware Torrid opens

LibreHardwareMonitor reads hardware in groups, and Torrid only opens the groups it needs. Which ones depends on the anti-cheat level and on the sensors you have [added](../README.md#adding-more-sensors):

| Level | CPU | GPU | Motherboard | Memory | Storage |
|---|---|---|---|---|---|
| **Paused** (anti-cheat running, Pause monitoring) | no | no | no | no | no |
| **GPU only** (anti-cheat running, GPU only) | no | yes | no | no | only if you added a storage sensor |
| **Full** (no anti-cheat, or Keep monitoring) | yes | yes | only if you added a motherboard sensor | only if you added a memory sensor | only if you added a storage sensor |

- CPU, Motherboard and Memory are the groups that use the **PawnIO** driver (CPU registers, the Super I/O chip, the SMBus). **GPU only never opens any of them**, whatever you have added.
- With nothing added, Torrid opens exactly what it always did: CPU and GPU at Full, GPU at GPU only, nothing when paused.
- **Adding a motherboard or memory sensor makes Torrid use PawnIO for that group as well**, at the Full level only. Remove the last sensor of that kind (the × on its row) and the group is closed again.
- **Scanning** (Sensors, Add sensor, Scan for sensors) only happens when you press the button. It briefly opens every group the current level allows (all five at Full, GPU and storage at GPU only) and closes them again; while monitoring is paused it is refused and touches nothing.

## How detection works

Detection is by **process name only**. Torrid does not inspect drivers, kernel modules or game files. It matches these process names, case-insensitively and with or without `.exe`:

| Product | Process names |
|---|---|
| Riot Vanguard | `vgc`, `VALORANT-Win64-Shipping` |
| Easy Anti-Cheat | `EasyAntiCheat`, `EasyAntiCheat_EOS`, `start_protected_game` |
| BattlEye | `BEService`, `BEService_x64` |
| FACEIT Anti-Cheat | `FACEIT` |
| Tencent ACE | `SGuard64`, `SGuardSvc64` |
| PunkBuster | `PnkBstrA`, `PnkBstrB` |
| nProtect GameGuard | `GameMon`, `GameMon64`, `npggsvc` |

`vgtray` (Vanguard's tray icon) is deliberately not on the list: it runs whenever Vanguard is installed, whereas `vgc` runs when protection is active.

You can extend the list yourself under **Settings, Also treat these programs as anti-cheat**. Enter a process name such as `MyGame.exe` and Torrid reacts to it the same way as to a known anti-cheat. Find the process name in Task Manager (Details tab).

The list is a best effort. It only covers what is listed above, and anti-cheat vendors can rename their processes.

## PawnIO is a separate, system-wide driver

PawnIO is installed on the system, not owned by Torrid. Torrid **cannot unload it**, and pausing Torrid does not remove it. Pausing or GPU-only mode only stops Torrid from using it.

### Installing and checking PawnIO

- **Install it separately.** Torrid does not ship PawnIO and **never installs or updates a driver itself**. Get the installer from the official release page, <https://github.com/namazso/PawnIO.Setup/releases/latest>, and run it yourself (it needs administrator rights).
- **Torrid shows its status.** Under **Settings, CPU temperature driver (PawnIO)** Torrid reports whether PawnIO is installed and which version, by reading the installer's uninstall entry in the registry (it does not touch the driver). **Open download page** only opens the release page in your browser. After installing or updating, press **Re-check**; the check also runs once when Torrid starts.
- **Home shows a short note only when PawnIO is not installed** ("CPU temperature needs the PawnIO driver — see Settings."), and not while monitoring is paused. An old version is explained in Settings only, so Home does not nag.
- **Recommended: 2.1.0 or newer** (the latest known to Torrid is 2.2.0). Versions older than 2.1.0 were signed with a certificate that was reported to be shared with cheat software, and FACEIT's anti-cheat blocked it. The maintainer reported this fixed as of 2.1.0. Source: [namazso/PawnIO.Setup issue #1](https://github.com/namazso/PawnIO.Setup/issues/1).
- **What is and is not known.** That report was about FACEIT. Whether Riot Vanguard or any other anti-cheat objects to PawnIO, old or new, is **unconfirmed**; Torrid's check says nothing about how a particular anti-cheat will behave.
- **Not the same as the "not administrator" case.** Without administrator rights the CPU reads `0` even with PawnIO installed; Torrid's PawnIO status only describes the driver, not whether it can currently be used.

If an anti-cheat objects to the PawnIO driver being present at all, the only remedy is to uninstall PawnIO (Windows Settings, Apps, or its own uninstaller). After that:

- Torrid still shows **GPU temperatures**, which do not depend on PawnIO.
- Torrid can no longer read the **CPU temperature**.
- Other monitoring tools that rely on PawnIO will also stop reading the CPU.

## If a game still complains

The exact cause of the Vanguard complaint originally reported on the development machine is **unconfirmed**. It may have been Torrid, the PawnIO driver, another tool, or something unrelated. The behaviours above are precautions based on how these tools are known to work, not a verified fix.

Things to try, in order:

1. If the game refuses to start or removes you, set **Settings, When an anti-cheat is running** to **Pause monitoring**. Check the status line on that page: it says `Paused` while an anti-cheat is detected.
2. If the game's anti-cheat is not on the known list, add the game or anti-cheat process under **Also treat these programs as anti-cheat**.
3. Try **GPU only** if you want temperatures during the game and the game tolerates it. If it complains, go back to Pause.
4. Exit Torrid completely from the tray menu (right-click the tray icon, **Exit**) before launching the game. Closing the window only hides it.
5. If the anti-cheat still objects, check whether PawnIO is installed and uninstall it if you do not need CPU temperatures.
6. If the problem persists, please report the **exact error code and message** the anti-cheat shows (for Vanguard, for example, a `VAN` or `VANGUARD` code). That is the information needed to find the real cause.

## The on-screen overlay

The overlay is an ordinary topmost window, and its shortcut uses the Windows `RegisterHotKey` call. Torrid installs no keyboard hook and injects nothing into other programs. The overlay reads only readings Torrid already has, and it shows `paused` while monitoring is paused for an anti-cheat. Whether an anti-cheat accepts a visible overlay window is unknown, as with the rest of Torrid.

## Limits you should know about

- Torrid is **unsigned**. It has no code-signing certificate, so Windows and anti-cheat software have no publisher identity to trust. Whether that matters to any particular anti-cheat is unknown.
- Torrid has **not been tested against Vanguard or any other anti-cheat**. The detection list and the three behaviours are unit-tested for logic, and the pause/resume flow was exercised with a stand-in process named `vgc.exe`, but no real anti-cheat has been involved.
- Torrid cannot guarantee a given game will accept it running, even when paused. Anti-cheat vendors change their rules without notice.
- Detection runs on the poll interval (2 seconds by default, up to 60 seconds if you raise it in Settings), so there is a short window between an anti-cheat starting and Torrid noticing.
