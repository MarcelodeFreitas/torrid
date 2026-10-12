using Torrid.App.Tray;
using Torrid.Core.Config;
using Torrid.Core.Models;

namespace Torrid.Tests;

public class TrayThresholdsTests
{
    private static SensorReading Reading(string id, double? value) =>
        new(id, id, SensorKind.OtherTemp, null, value);

    [Fact]
    public void AnyRed_UsesEachSensorsOwnBand()
    {
        var config = new AppConfig();
        ThresholdResolver.SetCustom(config, "hw:a", new ThresholdBand { BlueMax = 10, GreenMax = 20, YellowMax = 30 });

        // 60 is red for the custom band but yellow for the OtherTemp default (85).
        Assert.True(TrayThresholds.AnyRed(config, new[] { Reading("hw:a", 60) }));
        Assert.False(TrayThresholds.AnyRed(config, new[] { Reading("hw:b", 60) }));
    }

    [Fact]
    public void AnyRed_IgnoresNullValuesAndEmptyLists()
    {
        var config = new AppConfig();

        Assert.False(TrayThresholds.AnyRed(config, new[] { Reading("hw:a", null) }));
        Assert.False(TrayThresholds.AnyRed(config, Array.Empty<SensorReading>()));
    }
}
