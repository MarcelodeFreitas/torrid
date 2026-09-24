namespace Torrid.App.Tray;

using System.Drawing;
using System.Drawing.Drawing2D;
using Torrid.Core.Config;
using Torrid.Core.Models;

public sealed class TrayBitmapRenderer
{
    private const int Size = 32;

    public Icon Render(IReadOnlyList<SensorReading> readings, IReadOnlyDictionary<SensorKind, ThresholdBand> thresholds, bool blinkOn)
    {
        var cpu = readings.FirstOrDefault(r => r.Kind == SensorKind.CpuPackage);
        var hottestGpu = readings
            .Where(r => r.Kind == SensorKind.GpuHotSpot)
            .OrderByDescending(r => r.ValueCelsius ?? double.MinValue)
            .FirstOrDefault();

        var anyRed = readings.Any(r =>
            r.ValueCelsius is double v &&
            thresholds.TryGetValue(r.Kind, out var band) &&
            ColorThresholdEvaluator.GetColor(v, band) == ColorZone.Red);

        using var bitmap = new Bitmap(Size, Size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            if (anyRed && !blinkOn)
            {
                using var dimBrush = new SolidBrush(Color.FromArgb(70, 244, 67, 54));
                g.FillEllipse(dimBrush, 0, 0, Size, Size);
            }

            DrawHalf(g, top: true, cpu, thresholds);
            DrawHalf(g, top: false, hottestGpu, thresholds);
        }

        var handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }

    private static void DrawHalf(Graphics g, bool top, SensorReading? reading, IReadOnlyDictionary<SensorKind, ThresholdBand> thresholds)
    {
        var value = reading?.ValueCelsius;
        var zone = value is double v && reading is not null && thresholds.TryGetValue(reading.Kind, out var band)
            ? ColorThresholdEvaluator.GetColor(v, band)
            : ColorZone.Blue;
        var (r, gC, b) = ColorPalette.ForZone(zone);

        using var brush = new SolidBrush(Color.FromArgb(r, gC, b));
        using var font = new Font("Segoe UI", 11, System.Drawing.FontStyle.Bold);
        var text = value is double v2 ? $"{v2:F0}" : "--";
        var y = top ? 0 : Size / 2;
        var rect = new RectangleF(0, y, Size, Size / 2f);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, brush, rect, format);
    }
}
