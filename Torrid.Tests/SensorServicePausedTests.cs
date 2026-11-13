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
        using var service = new SensorService(60_000, behavior: () => AntiCheatBehavior.Pause, processLister: new FakeLister());

        Assert.Equal(HardwareAccessLevel.Paused, service.Status.Level);
        Assert.Equal("Riot Vanguard", service.Status.AntiCheat!.Product);
        var cpu = Assert.Single(service.CurrentReadings);
        Assert.Equal("cpu", cpu.Id);
        Assert.Null(cpu.ValueCelsius);
    }

    private sealed class ThrowingLister : IProcessLister
    {
        public IReadOnlyCollection<string> GetRunningProcessNames() => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void WhenProcessListingThrows_FailsClosedWithUnavailableCpuAndNeverOpensHardware()
    {
        using var service = new SensorService(60_000, behavior: () => AntiCheatBehavior.Continue, processLister: new ThrowingLister());

        var cpu = Assert.Single(service.CurrentReadings);
        Assert.Equal("cpu", cpu.Id);
        Assert.Null(cpu.ValueCelsius);
    }

    [Fact]
    public async Task ScanAsync_WhilePaused_ReturnsTheReasonAndNoSensorsWithoutOpeningHardware()
    {
        using var service = new SensorService(60_000, behavior: () => AntiCheatBehavior.Pause, processLister: new FakeLister());

        var result = await service.ScanAsync();

        Assert.Empty(result.Sensors);
        Assert.Equal("Monitoring is paused while Riot Vanguard is running.", result.UnavailableReason);
    }

    [Fact]
    public async Task ScanAsync_WhenProcessListingThrows_ReportsTheFailureWithoutOpeningHardware()
    {
        using var service = new SensorService(60_000, behavior: () => AntiCheatBehavior.Continue, processLister: new ThrowingLister());

        var result = await service.ScanAsync();

        Assert.Empty(result.Sensors);
        Assert.Equal("Couldn't scan hardware: boom", result.UnavailableReason);
    }

    [Fact]
    public void PausedAtLaunch_WithAddedSensors_ExposesNullReadingsForThemAfterTheCpu()
    {
        var added = new Dictionary<string, Torrid.Core.Config.AddedSensor>
        {
            ["hw:/lpc/nct6798d/0/temperature/4"] = new() { Name = "VRM", HardwareName = "Board", Kind = SensorKind.Motherboard },
            ["not-a-valid-id"] = new() { Name = "Garbage", Kind = SensorKind.Motherboard },
        };

        using var service = new SensorService(60_000, behavior: () => AntiCheatBehavior.Pause, processLister: new FakeLister(), addedSensors: () => added);

        Assert.Equal(new[] { "cpu", "hw:/lpc/nct6798d/0/temperature/4" }, service.CurrentReadings.Select(r => r.Id));
        var vrm = service.CurrentReadings[1];
        Assert.Equal("VRM", vrm.Label);
        Assert.Equal(SensorKind.Motherboard, vrm.Kind);
        Assert.Null(vrm.ValueCelsius);
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
