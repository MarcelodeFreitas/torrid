using Torrid.App;
using Torrid.Core.Config;

namespace Torrid.Tests;

public class ColorPaletteTests
{
    [Theory]
    [InlineData(ColorZone.Blue, 0x42, 0x9C, 0xF5)]
    [InlineData(ColorZone.Green, 0x4C, 0xAF, 0x50)]
    [InlineData(ColorZone.Yellow, 0xFF, 0xC1, 0x07)]
    [InlineData(ColorZone.Red, 0xF4, 0x43, 0x36)]
    public void ForZone_ReturnsExpectedRgb(ColorZone zone, byte r, byte g, byte b)
    {
        var (actualR, actualG, actualB) = ColorPalette.ForZone(zone);

        Assert.Equal((r, g, b), (actualR, actualG, actualB));
    }
}
