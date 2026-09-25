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
