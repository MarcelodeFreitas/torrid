using Torrid.Core.Config;
using Torrid.Core.Models;

namespace Torrid.Tests;

public class ThresholdResolverTests
{
    private static ThresholdBand Band(double blue, double green, double yellow) =>
        new() { BlueMax = blue, GreenMax = green, YellowMax = yellow };

    [Fact]
    public void For_OwnBandWinsOverKindDefault()
    {
        var config = new AppConfig();
        var own = Band(10, 20, 30);
        config.Sensors["cpu"] = new SensorPreference { Thresholds = own };

        Assert.Same(own, ThresholdResolver.For(config, "cpu", SensorKind.CpuPackage));
    }

    [Fact]
    public void For_WithoutOwnBand_UsesTheKindBandFromTheConfig()
    {
        var config = new AppConfig();
        var kindBand = Band(11, 22, 33);
        config.Thresholds[SensorKind.CpuPackage] = kindBand;
        config.Sensors["cpu"] = new SensorPreference { Label = "x" };

        Assert.Same(kindBand, ThresholdResolver.For(config, "cpu", SensorKind.CpuPackage));
        Assert.Same(kindBand, ThresholdResolver.For(config, "unknown", SensorKind.CpuPackage));
    }

    [Fact]
    public void For_WhenKindMissingFromConfig_UsesTheBuiltInDefault()
    {
        var config = new AppConfig { Thresholds = new Dictionary<SensorKind, ThresholdBand>() };

        var band = ThresholdResolver.For(config, "hw:x", SensorKind.Motherboard);

        Assert.Equal(AppConfig.DefaultThresholds()[SensorKind.Motherboard].YellowMax, band.YellowMax);
    }

    [Fact]
    public void For_ToleratesNullDictionariesAndNullEntries()
    {
        var config = new AppConfig { Thresholds = null!, Sensors = null! };
        Assert.NotNull(ThresholdResolver.For(config, "cpu", SensorKind.CpuPackage));
        Assert.False(ThresholdResolver.HasCustom(config, "cpu"));

        config = new AppConfig();
        config.Thresholds[SensorKind.CpuPackage] = null!;
        config.Sensors["cpu"] = null!;
        var band = ThresholdResolver.For(config, "cpu", SensorKind.CpuPackage);
        Assert.Equal(85, band.YellowMax);
    }

    [Fact]
    public void For_InvalidOwnBand_IsIgnored()
    {
        var config = new AppConfig();
        config.Sensors["cpu"] = new SensorPreference { Thresholds = Band(50, 40, 30) };

        var band = ThresholdResolver.For(config, "cpu", SensorKind.CpuPackage);

        Assert.Same(config.Thresholds[SensorKind.CpuPackage], band);
        Assert.False(ThresholdResolver.HasCustom(config, "cpu"));
    }

    [Fact]
    public void For_Overloads_AgreeWithTheIdAndKindOverload()
    {
        var config = new AppConfig();
        var own = Band(1, 2, 3);
        config.Sensors["gpu0-core"] = new SensorPreference { Thresholds = own };
        var reading = new SensorReading("gpu0-core", "GPU0 Core", SensorKind.GpuCore, 0, 50);
        var identity = SensorIdentity.FromReading(reading);

        Assert.Same(own, ThresholdResolver.For(config, reading));
        Assert.Same(own, ThresholdResolver.For(config, identity));
    }

    [Fact]
    public void HasCustom_TrueOnlyForAValidOwnBand()
    {
        var config = new AppConfig();
        config.Sensors["a"] = new SensorPreference { Thresholds = Band(1, 2, 3) };
        config.Sensors["b"] = new SensorPreference { Label = "B" };

        Assert.True(ThresholdResolver.HasCustom(config, "a"));
        Assert.False(ThresholdResolver.HasCustom(config, "b"));
        Assert.False(ThresholdResolver.HasCustom(config, "missing"));
    }

    [Theory]
    [InlineData(0, 1, 2, true)]
    [InlineData(40, 70, 150, true)]
    [InlineData(-1, 1, 2, false)]
    [InlineData(10, 10, 20, false)]
    [InlineData(10, 20, 20, false)]
    [InlineData(10, 20, 151, false)]
    [InlineData(double.NaN, 20, 30, false)]
    [InlineData(10, 20, double.PositiveInfinity, false)]
    public void IsValid_AppliesTheOrderingAndRangeRule(double blue, double green, double yellow, bool expected)
    {
        Assert.Equal(expected, ThresholdResolver.IsValid(Band(blue, green, yellow)));
    }

    [Fact]
    public void IsValid_Null_IsFalse() => Assert.False(ThresholdResolver.IsValid(null));

    [Fact]
    public void SetCustom_StoresTheBandAndLeavesOtherFieldsAlone()
    {
        var config = new AppConfig();
        config.Sensors["cpu"] = new SensorPreference { Label = "5950X", ShowInTray = true };

        ThresholdResolver.SetCustom(config, "cpu", Band(30, 60, 90));

        var pref = config.Sensors["cpu"];
        Assert.Equal("5950X", pref.Label);
        Assert.True(pref.ShowInTray);
        Assert.Equal(90, pref.Thresholds!.YellowMax);
        Assert.True(ThresholdResolver.HasCustom(config, "cpu"));
    }

    [Fact]
    public void SetCustom_CreatesTheEntryWhenMissing()
    {
        var config = new AppConfig();

        ThresholdResolver.SetCustom(config, "gpu0-core", Band(30, 60, 90));

        Assert.Equal(60, config.Sensors["gpu0-core"].Thresholds!.GreenMax);
    }

    [Fact]
    public void SetCustom_RejectsAnInvalidBand()
    {
        var config = new AppConfig();
        ThresholdResolver.SetCustom(config, "cpu", Band(30, 60, 90));

        ThresholdResolver.SetCustom(config, "cpu", Band(90, 60, 30));
        ThresholdResolver.SetCustom(config, "other", Band(90, 60, 30));

        Assert.Equal(90, config.Sensors["cpu"].Thresholds!.YellowMax);
        Assert.False(config.Sensors.ContainsKey("other"));
    }

    [Fact]
    public void ClearCustom_RemovesTheEntryWhenItBecomesEmpty()
    {
        var config = new AppConfig();
        ThresholdResolver.SetCustom(config, "cpu", Band(30, 60, 90));

        ThresholdResolver.ClearCustom(config, "cpu");

        Assert.False(config.Sensors.ContainsKey("cpu"));
        Assert.Same(config.Thresholds[SensorKind.CpuPackage], ThresholdResolver.For(config, "cpu", SensorKind.CpuPackage));
    }

    [Fact]
    public void ClearCustom_KeepsTheEntryWhenOtherFieldsRemain()
    {
        var config = new AppConfig();
        config.Sensors["cpu"] = new SensorPreference { Label = "5950X", Thresholds = Band(30, 60, 90) };

        ThresholdResolver.ClearCustom(config, "cpu");

        Assert.Equal("5950X", config.Sensors["cpu"].Label);
        Assert.Null(config.Sensors["cpu"].Thresholds);
    }

    [Fact]
    public void ClearCustom_OnAnUnknownSensor_DoesNothing()
    {
        var config = new AppConfig();

        ThresholdResolver.ClearCustom(config, "nope");

        Assert.Empty(config.Sensors);
    }

    [Fact]
    public void ThresholdsListView_DefaultsToFalse() => Assert.False(new AppConfig().ThresholdsListView);
}
