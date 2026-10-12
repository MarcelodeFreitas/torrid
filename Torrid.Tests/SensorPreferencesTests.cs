using Torrid.Core.Config;

namespace Torrid.Tests;

public class SensorPreferencesTests
{
    [Fact]
    public void Update_CreatesEntryWhenMissing()
    {
        var config = new AppConfig();

        SensorPreferences.Update(config, "gpu0-vram", p => p.ShowInTray = true);

        Assert.True(config.Sensors["gpu0-vram"].ShowInTray);
    }

    [Fact]
    public void Update_RemovesEntryOnceItBecomesEmpty()
    {
        var config = new AppConfig();
        SensorPreferences.Update(config, "cpu", p => p.Label = "5950X");

        SensorPreferences.Update(config, "cpu", p => p.Label = null);

        Assert.False(config.Sensors.ContainsKey("cpu"));
    }

    [Fact]
    public void IsEmpty_TreatsWhitespaceLabelAsEmpty()
    {
        Assert.True(new SensorPreference { Label = "   " }.IsEmpty);
        Assert.False(new SensorPreference { Label = "x" }.IsEmpty);
        Assert.False(new SensorPreference { Notify = false }.IsEmpty);
    }

    [Fact]
    public void IsEmpty_IsFalseWhenOnlyAThresholdBandIsSet()
    {
        var pref = new SensorPreference { Thresholds = new ThresholdBand { BlueMax = 1, GreenMax = 2, YellowMax = 3 } };

        Assert.False(pref.IsEmpty);
    }

    [Fact]
    public void Update_KeepsAPreferenceThatOnlyHasABand_AndRemovesItOnceTheBandGoes()
    {
        var config = new AppConfig();
        SensorPreferences.Update(config, "cpu", p => p.Thresholds = new ThresholdBand { BlueMax = 1, GreenMax = 2, YellowMax = 3 });
        Assert.True(config.Sensors.ContainsKey("cpu"));

        SensorPreferences.Update(config, "cpu", p => p.Thresholds = null);

        Assert.False(config.Sensors.ContainsKey("cpu"));
    }
}
