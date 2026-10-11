using Torrid.App;
using Torrid.Core.Config;

namespace Torrid.Tests;

public class ColorPaletteTests
{
    [Theory]
    [InlineData(ColorZone.Blue, 0x5A, 0xA9, 0xFF)]
    [InlineData(ColorZone.Green, 0x4E, 0xD3, 0x6B)]
    [InlineData(ColorZone.Yellow, 0xFF, 0xC9, 0x3C)]
    [InlineData(ColorZone.Red, 0xFF, 0x5C, 0x4D)]
    public void ForZone_ReturnsExpectedRgb(ColorZone zone, byte r, byte g, byte b)
    {
        var (actualR, actualG, actualB) = ColorPalette.ForZone(zone);

        Assert.Equal((r, g, b), (actualR, actualG, actualB));
    }

    [Fact]
    public void Unknown_IsSlateGray()
    {
        Assert.Equal(((byte)0x8A, (byte)0x94, (byte)0xA6), ColorPalette.Unknown);
    }
}
