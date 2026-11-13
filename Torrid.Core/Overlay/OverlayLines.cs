namespace Torrid.Core.Overlay;

using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

public static class OverlayLines
{
    public const string NoSensorsText = "No sensors selected";

    public static IReadOnlyList<string> Build(AppConfig config, IReadOnlyList<SensorReading> readings, HardwareStatus status)
    {
        var lines = readings
            .Where(r => r is not null && SensorDisplay.IsShownInOverlay(config, r))
            .Select(r => $"{LabelText(config, r)}: {ValueText(r, status)}")
            .ToList();
        if (lines.Count == 0) return [NoSensorsText];
        return lines;
    }

    /// <summary>A renamed sensor keeps its real name in brackets, so the overlay still says which sensor it is.</summary>
    private static string LabelText(AppConfig config, SensorReading reading)
    {
        var label = SensorDisplay.ResolveLabel(config, reading);
        return label == reading.Label ? label : $"{label} ({reading.Label})";
    }

    private static string ValueText(SensorReading reading, HardwareStatus status) =>
        status.Level == HardwareAccessLevel.Paused ? "paused"
        : reading.ValueCelsius is double v ? $"{v:F0}°C"
        : "--";
}
