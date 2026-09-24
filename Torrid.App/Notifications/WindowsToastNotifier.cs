namespace Torrid.App.Notifications;

using Microsoft.Toolkit.Uwp.Notifications;
using Torrid.Core.Models;
using Torrid.Core.Notifications;

public sealed class WindowsToastNotifier : IToastNotifier
{
    public void ShowRedAlert(SensorReading reading)
    {
        var value = reading.ValueCelsius is double v ? $"{v:F0}°C" : "unknown";
        new ToastContentBuilder()
            .AddText("Torrid — temperature critical")
            .AddText($"{reading.Label} is at {value}")
            .Show();
    }
}
