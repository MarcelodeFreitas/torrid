using Torrid.App;
using Torrid.App.Tray;
using Torrid.Core.Config;

namespace Torrid.Tests;

public class SingleValueBitmapRendererTests
{
    private static readonly ThresholdBand Cpu = new() { BlueMax = 40, GreenMax = 70, YellowMax = 85 };

    [Fact]
    public void FormatValueText_WhenValuePresent_ReturnsRoundedDegrees()
    {
        var text = SingleValueBitmapRenderer.FormatValueText(67.6);

        Assert.Equal("68", text);
    }

    [Fact]
    public void FormatValueText_WhenValueNull_ReturnsPlaceholder()
    {
        var text = SingleValueBitmapRenderer.FormatValueText(null);

        Assert.Equal("--", text);
    }

    [Fact]
    public void ResolveZone_WhenValueNull_ReturnsBlue()
    {
        var zone = SingleValueBitmapRenderer.ResolveZone(null, Cpu);

        Assert.Equal(ColorZone.Blue, zone);
    }

    [Fact]
    public void ResolveZone_WhenValuePresent_UsesColorThresholdEvaluator()
    {
        var zone = SingleValueBitmapRenderer.ResolveZone(90.0, Cpu);

        Assert.Equal(ColorZone.Red, zone);
    }
}

public class SingleValueBitmapRendererColourTests
{
    private static readonly ThresholdBand Cpu = new() { BlueMax = 40, GreenMax = 70, YellowMax = 85 };

    private static bool ContainsOpaque(System.Drawing.Icon icon, (byte R, byte G, byte B) rgb)
    {
        using var bitmap = icon.ToBitmap();
        for (var x = 0; x < bitmap.Width; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                var p = bitmap.GetPixel(x, y);
                if (p.A == 255 && p.R == rgb.R && p.G == rgb.G && p.B == rgb.B) return true;
            }
        }

        return false;
    }

    [Fact]
    public void Render_WhenValueNull_DrawsInTheUnavailableColour()
    {
        using var icon = new SingleValueBitmapRenderer().Render(null, Cpu, blinkOn: true);

        Assert.True(ContainsOpaque(icon, ColorPalette.Unknown));
        Assert.False(ContainsOpaque(icon, ColorPalette.Blue));
    }

    [Fact]
    public void Render_WhenValuePresent_DrawsInTheZoneColour()
    {
        using var icon = new SingleValueBitmapRenderer().Render(30, Cpu, blinkOn: true);

        Assert.True(ContainsOpaque(icon, ColorPalette.Blue));
    }
}
