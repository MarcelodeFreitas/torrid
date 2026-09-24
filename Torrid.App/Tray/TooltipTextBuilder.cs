namespace Torrid.App.Tray;

using Torrid.Core.Models;

public static class TooltipTextBuilder
{
    // NotifyIcon.Text throws ArgumentOutOfRangeException above 127 chars on Vista+.
    private const int MaxLength = 127;

    public static string Build(IReadOnlyList<SensorReading> readings)
    {
        var text = string.Join(
            Environment.NewLine,
            readings.Select(r => $"{r.Label}: {(r.ValueCelsius is double v ? $"{v:F0}°C" : "--")}"));

        return text.Length <= MaxLength ? text : text[..(MaxLength - 3)] + "...";
    }
}
