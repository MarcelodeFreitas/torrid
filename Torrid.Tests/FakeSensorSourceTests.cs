using Torrid.App.Sensors;
using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

namespace Torrid.Tests;

public class FakeSensorSourceTests
{
    [Fact]
    public async Task ScanAsync_ReturnsTheDeterministicDemoCatalog()
    {
        var source = new FakeSensorSource();

        var result = await source.ScanAsync();

        Assert.Null(result.UnavailableReason);
        Assert.Equal(
            new[]
            {
                ("CCD1 (Tdie)", "AMD Ryzen 9 5950X", SensorKind.CpuOther),
                ("CCD2 (Tdie)", "AMD Ryzen 9 5950X", SensorKind.CpuOther),
                ("SoC", "AMD Ryzen 9 5950X", SensorKind.CpuOther),
                ("Chipset", "ASUS ROG CROSSHAIR VIII", SensorKind.Motherboard),
                ("System", "ASUS ROG CROSSHAIR VIII", SensorKind.Motherboard),
                ("VRM", "ASUS ROG CROSSHAIR VIII", SensorKind.Motherboard),
                ("Composite", "Samsung SSD 980 PRO 1TB", SensorKind.Storage),
            },
            result.Sensors.Select(s => (s.Name, s.HardwareName, s.Kind)));
        Assert.All(result.Sensors, s => Assert.True(SensorIdentity.IsGenericId(s.Id)));
        Assert.All(result.Sensors, s => Assert.NotNull(s.ValueCelsius));
        Assert.Equal(result.Sensors.Count, result.Sensors.Select(s => s.Id).Distinct().Count());

        var again = await source.ScanAsync();
        Assert.Equal(result.Sensors, again.Sensors);
    }

    [Fact]
    public async Task ScanAsync_WhilePaused_ReturnsTheReasonAndNoSensors()
    {
        var source = new FakeSensorSource
        {
            Status = new HardwareStatus(HardwareAccessLevel.Paused, new AntiCheatMatch("Riot Vanguard", "vgc")),
        };

        var result = await source.ScanAsync();

        Assert.Empty(result.Sensors);
        Assert.Equal("Monitoring is paused while Riot Vanguard is running.", result.UnavailableReason);
    }

    [Fact]
    public async Task AddedIdsFromTheCatalog_ProduceReadingsWithTheStoredNameAndKind()
    {
        var catalog = (await new FakeSensorSource().ScanAsync()).Sensors;
        var vrm = catalog.Single(s => s.Name == "VRM");
        var ssd = catalog.Single(s => s.Name == "Composite");
        var added = new Dictionary<string, AddedSensor>
        {
            [vrm.Id] = new() { Name = "My VRM", HardwareName = vrm.HardwareName, Kind = vrm.Kind },
            [ssd.Id] = new() { Name = ssd.Name, HardwareName = ssd.HardwareName, Kind = ssd.Kind },
        };

        var source = new FakeSensorSource(() => added);

        var builtIns = new[] { "cpu", "gpu0-core", "gpu0-hotspot", "gpu0-vram", "gpu1-core", "gpu1-hotspot", "gpu1-vram" };
        Assert.Equal(builtIns.Concat(new[] { vrm.Id, ssd.Id }), source.CurrentReadings.Select(r => r.Id));
        var vrmReading = source.CurrentReadings.Single(r => r.Id == vrm.Id);
        Assert.Equal("My VRM", vrmReading.Label);
        Assert.Equal(SensorKind.Motherboard, vrmReading.Kind);
        Assert.NotNull(vrmReading.ValueCelsius);
        Assert.NotNull(source.CurrentReadings.Single(r => r.Id == ssd.Id).ValueCelsius);
    }

    [Fact]
    public void AddedIdNotInTheCatalog_YieldsANullReading()
    {
        var added = new Dictionary<string, AddedSensor>
        {
            ["hw:/nowhere/0/temperature/0"] = new() { Name = "Gone", Kind = SensorKind.Memory },
        };

        var source = new FakeSensorSource(() => added);

        var gone = source.CurrentReadings.Single(r => r.Id == "hw:/nowhere/0/temperature/0");
        Assert.Equal("Gone", gone.Label);
        Assert.Null(gone.ValueCelsius);
    }

    [Fact]
    public void WithoutAddedSensors_ReadingsAreTheBuiltInSevenAsBefore()
    {
        var source = new FakeSensorSource();

        Assert.Equal(7, source.CurrentReadings.Count);
        Assert.DoesNotContain(source.CurrentReadings, r => SensorIdentity.IsGenericId(r.Id));
    }

    [Fact]
    public async Task Paused_AddedReadingsAreNull()
    {
        var vrm = (await new FakeSensorSource().ScanAsync()).Sensors.Single(s => s.Name == "VRM");
        var added = new Dictionary<string, AddedSensor> { [vrm.Id] = new() { Name = "VRM", Kind = SensorKind.Motherboard } };

        var source = new FakeSensorSource(() => added)
        {
            Status = new HardwareStatus(HardwareAccessLevel.Paused, new AntiCheatMatch("Riot Vanguard", "vgc")),
        };

        Assert.Contains(source.CurrentReadings, r => r.Id == vrm.Id);
        Assert.All(source.CurrentReadings, r => Assert.Null(r.ValueCelsius));
    }
}
