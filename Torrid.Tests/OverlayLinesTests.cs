using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Overlay;
using Torrid.Core.Sensors;

namespace Torrid.Tests;

public class OverlayLinesTests
{
    private static readonly HardwareStatus Full = new(HardwareAccessLevel.Full, null);
    private static readonly HardwareStatus Paused = new(HardwareAccessLevel.Paused, null);
    private static readonly SensorReading Cpu = new("cpu", "CPU", SensorKind.CpuPackage, null, 59.4);
    private static readonly SensorReading Core = new("gpu0-core", "GPU0 Core", SensorKind.GpuCore, 0, 71.0);
    private static readonly SensorReading Hot = new("gpu0-hotspot", "GPU0 Hot", SensorKind.GpuHotSpot, 0, null);

    [Fact]
    public void Build_ListsOnlyOverlaySensors_InReadingOrder_WithResolvedLabels()
    {
        var config = new AppConfig();
        SensorPreferences.Update(config, "gpu0-core", p => { p.ShowInOverlay = true; p.Label = "MSI Core"; });

        var lines = OverlayLines.Build(config, new[] { Core, Cpu }, Full);

        Assert.Equal(new[] { "MSI Core (GPU0 Core): 71°C", "CPU: 59°C" }, lines);
    }

    [Fact]
    public void Build_ShowsTheRealSensorName_InBrackets_ForARenamedAddedSensor()
    {
        var config = new AppConfig();
        var added = new SensorReading("hw:/amdcpu/0/temperature/3", "CPU Package", SensorKind.OtherTemp, null, 40.0);
        SensorPreferences.Update(config, added.Id, p => { p.ShowInOverlay = true; p.Label = "Chip"; });

        var lines = OverlayLines.Build(config, new[] { added }, Full);

        Assert.Equal(new[] { "Chip (CPU Package): 40°C" }, lines);
    }

    [Fact]
    public void Build_FormatsRoundedCelsius_AndDashesForNull()
    {
        var lines = OverlayLines.Build(new AppConfig(), new[] { Cpu, Hot }, Full);

        Assert.Equal(new[] { "CPU: 59°C", "GPU0 Hot: --" }, lines);
    }

    [Fact]
    public void Build_WhenPaused_SaysPaused()
    {
        var lines = OverlayLines.Build(new AppConfig(), new[] { Cpu, Hot }, Paused);

        Assert.Equal(new[] { "CPU: paused", "GPU0 Hot: paused" }, lines);
    }

    [Fact]
    public void Build_WhenNothingSelected_ReturnsTheNoSensorsLine()
    {
        var config = new AppConfig();
        SensorPreferences.Update(config, "cpu", p => p.ShowInOverlay = false);
        SensorPreferences.Update(config, "gpu0-hotspot", p => p.ShowInOverlay = false);

        var lines = OverlayLines.Build(config, new[] { Cpu, Hot }, Full);

        Assert.Equal(new[] { OverlayLines.NoSensorsText }, lines);
    }

    [Fact]
    public void Build_IgnoresNullReadings()
    {
        var lines = OverlayLines.Build(new AppConfig(), new SensorReading[] { null!, Cpu }, Full);

        Assert.Equal(new[] { "CPU: 59°C" }, lines);
    }
}
