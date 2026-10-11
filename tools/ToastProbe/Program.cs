using System.Runtime.InteropServices;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;
using ToastProbe;

const string Aumid = "ToastProbe.App";

var withShortcut = args.Contains("--shortcut");
var shortcutPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    @"Microsoft\Windows\Start Menu\Programs\ToastProbe.lnk");

Console.WriteLine($"Mode: {(withShortcut ? "WITH Start Menu shortcut carrying the AUMID" : "registry-only AUMID (what Torrid does today)")}");

SetCurrentProcessExplicitAppUserModelID(Aumid);

if (withShortcut)
{
    ShortcutHelper.Create(shortcutPath, Environment.ProcessPath!, Aumid);
    Console.WriteLine($"Created shortcut: {shortcutPath}");
    Thread.Sleep(2000);
}

var before = ReadLastAdded();
Console.WriteLine($"LastNotificationAddedTime before: {before ?? "(no key)"}");

try
{
    new ToastContentBuilder()
        .AddText("ToastProbe test")
        .AddText(withShortcut ? "with Start Menu shortcut" : "registry-only AUMID")
        .Show(toast =>
        {
            toast.Failed += (_, e) => Console.WriteLine($"EVENT Failed: 0x{e.ErrorCode?.HResult:X8} {e.ErrorCode?.Message}");
            toast.Dismissed += (_, e) => Console.WriteLine($"EVENT Dismissed: {e.Reason}");
            toast.Activated += (_, _) => Console.WriteLine("EVENT Activated");
        });
    Console.WriteLine("Show() returned without throwing.");
}
catch (Exception ex)
{
    Console.WriteLine("EXCEPTION: " + ex);
}

Thread.Sleep(4000);
var after = ReadLastAdded();
Console.WriteLine($"LastNotificationAddedTime after:  {after ?? "(no key)"}");
Console.WriteLine(after is not null && after != before
    ? "RESULT: Windows ACCEPTED the notification."
    : "RESULT: Windows did NOT record any notification for this app id.");

static string? ReadLastAdded()
{
    using var key = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\{Aumid}");
    return key?.GetValue("LastNotificationAddedTime")?.ToString();
}

[DllImport("shell32.dll", SetLastError = true)]
static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);
