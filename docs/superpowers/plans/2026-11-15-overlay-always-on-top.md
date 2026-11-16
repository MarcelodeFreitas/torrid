# Overlay Always On Top Implementation Plan

> **For agentic workers:** One task, in order. Steps use checkbox (`- [ ]`) syntax. Do not re-decide design; if the code does not match what this plan says, stop and report instead of improvising.

## Goal

The sensor overlay stays above every other normal window at all times: normal windows, windowed games, borderless / fullscreen-windowed games, and other "always on top" windows that activate after it. It keeps being hidden from screen sharing and recording (`SetWindowDisplayAffinity` with `OverlayCapture.DisplayAffinity`, `WDA_EXCLUDEFROMCAPTURE` = `0x11`), stays click-through and never takes focus.

**Why it fails today:** `Topmost="True"` only puts the overlay in the topmost group once. Inside that group Windows orders windows by activation, so a game (or any other topmost window) that activates later goes above it, and nothing ever raises the overlay again.

## Non-goals (what cannot be done without injection)

- **True exclusive fullscreen** (legacy DXGI/D3D9 exclusive mode, or a game with "fullscreen optimizations" turned off): the game owns the display output and DWM does not compose other windows on it. No normal window can draw there, however it is raised. Only overlays injected into the game's render loop (RTSS, Steam, Discord, NVIDIA) can, and Torrid will not inject (anti-cheat: see `docs/anti-cheat.md`; the repo already refuses even a keyboard hook, see `HotkeyHost`).
- **What the user can do themselves for those games:** pick "Borderless" / "Fullscreen windowed" in the game's display settings; or keep Windows "fullscreen optimizations" on for the game (exe Properties > Compatibility, "Disable fullscreen optimizations" unchecked) and, on Windows 11, "Optimizations for windowed games" on (Settings > System > Display > Graphics). Many DX11/DX12 "exclusive" games then actually present through DWM and the overlay shows. Whether a given game does is per game and cannot be checked here.
- **Windows above the topmost band:** Start, Task View, Xbox Game Bar, the on-screen keyboard, Magnifier and other UIAccess/system windows live in a higher z-order band. Reaching it needs `uiAccess="true"` plus an Authenticode-signed exe installed under Program Files (or the undocumented, Microsoft-signed-only `CreateWindowInBand`). Not in scope.
- No fullscreen detection, no UI, no new setting.

## Decisions

1. **Re-raise routine:** `SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE)`. It changes only z-order: no move, no size, no activation, no `SWP_FRAMECHANGED`, so the extended styles (click-through, no-activate) and the display affinity are untouched. Return value ignored, same as `SetWindowDisplayAffinity` today.
2. **Both triggers, only while the overlay is visible:**
   - **`SetWinEventHook(EVENT_SYSTEM_FOREGROUND, ..., WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS)`** raises it immediately when any other app becomes foreground (Alt+Tab into a game, a game launching). Out-of-context means no DLL is loaded into other processes (not injection, not a keyboard hook); Windows delivers the callback through the installing thread's message loop, so it runs on the WPF UI thread and needs no `UiThread.Post`.
   - **A `DispatcherTimer` every 1 second** catches windows that make themselves topmost *after* they become foreground (many borderless games and launchers call `SetWindowPos(HWND_TOPMOST)` a moment later or repeatedly), which the foreground event alone misses. 1 s: a `SetWindowPos` that changes nothing costs microseconds, 1 s is the longest the overlay can stay hidden behind such a window, and a shorter interval only makes the fight with other always-on-top tools busier.
   - Both start when the window becomes visible (one immediate raise too) and stop when it is hidden or closed, so a hidden overlay costs nothing.
3. **No `EVENT_OBJECT_REORDER`:** it fires for every z-order change of every window system wide (very chatty), and the 1 s timer already covers what it would add.
4. **No `SHQueryUserNotificationState` / `QUNS_RUNNING_D3D_FULL_SCREEN` check in the first cut.** It cannot make the overlay draw over exclusive fullscreen, and under fullscreen optimizations it may report "D3D fullscreen" for games where the overlay *does* show, so gating on it could hide the overlay where it works. It is kept as the fallback for Risk 1 below, only if the manual check shows a problem.
5. **Display affinity stays correct on every path:** affinity belongs to the HWND. The overlay HWND is created once (`_window ??= new OverlayWindow()`), `Hide()`/`Show()` do not destroy it, `SetWindowPos` does not touch it, and `OverlayController.Refresh()` re-applies it before every `Show()` and on every config change and reading. Nothing to add.
6. **No focus steal / click-through kept:** `SWP_NOACTIVATE` plus the existing `WS_EX_NOACTIVATE`; `WS_EX_TRANSPARENT | WS_EX_LAYERED` are not changed by `SetWindowPos`.
7. **All code in `OverlayWindow.xaml.cs`.** There is no pure logic worth moving to `Torrid.Core` (it is Win32 calls and two constants), so no new Core type and no new unit test. Existing tests must stay green (baseline 2026-11-15: 617 passed).
8. Elevation does not matter: the Release build is `requireAdministrator`, the Debug build `asInvoker`; `SetWindowPos` on our own window is never blocked by UIPI either way.

## Exact changes

### `Torrid.App/Overlay/OverlayWindow.xaml.cs` (only code file changed)

- [ ] Add `using System.Windows.Threading;`.
- [ ] Constants next to the existing ones:
  ```csharp
  private static readonly nint HwndTopmost = -1;
  private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10;
  private const uint EventSystemForeground = 0x0003;
  private const uint WinEventOutOfContext = 0x0, WinEventSkipOwnProcess = 0x2;
  ```
- [ ] Fields:
  ```csharp
  private readonly DispatcherTimer _raiseTimer = new() { Interval = TimeSpan.FromSeconds(1) };
  private WinEventProc? _onForeground; // kept in a field: Windows holds the pointer, the GC must not collect the delegate
  private nint _foregroundHook;
  ```
- [ ] Constructor, after `InitializeComponent()`:
  ```csharp
  _raiseTimer.Tick += (_, _) => RaiseToTop();
  IsVisibleChanged += (_, _) => KeepOnTop(IsVisible);
  ```
- [ ] New `private void KeepOnTop(bool on)`: when `on`: `RaiseToTop()`, `_raiseTimer.Start()`, and if `_foregroundHook == 0` set `_onForeground ??= (_, _, _, _, _, _, _) => RaiseToTop();` then `_foregroundHook = SetWinEventHook(EventSystemForeground, EventSystemForeground, 0, _onForeground, 0, 0, WinEventOutOfContext | WinEventSkipOwnProcess);`. When off: `_raiseTimer.Stop()`, and if `_foregroundHook != 0` call `UnhookWinEvent(_foregroundHook)` and set it to 0.
- [ ] New `private void RaiseToTop()`: `var hwnd = new WindowInteropHelper(this).Handle; if (hwnd == 0) return;` then `SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);` (result ignored). One-line comment: why: Topmost alone loses to topmost windows activated later.
- [ ] `protected override void OnClosed(EventArgs e)`: `KeepOnTop(false); base.OnClosed(e);` (`OverlayController.Dispose` calls `Close()`; this guarantees the hook is removed even if `IsVisibleChanged` does not fire on close).
- [ ] P/Invokes next to the existing ones:
  ```csharp
  private delegate void WinEventProc(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime);

  [DllImport("user32.dll")]
  private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

  [DllImport("user32.dll")]
  private static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint hmodWinEventProc, WinEventProc pfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

  [DllImport("user32.dll")]
  private static extern bool UnhookWinEvent(nint hWinEventHook);
  ```
- [ ] Update the class `<summary>` to: "Click-through, never-focused sensor overlay in the top-right of the primary screen, re-raised above other topmost windows while visible. Only UI thread."

No change to `OverlayWindow.xaml` (keep `Topmost="True"`, it sets the initial state), `OverlayController.cs`, `HotkeyHost.cs`, `UiThread.cs` or anything in `Torrid.Core`.

### `README.md`, Overlay section (line 79)

- [ ] Replace the exclusive-fullscreen bullet with: "The overlay stays on top of other windows, including windowed and borderless games. An exclusive-fullscreen game draws over every window, so the overlay cannot show there; switch the game to borderless / fullscreen windowed, or keep Windows' fullscreen optimizations on for it (many games then present through the desktop and the overlay shows)."

## Test plan

**Automated**
- [ ] `& "C:\Program Files\dotnet\dotnet.exe" build` and `& "C:\Program Files\dotnet\dotnet.exe" test Torrid.Tests`: all green (617 before the change). No new unit test: no new pure logic.

**Manual** (Debug build, `--demo --config %TEMP%\torrid-ontop.json`; leave no `Torrid.App` running afterwards; delete `Torrid (Dev).lnk` from the Start Menu folder if the run re-creates it)
- [ ] Synthetic topmost fight (no game needed): show the overlay (Ctrl+Alt+O), open Task Manager, Options > Always on top, drag it over the overlay's corner and click it. The overlay is back above it at once (foreground event) or within 1 s.
- [ ] Normal windows: maximise a browser over the top-right; the overlay stays visible.
- [ ] Windowed game: overlay visible; Alt+Tab out and back, still visible.
- [ ] Borderless / fullscreen-windowed game: overlay visible within 1 s of the game appearing, and after Alt+Tab back into it.
- [ ] Exclusive fullscreen game: expected not to show (documented). Check the game does **not** flicker, minimise or drop out of fullscreen once a second. Then the same game with fullscreen optimizations on: note whether the overlay shows.
- [ ] Capture still hidden: with the overlay visible, start a Discord/Teams/OBS display capture or Win+Shift+S snip: overlay absent. Turn the Overlay page switch off: overlay present. Turn it back on, hide and show the overlay with the hotkey: still absent.
- [ ] No focus steal: type in Notepad while the overlay is visible for 10+ s; the caret and keystrokes stay in Notepad; the game keeps focus while playing.
- [ ] Click-through: click on something under the overlay; the click reaches the window below.
- [ ] Hide the overlay and watch Task Manager for a minute: Torrid CPU does not rise (timer and hook stopped).

## Risks

1. **Exclusive-fullscreen games reacting to a topmost window raised over them** (occlusion can make some DXGI games flicker or leave fullscreen). The current `Topmost` overlay already overlaps them, so this is likely unchanged, but the 1 s re-raise makes it periodic. If the manual check shows it: skip `RaiseToTop()` while `SHQueryUserNotificationState` reports `QUNS_RUNNING_D3D_FULL_SCREEN` (3) (one extra `shell32` P/Invoke, ~4 lines). Not done up front, see Decision 4.
2. **Fighting other always-on-top tools** that also re-raise themselves (other overlays, some launchers): the two swap places up to once a second. Last raise wins; acceptable, the user asked for the overlay to win.
3. **Anti-cheat perception:** an out-of-context WinEvent hook loads nothing into other processes and is widely used by accessibility tools; it is not a keyboard hook. Still untested against any anti-cheat, like the rest of Torrid.
4. **Delegate lifetime:** if `_onForeground` were a local, the GC could collect it and the hook would crash the process. The field prevents that; keep it.
5. Windows in the system/UIAccess band (Start, Game Bar, on-screen keyboard) stay above the overlay; that is by design of Windows (see Non-goals).
