using Torrid.App.Tray;
using Torrid.Core.Models;

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
    public void Build_WhenTextWouldExceedNotifyIconLimit_TruncatesTo127Chars()
    {
        var readings = Enumerable.Range(0, 20)
            .Select(i => new SensorReading($"s{i}", $"Sensor Number {i}", SensorKind.CpuPackage, null, 50.0))
            .ToList();

        var text = TooltipTextBuilder.Build(readings);

        Assert.True(text.Length <= 127);
    }
}
