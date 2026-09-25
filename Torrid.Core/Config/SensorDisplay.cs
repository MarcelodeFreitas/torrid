namespace Torrid.Core.Config;

using Torrid.Core.Models;

public static class SensorDisplay
{
    public static bool IsVisible(AppConfig config, string sensorId) =>
        config.VisibleSensorIds.Count == 0 || config.VisibleSensorIds.Contains(sensorId);

    public static string ResolveLabel(AppConfig config, SensorReading reading) =>
        config.CustomLabels.TryGetValue(reading.Id, out var custom) && !string.IsNullOrWhiteSpace(custom)
            ? custom
            : reading.Label;

    public static bool NotificationsEnabled(AppConfig config, string sensorId) =>
        !config.NotificationDisabledSensorIds.Contains(sensorId);
}
