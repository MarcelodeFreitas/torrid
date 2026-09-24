using Microsoft.Toolkit.Uwp.Notifications;
using System.Runtime.InteropServices;

var isElevated = new System.Security.Principal.WindowsPrincipal(
        System.Security.Principal.WindowsIdentity.GetCurrent())
    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

Console.WriteLine($"IsElevated: {isElevated}");

SetCurrentProcessExplicitAppUserModelID("ToastProbe.App");

try
{
    var notifier = ToastNotificationManagerCompat.CreateToastNotifier();
    Console.WriteLine($"NotificationSetting: {notifier.Setting}");
}
catch (Exception ex)
{
    Console.WriteLine("EXCEPTION getting Setting: " + ex);
}

try
{
    new ToastContentBuilder()
        .AddText("ToastProbe test")
        .AddText($"Elevated={isElevated}")
        .Show();
    Console.WriteLine("Show() completed without throwing.");
}
catch (Exception ex)
{
    Console.WriteLine("EXCEPTION: " + ex);
}

Console.WriteLine("Waiting 5s so the toast has time to appear...");
Thread.Sleep(5000);
Console.WriteLine("Done.");

[DllImport("shell32.dll", SetLastError = true)]
static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);
