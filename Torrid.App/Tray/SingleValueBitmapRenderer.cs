namespace Torrid.App.Tray;

using System.Drawing;
using System.Drawing.Drawing2D;
using Torrid.Core.Config;

public sealed class SingleValueBitmapRenderer
{
    private const int Size = 32;

    public Icon Render(double? valueCelsius, ThresholdBand thresholds, bool blinkOn)
    {
        var zone = ResolveZone(valueCelsius, thresholds);
        var isRed = zone == ColorZone.Red;

        using var bitmap = new Bitmap(Size, Size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            if (isRed && !blinkOn)
            {
                using var dimBrush = new SolidBrush(Color.FromArgb(70, 244, 67, 54));
                g.FillEllipse(dimBrush, 0, 0, Size, Size);
            }

            var (r, gC, b) = ColorPalette.ForZone(zone);
            using var brush = new SolidBrush(Color.FromArgb(r, gC, b));
            using var font = new Font("Segoe UI", 16, System.Drawing.FontStyle.Bold);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(FormatValueText(valueCelsius), font, brush, new RectangleF(0, 0, Size, Size), format);
        }

        var handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }

    public static string FormatValueText(double? valueCelsius) =>
        valueCelsius is double v ? $"{v:F0}" : "--";

    public static ColorZone ResolveZone(double? valueCelsius, ThresholdBand thresholds) =>
        valueCelsius is double v ? ColorThresholdEvaluator.GetColor(v, thresholds) : ColorZone.Blue;
}
