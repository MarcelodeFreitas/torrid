using Torrid.Core.Overlay;

namespace Torrid.Tests;

public class OverlayAppearanceTests
{
    [Theory]
    [InlineData(9, 10)]
    [InlineData(49, 48)]
    [InlineData(20, 20)]
    public void ClampTextSize_KeepsTheRange(int size, int expected)
    {
        Assert.Equal(expected, OverlayAppearance.ClampTextSize(size));
    }

    [Theory]
    [InlineData("#FFFFFF", true)]
    [InlineData("#ff00aa", true)]
    [InlineData("FFFFFF", false)]
    [InlineData("#FFF", false)]
    [InlineData("#GGGGGG", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("red", false)]
    public void IsValidColor_AcceptsOnlySixDigitHex(string? hex, bool expected)
    {
        Assert.Equal(expected, OverlayAppearance.IsValidColor(hex));
    }

    [Fact]
    public void Presets_AreAllValidColors_AndIncludeTheDefault()
    {
        Assert.All(OverlayAppearance.Presets, preset => Assert.True(OverlayAppearance.IsValidColor(preset.Hex)));
        Assert.Contains(OverlayAppearance.Presets, preset => preset.Hex == OverlayAppearance.DefaultColor);
    }
}
