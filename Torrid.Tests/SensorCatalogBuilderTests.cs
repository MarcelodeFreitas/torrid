using LibreHardwareMonitor.Hardware;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

namespace Torrid.Tests;

public class SensorCatalogBuilderTests
{
    private static SensorSnapshot T(string name, double? value, string? id) => new(name, SensorType.Temperature, value, id);

    private static HardwareSnapshot Hw(HardwareType type, string name, SensorSnapshot[] sensors, HardwareSnapshot[]? sub = null) =>
        new(type, name, sensors, sub ?? Array.Empty<HardwareSnapshot>(), "/" + name.Replace(' ', '-'));

    [Fact]
    public void Build_ListsTemperatureSensorsOnly()
    {
        var hardware = new[]
        {
            Hw(HardwareType.Cpu, "CPU", new[]
            {
                T("Core (Tctl/Tdie)", 59.0, "/amdcpu/0/temperature/2"),
                new SensorSnapshot("CPU Total", SensorType.Load, 10.0, "/amdcpu/0/load/0"),
                new SensorSnapshot("Fan", SensorType.Fan, 900, "/amdcpu/0/fan/0"),
            }),
        };

        var catalog = SensorCatalogBuilder.Build(hardware);

        var s = Assert.Single(catalog);
        Assert.Equal("hw:/amdcpu/0/temperature/2", s.Id);
        Assert.Equal("Core (Tctl/Tdie)", s.Name);
        Assert.Equal("CPU", s.HardwareName);
        Assert.Equal(SensorKind.CpuOther, s.Kind);
        Assert.Equal(59.0, s.ValueCelsius);
    }

    [Fact]
    public void Build_RecursesIntoSubHardware_AndNamesSuperIoAfterTheBoardAndChip()
    {
        var hardware = new[]
        {
            Hw(HardwareType.Motherboard, "ASUS ROG", Array.Empty<SensorSnapshot>(), new[]
            {
                Hw(HardwareType.SuperIO, "Nuvoton NCT6798D", new[] { T("VRM", 48.0, "/lpc/nct/0/temperature/4") }),
            }),
        };

        var s = Assert.Single(SensorCatalogBuilder.Build(hardware));

        Assert.Equal("hw:/lpc/nct/0/temperature/4", s.Id);
        Assert.Equal(SensorKind.Motherboard, s.Kind);
        Assert.Equal("ASUS ROG (Nuvoton NCT6798D)", s.HardwareName);
    }

    [Fact]
    public void Build_SuperIoWithoutNameFallsBackToTheBoard_AndNonSuperIoSubHardwareKeepsItsOwnName()
    {
        var hardware = new[]
        {
            Hw(HardwareType.Motherboard, "ASUS ROG", Array.Empty<SensorSnapshot>(), new[]
            {
                Hw(HardwareType.SuperIO, " ", new[] { T("VRM", 48.0, "/lpc/a") }),
                Hw(HardwareType.EmbeddedController, "EC", new[] { T("Chipset", 40.0, "/ec/b") }),
            }),
        };

        var catalog = SensorCatalogBuilder.Build(hardware);

        Assert.Equal("ASUS ROG", catalog.Single(c => c.Id == "hw:/lpc/a").HardwareName);
        Assert.Equal("EC", catalog.Single(c => c.Id == "hw:/ec/b").HardwareName);
        Assert.Equal(SensorKind.Motherboard, catalog.Single(c => c.Id == "hw:/ec/b").Kind);
    }

    [Fact]
    public void Build_TopLevelSuperIo_UsesItsOwnName()
    {
        var hardware = new[] { Hw(HardwareType.SuperIO, "Nuvoton NCT6798D", new[] { T("VRM", 48.0, "/lpc/a") }) };

        Assert.Equal("Nuvoton NCT6798D", SensorCatalogBuilder.Build(hardware).Single().HardwareName);
    }

    [Fact]
    public void Build_DeduplicatesById_AndSkipsSensorsWithoutIdentifiers()
    {
        var hardware = new[]
        {
            Hw(HardwareType.Storage, "Disk A", new[] { T("Temperature", 30.0, "/nvme/0/temperature/0"), T("NoId", 1.0, null), T("Blank", 1.0, "") }),
            Hw(HardwareType.Storage, "Disk A copy", new[] { T("Temperature", 99.0, "/nvme/0/temperature/0") }),
        };

        var s = Assert.Single(SensorCatalogBuilder.Build(hardware));

        Assert.Equal("Disk A", s.HardwareName);
        Assert.Equal(30.0, s.ValueCelsius);
    }

    [Fact]
    public void Build_OrdersByHardwareKindThenHardwareNameThenSensorName()
    {
        var hardware = new[]
        {
            Hw(HardwareType.Network, "Wifi", new[] { T("Z", 1, "/n/0") }),
            Hw(HardwareType.Storage, "Disk B", new[] { T("Temperature", 1, "/s/b") }),
            Hw(HardwareType.Memory, "DIMM", new[] { T("DIMM #1", 1, "/m/0") }),
            Hw(HardwareType.Motherboard, "Board", new[] { T("Chipset", 1, "/mb/0") }),
            Hw(HardwareType.GpuNvidia, "GPU", new[] { T("GPU Core", 1, "/g/0") }),
            Hw(HardwareType.Storage, "Disk A", new[] { T("Temperature", 1, "/s/a"), T("Composite", 1, "/s/a2") }),
            Hw(HardwareType.Cpu, "CPU", new[] { T("SoC", 1, "/c/1"), T("CCD1", 1, "/c/0") }),
        };

        var ids = SensorCatalogBuilder.Build(hardware).Select(c => c.Id).ToArray();

        Assert.Equal(new[]
        {
            "hw:/c/0", "hw:/c/1", "hw:/g/0", "hw:/mb/0", "hw:/m/0",
            "hw:/s/a2", "hw:/s/a", "hw:/s/b", "hw:/n/0",
        }, ids);
    }

    [Fact]
    public void Build_MapsKindsThroughKindFor()
    {
        var hardware = new[]
        {
            Hw(HardwareType.Cpu, "CPU", new[] { T("a", 1, "/1") }),
            Hw(HardwareType.GpuAmd, "GPU", new[] { T("a", 1, "/2") }),
            Hw(HardwareType.Memory, "RAM", new[] { T("a", 1, "/3") }),
            Hw(HardwareType.Storage, "SSD", new[] { T("a", 1, "/4") }),
            Hw(HardwareType.Cooler, "Pump", new[] { T("a", 1, "/5") }),
        };

        var kinds = SensorCatalogBuilder.Build(hardware).ToDictionary(c => c.Id, c => c.Kind);

        Assert.Equal(SensorKind.CpuOther, kinds["hw:/1"]);
        Assert.Equal(SensorKind.GpuOther, kinds["hw:/2"]);
        Assert.Equal(SensorKind.Memory, kinds["hw:/3"]);
        Assert.Equal(SensorKind.Storage, kinds["hw:/4"]);
        Assert.Equal(SensorKind.OtherTemp, kinds["hw:/5"]);
    }

    [Fact]
    public void Build_NullValueIsKept()
    {
        var s = Assert.Single(SensorCatalogBuilder.Build(new[] { Hw(HardwareType.Cpu, "CPU", new[] { T("a", null, "/1") }) }));

        Assert.Null(s.ValueCelsius);
    }

    [Fact]
    public void Build_EmptyHardware_ReturnsEmpty()
    {
        Assert.Empty(SensorCatalogBuilder.Build(Array.Empty<HardwareSnapshot>()));
    }
}
