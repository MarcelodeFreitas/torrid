# Toast notification investigation — open issue, deferred

`Torrid.App`'s `WindowsToastNotifier` (via `Microsoft.Toolkit.Uwp.Notifications` 7.1.3,
`ToastContentBuilder().Show()`) does not produce a visible toast or an Action Center
entry on this machine, in either configuration tested:

| Run | Elevated | `Show()` threw? | Toast/banner appeared? | Action Center entry? |
|---|---|---|---|---|
| `Torrid.App.exe` (real app) | Yes (requireAdministrator) | No | No | No |
| `tools/ToastProbe` (throwaway console app, same package/API, no manifest) | No | No | No | No |

**Conclusion so far:** not an elevation problem — the non-elevated probe fails identically.
`.Show()` completes without throwing in both cases, meaning the call reaches the
notification API layer without a COM/activation exception, but the notification never
reaches Action Center at all (not just a suppressed banner). Root cause not yet
determined — candidates not yet checked:
- Windows notification settings on this machine: Settings > System > Notifications,
  whether "Get notifications from apps and other senders" is on, and whether Focus
  Assist is active.
- Whether the process's AUMID (`Torrid.App` / `ToastProbe.App`) needs a matching
  Start Menu shortcut to be accepted by the shell on this Windows build — some Windows
  10/11 versions still require this for unpackaged apps despite documentation
  suggesting `SetCurrentProcessExplicitAppUserModelID` alone is sufficient.
- Whether `Microsoft.Toolkit.Uwp.Notifications`' internal `ToastNotificationManagerCompat`
  path is actually being hit by `ToastContentBuilder.Show()` on this package version,
  vs. silently falling back to something that no-ops without a package identity.

## Update 2026-09-25 — additional diagnostics from the final Phase 1 review

The final whole-branch review inspected the registry directly and found the AUMID
registration itself succeeded for both `Torrid.App` and `ToastProbe.App`
(`HKCU\Software\Classes\AppUserModelId\<aumid>` has `DisplayName`/`IconUri`/
`CustomActivator` for both), but neither has an entry under
`HKCU\...\Notifications\Settings` (which Windows writes when it accepts a
notification from an app) - while other apps on the machine do, one dated
2026-09-24 (the actual test day). `PushNotifications\ToastEnabled = 1` globally.

Following that lead, `ToastNotificationManagerCompat.CreateToastNotifier().Setting`
was checked directly (added to this probe) and returned **`Enabled`** - no
`DisabledForApplication`, `DisabledForUser`, `DisabledByGroupPolicy`, or
`DisabledByManifest`. This rules out every permission-based block the API
exposes, yet the toast still produces no banner and no Action Center entry.

**Net result:** registration succeeds, the OS reports the notification as
permitted, `.Show()` throws nothing - and still nothing is ever delivered or
recorded. Root cause remains undetermined; deeper candidates (untested):
whether a `ToastNotification.Failed` event handler would reveal a delivery-time
failure, or something specific to this Windows build/session not covered by the
documented API surface.

**Deferred per explicit user instruction 2026-09-24/25** - revisit before Phase 1/the
notification feature is considered done. `NotificationService` (the debounce/hysteresis
logic) is unaffected and fully unit-tested independent of this; only the actual OS
toast delivery is unverified/broken.
