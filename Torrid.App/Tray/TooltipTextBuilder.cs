namespace Torrid.App.Tray;

using Torrid.Core.Models;
using Torrid.Core.Sensors;

public static class TooltipTextBuilder
{
    // NotifyIcon.Text throws ArgumentOutOfRangeException above 127 chars on Vista+.
    private const int MaxLength = 127;

    public static string Build(IReadOnlyList<SensorReading> readings) => Build(readings, HardwareStatus.FullAccess);

    /// <summary>
    /// One line per reading: "{label}: 59°C" (or "--"), or "{label}: paused ({product} is running)" while monitoring is
    /// paused for an anti-cheat.
    /// </summary>
    public static string Build(IReadOnlyList<SensorReading> readings, HardwareStatus status)
    {
        var pausedText = status.Level == HardwareAccessLevel.Paused ? $"paused ({DescribeCulprit(status.AntiCheat)} is running)" : null;

        var text = string.Join(
            Environment.NewLine,
            readings.Select(r => $"{r.Label}: {pausedText ?? (r.ValueCelsius is double v ? $"{v:F0}°C" : "--")}"));

        return text.Length <= MaxLength ? text : text[..(MaxLength - 3)] + "...";
    }

    private static string DescribeCulprit(AntiCheatMatch? match) => match switch
    {
        null => "an anti-cheat",
        { Product: "Custom" } => match.ProcessName,
        _ => match.Product,
    };
}
