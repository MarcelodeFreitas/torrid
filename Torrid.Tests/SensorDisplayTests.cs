using Torrid.Core.Config;
using Torrid.Core.Models;

namespace Torrid.Tests;

public class SensorDisplayTests
{
    private static SensorReading Cpu(string id = "cpu", string label = "CPU") =>
        new(id, label, SensorKind.CpuPackage, null, 50.0);

    [Fact]
    public void IsVisible_WhenVisibleSensorIdsEmpty_ReturnsTrueForAnyId()
    {
        var config = new AppConfig();

        Assert.True(SensorDisplay.IsVisible(config, "anything"));
    }

    [Fact]
    public void IsVisible_WhenVisibleSensorIdsNonEmpty_ReturnsTrueOnlyForListedIds()
    {
        var config = new AppConfig { VisibleSensorIds = { "cpu" } };

        Assert.True(SensorDisplay.IsVisible(config, "cpu"));
        Assert.False(SensorDisplay.IsVisible(config, "gpu0-core"));
    }

    [Fact]
    public void ResolveLabel_WhenNoCustomLabel_ReturnsReadingLabel()
    {
        var config = new AppConfig();

        var resolved = SensorDisplay.ResolveLabel(config, Cpu());

        Assert.Equal("CPU", resolved);
    }

    [Fact]
    public void ResolveLabel_WhenCustomLabelSet_ReturnsCustomLabel()
    {
        var config = new AppConfig { CustomLabels = { ["cpu"] = "5950X" } };

        var resolved = SensorDisplay.ResolveLabel(config, Cpu());

        Assert.Equal("5950X", resolved);
    }

    [Fact]
    public void ResolveLabel_WhenCustomLabelIsWhitespace_FallsBackToReadingLabel()
    {
        var config = new AppConfig { CustomLabels = { ["cpu"] = "   " } };

        var resolved = SensorDisplay.ResolveLabel(config, Cpu());

        Assert.Equal("CPU", resolved);
    }

    [Fact]
    public void NotificationsEnabled_ByDefault_ReturnsTrue()
    {
        var config = new AppConfig();

        Assert.True(SensorDisplay.NotificationsEnabled(config, "gpu1-hotspot"));
    }

    [Fact]
    public void NotificationsEnabled_WhenSensorIdDisabled_ReturnsFalseOnlyForThatId()
    {
        var config = new AppConfig { NotificationDisabledSensorIds = { "gpu1-hotspot" } };

        Assert.False(SensorDisplay.NotificationsEnabled(config, "gpu1-hotspot"));
        Assert.True(SensorDisplay.NotificationsEnabled(config, "gpu0-hotspot"));
    }
}
