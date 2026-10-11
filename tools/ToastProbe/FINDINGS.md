# Toast notifications - RESOLVED: Windows needs a Start Menu shortcut carrying the AppUserModelID

## Symptom (Phase 1 onward)

`ToastContentBuilder().Show()` (Microsoft.Toolkit.Uwp.Notifications 7.1.3) returned without throwing, the OS
reported `NotificationSetting: Enabled`, the app id registered correctly under
`HKCU\Software\Classes\AppUserModelId\<id>` - and yet no banner and no Action Center entry ever appeared, elevated
or not. Windows never wrote `HKCU\...\Notifications\Settings\<id>\LastNotificationAddedTime`, which it does write for
every toast it actually accepts (that key is the machine-checkable "accepted" signal used below).

## Cause (proven 2026-10-11)

On this Windows build an unpackaged desktop app's toasts are silently dropped unless a **Start Menu shortcut that
carries the same AppUserModelID** exists. The toolkit's registry registration alone is not enough.

Controlled experiment with this probe (`ToastProbe.App`):

| State | Result |
|---|---|
| Registry registration only (what Torrid did) | not accepted, every time |
| + Start Menu shortcut with `System.AppUserModel.ID = ToastProbe.App` (`--shortcut`) | **accepted** (`LastNotificationAddedTime` written) |
| Shortcut deleted again | not accepted |

Windows needs a short while (more than ~6 seconds, less than a minute) to pick up a brand-new shortcut, so the very
first toast right after the shortcut is first created can still be dropped; later ones are delivered.

## Fix (Torrid)

Create/refresh `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Torrid.lnk` at startup, pointing at the running exe and
carrying Torrid's AppUserModelID (Debug builds use a separate id and shortcut so they don't fight the published app).
`ShortcutHelper.cs` here is the reference interop implementation. Settings has a "Send test notification" button so the
result can be checked at any time.

## Re-running the probe

```
dotnet build tools\ToastProbe -c Release
tools\ToastProbe\bin\Release\net8.0-windows10.0.17763.0\ToastProbe.exe              # registry-only baseline
tools\ToastProbe\bin\Release\net8.0-windows10.0.17763.0\ToastProbe.exe --shortcut   # creates the shortcut, then toasts
```
The probe prints `RESULT: Windows ACCEPTED the notification.` when the registry signal changes. Delete
`%APPDATA%\Microsoft\Windows\Start Menu\Programs\ToastProbe.lnk` afterwards to clean up.
