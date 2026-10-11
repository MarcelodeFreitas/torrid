namespace Torrid.Core.Config;

public static class SensorPreferences
{
    public static void Update(AppConfig config, string sensorId, Action<SensorPreference> change)
    {
        var preference = config.Sensors.TryGetValue(sensorId, out var existing) ? existing : new SensorPreference();
        change(preference);

        if (preference.IsEmpty)
        {
            config.Sensors.Remove(sensorId);
        }
        else
        {
            config.Sensors[sensorId] = preference;
        }
    }
}
