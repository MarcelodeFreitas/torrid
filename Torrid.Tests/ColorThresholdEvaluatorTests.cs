using Torrid.Core.Config;

namespace Torrid.Tests;

public class ColorThresholdEvaluatorTests
{
    private static readonly ThresholdBand Cpu = new() { BlueMax = 40, GreenMax = 70, YellowMax = 85 };

    [Theory]
    [InlineData(39.9, ColorZone.Blue)]
    [InlineData(40.0, ColorZone.Green)]
    [InlineData(69.9, ColorZone.Green)]
    [InlineData(70.0, ColorZone.Yellow)]
    [InlineData(84.9, ColorZone.Yellow)]
    [InlineData(85.0, ColorZone.Yellow)]
    [InlineData(85.1, ColorZone.Red)]
    [InlineData(120.0, ColorZone.Red)]
    public void GetColor_ReturnsExpectedZone_AtEachBoundary(double value, ColorZone expected)
    {
        var actual = ColorThresholdEvaluator.GetColor(value, Cpu);

        Assert.Equal(expected, actual);
    }
}
