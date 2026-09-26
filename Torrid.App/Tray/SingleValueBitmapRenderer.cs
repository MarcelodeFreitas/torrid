namespace Torrid.App.Tray;

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Torrid.Core.Config;

public sealed class SingleValueBitmapRenderer
{
    private const int Size = 32;
    private const int TextMargin = 2;

    public Icon Render(double? valueCelsius, ThresholdBand thresholds, bool blinkOn)
    {
        var zone = ResolveZone(valueCelsius, thresholds);
        var isRed = zone == ColorZone.Red;
        var text = FormatValueText(valueCelsius);

        using var bitmap = new Bitmap(Size, Size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            if (isRed && !blinkOn)
            {
                using var dimBrush = new SolidBrush(Color.FromArgb(70, 244, 67, 54));
                g.FillEllipse(dimBrush, 0, 0, Size, Size);
            }

            var (r, gC, b) = ColorPalette.ForZone(zone);
            using var brush = new SolidBrush(Color.FromArgb(r, gC, b));
            using var format = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            using var font = CreateFittingFont(g, text, format, Size - TextMargin * 2, Size - TextMargin * 2);
            g.DrawString(text, font, brush, new RectangleF(0, 0, Size, Size), format);
        }

        var handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }

    public static string FormatValueText(double? valueCelsius) =>
        valueCelsius is double v ? $"{v:F0}" : "--";

    public static ColorZone ResolveZone(double? valueCelsius, ThresholdBand thresholds) =>
        valueCelsius is double v ? ColorThresholdEvaluator.GetColor(v, thresholds) : ColorZone.Blue;

    private static Font CreateFittingFont(Graphics g, string text, StringFormat format, float maxWidth, float maxHeight)
    {
        for (var size = 16; size >= 8; size--)
        {
            var candidate = new Font("Segoe UI", size, FontStyle.Bold);
            var measured = g.MeasureString(text, candidate, PointF.Empty, format);
            if (measured.Width <= maxWidth && measured.Height <= maxHeight)
            {
                return candidate;
            }
            candidate.Dispose();
        }

        return new Font("Segoe UI", 8, FontStyle.Bold);
    }
}
