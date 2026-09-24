namespace Torrid.Core.Notifications;

using Torrid.Core.Models;

public interface IToastNotifier
{
    void ShowRedAlert(SensorReading reading);
}
