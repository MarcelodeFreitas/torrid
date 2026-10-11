namespace Torrid.Core.Config;

using Torrid.Core.Models;

public static class SensorDisplay
{
    public static bool IsShownInWindow(AppConfig config, SensorIdentity sensor) =>
        Preference(config, sensor.Id)?.ShowInWindow ?? SensorDefaults.ShownInWindow(sensor.Kind);

    public static bool IsShownInTray(AppConfig config, SensorIdentity sensor) =>
        Preference(config, sensor.Id)?.ShowInTray ?? SensorDefaults.ShownInTray(sensor.Kind);

    public static bool NotificationsEnabled(AppConfig config, SensorIdentity sensor) =>
        Preference(config, sensor.Id)?.Notify ?? SensorDefaults.Notify(sensor.Kind);

    public static string ResolveLabel(AppConfig config, SensorIdentity sensor) =>
        Preference(config, sensor.Id)?.Label is { } label && !string.IsNullOrWhiteSpace(label) ? label : sensor.DefaultLabel;

    public static bool IsShownInWindow(AppConfig config, SensorReading reading) =>
        IsShownInWindow(config, SensorIdentity.FromReading(reading));

    public static bool IsShownInTray(AppConfig config, SensorReading reading) =>
        IsShownInTray(config, SensorIdentity.FromReading(reading));

    public static bool NotificationsEnabled(AppConfig config, SensorReading reading) =>
        NotificationsEnabled(config, SensorIdentity.FromReading(reading));

    public static string ResolveLabel(AppConfig config, SensorReading reading) =>
        ResolveLabel(config, SensorIdentity.FromReading(reading));

    private static SensorPreference? Preference(AppConfig config, string sensorId) =>
        config.Sensors.TryGetValue(sensorId, out var preference) ? preference : null;
}
