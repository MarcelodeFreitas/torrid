using Torrid.App.Tray;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

namespace Torrid.Tests;

public class TooltipTextBuilderTests
{
    [Fact]
    public void Build_WithNoReadings_ReturnsEmptyStringWithoutThrowing()
    {
        var text = TooltipTextBuilder.Build(Array.Empty<SensorReading>());

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void Build_WithReadings_JoinsLabelAndValuePerLine()
    {
        var readings = new List<SensorReading>
        {
            new("cpu", "CPU", SensorKind.CpuPackage, null, 59.0),
            new("gpu0-core", "GPU0 Core", SensorKind.GpuCore, 0, null),
        };

        var text = TooltipTextBuilder.Build(readings);

        Assert.Contains("CPU: 59°C", text);
        Assert.Contains("GPU0 Core: --", text);
    }

    [Fact]
    public void Build_WhilePaused_SaysPausedAndNamesTheProduct()
    {
        var readings = new List<SensorReading>
        {
            new("cpu", "My CPU", SensorKind.CpuPackage, null, null),
            new("gpu0-hotspot", "GPU0 Hot", SensorKind.GpuHotSpot, 0, 71.0),
        };
        var paused = new HardwareStatus(HardwareAccessLevel.Paused, new AntiCheatMatch("Riot Vanguard", "vgc"));

        var text = TooltipTextBuilder.Build(readings, paused);

        Assert.Equal(
            "My CPU: paused (Riot Vanguard is running)" + Environment.NewLine + "GPU0 Hot: paused (Riot Vanguard is running)",
            text);
    }

    [Fact]
    public void Build_WhilePausedByACustomProcess_NamesTheProcess()
    {
        var readings = new[] { new SensorReading("cpu", "CPU", SensorKind.CpuPackage, null, null) };
        var paused = new HardwareStatus(HardwareAccessLevel.Paused, new AntiCheatMatch("Custom", "MyGame"));

        Assert.Equal("CPU: paused (MyGame is running)", TooltipTextBuilder.Build(readings, paused));
    }

    [Theory]
    [InlineData(HardwareAccessLevel.Full)]
    [InlineData(HardwareAccessLevel.GpuOnly)]
    public void Build_WhenNotPaused_ShowsValues(HardwareAccessLevel level)
    {
        var readings = new[] { new SensorReading("gpu0-core", "GPU0 Core", SensorKind.GpuCore, 0, 59.4) };
        var status = new HardwareStatus(level, new AntiCheatMatch("Riot Vanguard", "vgc"));

        Assert.Equal("GPU0 Core: 59°C", TooltipTextBuilder.Build(readings, status));
    }

    [Fact]
    public void Build_WhilePaused_StillTruncatesTo127Chars()
    {
        var readings = Enumerable.Range(0, 10)
            .Select(i => new SensorReading($"s{i}", $"Sensor Number {i}", SensorKind.CpuPackage, null, null))
            .ToList();
        var paused = new HardwareStatus(HardwareAccessLevel.Paused, new AntiCheatMatch("Easy Anti-Cheat", "EasyAntiCheat"));

        Assert.True(TooltipTextBuilder.Build(readings, paused).Length <= 127);
    }

    [Fact]
    public void Build_WhenTextWouldExceedNotifyIconLimit_TruncatesTo127Chars()
    {
        var readings = Enumerable.Range(0, 20)
            .Select(i => new SensorReading($"s{i}", $"Sensor Number {i}", SensorKind.CpuPackage, null, 50.0))
            .ToList();

        var text = TooltipTextBuilder.Build(readings);

        Assert.True(text.Length <= 127);
    }
}
