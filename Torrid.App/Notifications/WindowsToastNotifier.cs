namespace Torrid.App.Notifications;

using Microsoft.Toolkit.Uwp.Notifications;
using Torrid.Core.Models;
using Torrid.Core.Notifications;

/// <summary>
/// Shows toasts under the process AppUserModelID (<see cref="AppIdentity.Aumid"/>). Windows only delivers them when the
/// Start Menu shortcut from <see cref="StartMenuShortcut.EnsureForToasts"/> exists. Never throws.
/// </summary>
public sealed class WindowsToastNotifier : IToastNotifier
{
    public void ShowRedAlert(SensorReading reading)
    {
        var value = reading.ValueCelsius is double v ? $"{v:F0}°C" : "unknown";
        Show("Torrid — temperature critical", $"{reading.Label} is at {value}");
    }

    public void ShowTestNotification() => Show("Torrid test notification", "Alerts are working.");

    private static void Show(string title, string line)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(line)
                .Show();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Toast '{title}' failed: {ex}");
        }
    }
}
