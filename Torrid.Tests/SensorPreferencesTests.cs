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
}
