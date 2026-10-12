namespace Torrid.Core.Config;

using System.Diagnostics.CodeAnalysis;
using Torrid.Core.Models;

/// <summary>
/// Resolves the colour/alert band of one sensor: its own band when it has a valid one, otherwise the default of its
/// kind (<see cref="AppConfig.Thresholds"/>), otherwise the built-in default. Every consumer (Home, Sensors, tray,
/// alerts) goes through here so a sensor gets the same band everywhere. UI thread only, like all threshold reads.
/// </summary>
public static class ThresholdResolver
{
    public const double MaxLimit = 150;

    // Shared, read-only fallback; never handed out for editing.
    private static readonly Dictionary<SensorKind, ThresholdBand> BuiltInDefaults = AppConfig.DefaultThresholds();

    /// <summary>The single band validity rule: <c>0 &lt;= Blue &lt; Green &lt; Yellow &lt;= 150</c>.</summary>
    public static bool IsValid([NotNullWhen(true)] ThresholdBand? band) =>
        band is not null && 0 <= band.BlueMax && band.BlueMax < band.GreenMax && band.GreenMax < band.YellowMax && band.YellowMax <= MaxLimit;

    /// <summary>The effective band: own valid band, else the kind default from the config, else the built-in default. Never null.</summary>
    public static ThresholdBand For(AppConfig config, string sensorId, SensorKind kind)
    {
        var own = OwnBand(config, sensorId);
        if (own is not null) return own;

        if (config.Thresholds is not null && config.Thresholds.TryGetValue(kind, out var kindBand) && kindBand is not null)
        {
            return kindBand;
        }

        return BuiltInDefaults.TryGetValue(kind, out var builtIn) ? builtIn : BuiltInDefaults[SensorKind.OtherTemp];
    }

    public static ThresholdBand For(AppConfig config, SensorReading reading) => For(config, reading.Id, reading.Kind);

    public static ThresholdBand For(AppConfig config, SensorIdentity sensor) => For(config, sensor.Id, sensor.Kind);

    /// <summary>True when the sensor has its own valid band (an invalid one is ignored, as in <see cref="For(AppConfig, string, SensorKind)"/>).</summary>
    public static bool HasCustom(AppConfig config, string sensorId) => OwnBand(config, sensorId) is not null;

    /// <summary>Gives the sensor its own band (a copy). An invalid band is ignored.</summary>
    public static void SetCustom(AppConfig config, string sensorId, ThresholdBand band)
    {
        if (!IsValid(band)) return;

        var copy = new ThresholdBand { BlueMax = band.BlueMax, GreenMax = band.GreenMax, YellowMax = band.YellowMax };
        SensorPreferences.Update(config, sensorId, p => p.Thresholds = copy);
    }

    /// <summary>Removes the sensor's own band so it follows its kind default again; drops the preference if nothing else is left in it.</summary>
    public static void ClearCustom(AppConfig config, string sensorId)
    {
        if (config.Sensors is null || !config.Sensors.ContainsKey(sensorId)) return;

        SensorPreferences.Update(config, sensorId, p => p.Thresholds = null);
    }

    private static ThresholdBand? OwnBand(AppConfig config, string sensorId)
    {
        if (config.Sensors is null || sensorId is null) return null;

        return config.Sensors.TryGetValue(sensorId, out var pref) && pref is not null && IsValid(pref.Thresholds)
            ? pref.Thresholds
            : null;
    }
}
