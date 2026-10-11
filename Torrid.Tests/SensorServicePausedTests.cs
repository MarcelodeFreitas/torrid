using Torrid.Core.Models;
using Torrid.Core.Sensors;

namespace Torrid.Tests;

public class SensorServicePausedTests
{
    private sealed class FakeLister : IProcessLister
    {
        public IReadOnlyCollection<string> GetRunningProcessNames() => new[] { "explorer", "vgc" };
    }

    [Fact]
    public void PausedAtLaunch_ExposesNullValuedCpuReadingWithoutOpeningHardware()
    {
        using var service = new SensorService(60_000, processLister: new FakeLister());

        Assert.Equal(HardwareAccessLevel.Paused, service.Status.Level);
        Assert.Equal("Riot Vanguard", service.Status.AntiCheat!.Product);
        var cpu = Assert.Single(service.CurrentReadings);
        Assert.Equal("cpu", cpu.Id);
        Assert.Null(cpu.ValueCelsius);
    }

    [Fact]
    public void ToUnavailableThenCpuPlaceholder_OfEmpty_YieldsSingleNullCpu()
    {
        var readings = SensorReadingBuilder.WithCpuPlaceholder(SensorReadingBuilder.ToUnavailable(Array.Empty<SensorReading>()));

        var cpu = Assert.Single(readings);
        Assert.Equal("cpu", cpu.Id);
        Assert.Null(cpu.ValueCelsius);
    }

    [Fact]
    public void ToUnavailable_OfPopulatedList_KeepsIdsAndNullsValues()
    {
        var input = new[]
        {
            new SensorReading("cpu", "CPU", SensorKind.CpuPackage, null, 60.0),
            new SensorReading("gpu0-core", "GPU0 Core", SensorKind.GpuCore, 0, 50.0),
        };

        var readings = SensorReadingBuilder.WithCpuPlaceholder(SensorReadingBuilder.ToUnavailable(input));

        Assert.Equal(new[] { "cpu", "gpu0-core" }, readings.Select(r => r.Id));
        Assert.All(readings, r => Assert.Null(r.ValueCelsius));
    }
}
