namespace Torrid.App.Tray;

using Torrid.Core.Config;
using Torrid.Core.Models;

/// <summary>The tray's band lookup, kept free of <c>NotifyIcon</c> so it can be unit tested.</summary>
public static class TrayThresholds
{
    /// <summary>True when any reading with a value is in the red zone of its sensor's own effective band.</summary>
    public static bool AnyRed(AppConfig config, IEnumerable<SensorReading> readings) =>
        readings.Any(r =>
            r.ValueCelsius is double value &&
            ColorThresholdEvaluator.GetColor(value, ThresholdResolver.For(config, r)) == ColorZone.Red);
}
