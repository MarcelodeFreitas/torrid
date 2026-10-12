using LibreHardwareMonitor.Hardware;
using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

namespace Torrid.Tests;

public class SensorReadingBuilderTests
{
    private static SensorSnapshot Temp(string name, double? value) => new(name, SensorType.Temperature, value);

    [Fact]
    public void Build_WithCpuAndOneGpu_ProducesFourReadings()
    {
        var hardware = new List<HardwareSnapshot>
        {
            new(HardwareType.Cpu, "AMD Ryzen 9 5950X",
                new[] { Temp("Core (Tctl/Tdie)", 59.0), Temp("SoC", 38.0) },
                Array.Empty<HardwareSnapshot>()),
            new(HardwareType.GpuNvidia, "NVIDIA GeForce RTX 3090",
                new[] { Temp("GPU Core", 41.0), Temp("GPU Hot Spot", 51.0), Temp("GPU Memory Junction", 50.0) },
                Array.Empty<HardwareSnapshot>()),
        };

        var readings = SensorReadingBuilder.Build(hardware);

        Assert.Equal(4, readings.Count);
        var cpu = Assert.Single(readings, r => r.Kind == SensorKind.CpuPackage);
        Assert.Equal("cpu", cpu.Id);
        Assert.Null(cpu.GpuIndex);
        Assert.Equal(59.0, cpu.ValueCelsius);

        var gpuCore = Assert.Single(readings, r => r.Kind == SensorKind.GpuCore);
        Assert.Equal("gpu0-core", gpuCore.Id);
        Assert.Equal(0, gpuCore.GpuIndex);
        Assert.Equal(41.0, gpuCore.ValueCelsius);
    }

    [Fact]
    public void Build_WithTwoGpus_LabelsThemByIndex()
    {
        HardwareSnapshot Gpu(string name) => new(HardwareType.GpuNvidia, name,
            new[] { Temp("GPU Core", 40.0), Temp("GPU Hot Spot", 50.0), Temp("GPU Memory Junction", 50.0) },
            Array.Empty<HardwareSnapshot>());

        var hardware = new List<HardwareSnapshot> { Gpu("RTX 3090"), Gpu("RTX 3090") };

        var readings = SensorReadingBuilder.Build(hardware);

        Assert.Contains(readings, r => r.Id == "gpu0-core" && r.GpuIndex == 0);
        Assert.Contains(readings, r => r.Id == "gpu1-core" && r.GpuIndex == 1);
    }

    [Fact]
    public void Build_WithNoGpu_ProducesCpuOnlyWithoutThrowing()
    {
        var hardware = new List<HardwareSnapshot>
        {
            new(HardwareType.Cpu, "AMD Ryzen 9 5950X",
                new[] { Temp("Core (Tctl/Tdie)", 59.0) }, Array.Empty<HardwareSnapshot>()),
        };

        var readings = SensorReadingBuilder.Build(hardware);

        Assert.Single(readings);
        Assert.Equal(SensorKind.CpuPackage, readings[0].Kind);
    }

    [Fact]
    public void Build_WhenTemperatureSensorMissing_ProducesNullValueNotThrow()
    {
        var hardware = new List<HardwareSnapshot>
        {
            new(HardwareType.Cpu, "AMD Ryzen 9 5950X", Array.Empty<SensorSnapshot>(), Array.Empty<HardwareSnapshot>()),
        };

        var readings = SensorReadingBuilder.Build(hardware);

        Assert.Null(readings[0].ValueCelsius);
    }

    [Fact]
    public void ToUnavailable_GivenReadingsWithValues_ReturnsSameIdentityWithNullValues()
    {
        var readings = new List<SensorReading>
        {
            new("cpu", "CPU", SensorKind.CpuPackage, null, 59.0),
            new("gpu0-core", "GPU0 Core", SensorKind.GpuCore, 0, 41.0),
        };

        var result = SensorReadingBuilder.ToUnavailable(readings);

        Assert.Equal(2, result.Count);
        Assert.Equal("cpu", result[0].Id);
        Assert.Equal("CPU", result[0].Label);
        Assert.Equal(SensorKind.CpuPackage, result[0].Kind);
        Assert.Null(result[0].GpuIndex);
        Assert.Null(result[0].ValueCelsius);

        Assert.Equal("gpu0-core", result[1].Id);
        Assert.Equal("GPU0 Core", result[1].Label);
        Assert.Equal(SensorKind.GpuCore, result[1].Kind);
        Assert.Equal(0, result[1].GpuIndex);
        Assert.Null(result[1].ValueCelsius);
    }

    [Fact]
    public void ToUnavailable_GivenEmptyList_ReturnsEmptyList()
    {
        var result = SensorReadingBuilder.ToUnavailable(Array.Empty<SensorReading>());

        Assert.Empty(result);
    }

    [Fact]
    public void Build_PopulatesHardwareAndSourceSensorNames()
    {
        var hardware = new List<HardwareSnapshot>
        {
            new(HardwareType.Cpu, "AMD Ryzen 9 5950X",
                new[] { Temp("Core (Tctl/Tdie)", 59.0) }, Array.Empty<HardwareSnapshot>()),
            new(HardwareType.GpuNvidia, "NVIDIA GeForce RTX 3090",
                new[] { Temp("GPU Core", 41.0), Temp("GPU Hot Spot", 51.0), Temp("GPU Memory Junction", 50.0) },
                Array.Empty<HardwareSnapshot>()),
        };

        var readings = SensorReadingBuilder.Build(hardware);

        var cpu = Assert.Single(readings, r => r.Id == "cpu");
        Assert.Equal("AMD Ryzen 9 5950X", cpu.HardwareName);
        Assert.Equal("Core (Tctl/Tdie)", cpu.SourceSensorName);
        var hot = Assert.Single(readings, r => r.Id == "gpu0-hotspot");
        Assert.Equal("NVIDIA GeForce RTX 3090", hot.HardwareName);
        Assert.Equal("GPU Hot Spot", hot.SourceSensorName);
        var vram = Assert.Single(readings, r => r.Id == "gpu0-vram");
        Assert.Equal("GPU Memory Junction", vram.SourceSensorName);
    }

    [Fact]
    public void WithCpuPlaceholder_InsertsUnavailableCpuFirstWhenMissing()
    {
        var gpu = new SensorReading("gpu0-hotspot", "GPU0 Hot", SensorKind.GpuHotSpot, 0, 51.0);

        var readings = SensorReadingBuilder.WithCpuPlaceholder(new[] { gpu });

        Assert.Equal(2, readings.Count);
        Assert.Equal("cpu", readings[0].Id);
        Assert.Null(readings[0].ValueCelsius);
        Assert.Equal("gpu0-hotspot", readings[1].Id);
    }

    [Fact]
    public void WithCpuPlaceholder_LeavesAnExistingCpuReadingAlone()
    {
        var cpu = new SensorReading("cpu", "CPU", SensorKind.CpuPackage, null, 60.0);

        var readings = SensorReadingBuilder.WithCpuPlaceholder(new[] { cpu });

        Assert.Single(readings);
        Assert.Equal(60.0, readings[0].ValueCelsius);
    }

    // ---- Intel / other CPU fallback --------------------------------------------------------

    private static HardwareSnapshot Cpu(params SensorSnapshot[] sensors) =>
        new(HardwareType.Cpu, "CPU", sensors, Array.Empty<HardwareSnapshot>());

    [Fact]
    public void Build_IntelCpuPackage_ProducesTheCpuReadingFromThatSensor()
    {
        var readings = SensorReadingBuilder.Build(new[]
        {
            Cpu(Temp("CPU Core #1", 50.0), Temp("CPU Package", 63.0)),
        });

        var cpu = Assert.Single(readings, r => r.Id == "cpu");
        Assert.Equal(63.0, cpu.ValueCelsius);
        Assert.Equal("CPU Package", cpu.SourceSensorName);
        Assert.Equal(SensorKind.CpuPackage, cpu.Kind);
    }

    [Fact]
    public void Build_CpuSensorPriority_AmdTctlTdieBeatsPackageBeatsCoreMax()
    {
        var all = new[]
        {
            Temp("Core Average", 1.0), Temp("Package", 2.0), Temp("Core Max", 3.0),
            Temp("CPU Package", 4.0), Temp("Core (Tctl/Tdie)", 5.0),
        };

        Assert.Equal("Core (Tctl/Tdie)", SensorReadingBuilder.Build(new[] { Cpu(all) })[0].SourceSensorName);
        Assert.Equal("CPU Package", SensorReadingBuilder.Build(new[] { Cpu(all[..4]) })[0].SourceSensorName);
        Assert.Equal("Core Max", SensorReadingBuilder.Build(new[] { Cpu(all[..3]) })[0].SourceSensorName);
        Assert.Equal("Package", SensorReadingBuilder.Build(new[] { Cpu(all[..2]) })[0].SourceSensorName);
        var last = SensorReadingBuilder.Build(new[] { Cpu(all[..1]) })[0];
        Assert.Equal("Core Average", last.SourceSensorName);
        Assert.Equal(1.0, last.ValueCelsius);
    }

    [Fact]
    public void Build_AmdAndIntelNamesBothPresent_AmdWins()
    {
        var cpu = SensorReadingBuilder.Build(new[]
        {
            Cpu(Temp("CPU Package", 70.0), Temp("Core (Tctl/Tdie)", 59.0)),
        })[0];

        Assert.Equal(59.0, cpu.ValueCelsius);
        Assert.Equal("Core (Tctl/Tdie)", cpu.SourceSensorName);
    }

    [Fact]
    public void Build_CpuWithNoKnownTemperatureName_StillYieldsCpuReadingWithNull()
    {
        var readings = SensorReadingBuilder.Build(new[] { Cpu(Temp("SoC", 40.0), new SensorSnapshot("CPU Package", SensorType.Load, 5.0)) });

        var cpu = Assert.Single(readings);
        Assert.Equal("cpu", cpu.Id);
        Assert.Null(cpu.ValueCelsius);
    }

    // ---- Added (generic) sensors -----------------------------------------------------------

    private static IReadOnlyList<HardwareSnapshot> BoardTree() => new List<HardwareSnapshot>
    {
        new(HardwareType.Motherboard, "ASUS ROG", Array.Empty<SensorSnapshot>(),
            new[]
            {
                new HardwareSnapshot(HardwareType.SuperIO, "Nuvoton NCT6798D",
                    new[]
                    {
                        new SensorSnapshot("VRM", SensorType.Temperature, 48.0, "/lpc/nct6798d/0/temperature/4"),
                        new SensorSnapshot("System", SensorType.Temperature, 36.5, "/lpc/nct6798d/0/temperature/2"),
                        new SensorSnapshot("Fan #1", SensorType.Fan, 900, "/lpc/nct6798d/0/fan/0"),
                    },
                    Array.Empty<HardwareSnapshot>(), "/lpc/nct6798d/0"),
            }, "/motherboard"),
    };

    [Fact]
    public void Build_AddedIds_AreFoundInSubHardwareAndGetIdLabelKindAndValue()
    {
        var readings = SensorReadingBuilder.Build(BoardTree(),
            new[] { "hw:/lpc/nct6798d/0/temperature/4", "hw:/lpc/nct6798d/0/temperature/2" });

        Assert.Equal(2, readings.Count);
        var vrm = Assert.Single(readings, r => r.Id == "hw:/lpc/nct6798d/0/temperature/4");
        Assert.Equal("VRM", vrm.Label);
        Assert.Equal(SensorKind.Motherboard, vrm.Kind);
        Assert.Null(vrm.GpuIndex);
        Assert.Equal(48.0, vrm.ValueCelsius);
        Assert.Equal("Nuvoton NCT6798D", vrm.HardwareName);
        Assert.Equal("VRM", vrm.SourceSensorName);
        var sys = Assert.Single(readings, r => r.Id == "hw:/lpc/nct6798d/0/temperature/2");
        Assert.Equal(36.5, sys.ValueCelsius);
    }

    [Fact]
    public void Build_AddedInfo_OverridesLabelAndKind()
    {
        var info = new AddedSensor { Name = "My VRM", HardwareName = "Board", Kind = SensorKind.OtherTemp };

        var readings = SensorReadingBuilder.Build(BoardTree(), new[] { "hw:/lpc/nct6798d/0/temperature/4" }, _ => info);

        var vrm = Assert.Single(readings);
        Assert.Equal("My VRM", vrm.Label);
        Assert.Equal(SensorKind.OtherTemp, vrm.Kind);
        Assert.Equal(48.0, vrm.ValueCelsius);
    }

    [Fact]
    public void Build_AddedIdNotFound_YieldsNullValueReadingUsingInfo()
    {
        var info = new AddedSensor { Name = "Gone", HardwareName = "Old board", Kind = SensorKind.Storage };

        var readings = SensorReadingBuilder.Build(BoardTree(), new[] { "hw:/nvme/9/temperature/0" }, _ => info);

        var gone = Assert.Single(readings);
        Assert.Equal("hw:/nvme/9/temperature/0", gone.Id);
        Assert.Equal("Gone", gone.Label);
        Assert.Equal(SensorKind.Storage, gone.Kind);
        Assert.Null(gone.ValueCelsius);
        Assert.Equal("Old board", gone.HardwareName);
        Assert.Null(gone.GpuIndex);
    }

    [Fact]
    public void Build_AddedIdNotFoundAndNoInfo_IsSkipped()
    {
        var readings = SensorReadingBuilder.Build(BoardTree(), new[] { "hw:/nvme/9/temperature/0" });

        Assert.Empty(readings);
    }

    [Fact]
    public void Build_AddedIdMatchingANonTemperatureSensor_YieldsNull()
    {
        var readings = SensorReadingBuilder.Build(BoardTree(), new[] { "hw:/lpc/nct6798d/0/fan/0" });

        var fan = Assert.Single(readings);
        Assert.Null(fan.ValueCelsius);
    }

    [Fact]
    public void Build_WithoutAddedIds_BuiltInOutputIsUnchanged()
    {
        var hardware = new List<HardwareSnapshot>(BoardTree())
        {
            Cpu(Temp("Core (Tctl/Tdie)", 59.0)),
        };

        var plain = SensorReadingBuilder.Build(hardware);
        var empty = SensorReadingBuilder.Build(hardware, Array.Empty<string>(), _ => null);

        Assert.Equal(plain, empty);
        Assert.Equal(new[] { "cpu" }, plain.Select(r => r.Id));
    }

    [Fact]
    public void Build_BuiltInsAndAddedTogether_AddedFollowTheBuiltIns()
    {
        var hardware = new List<HardwareSnapshot>(BoardTree()) { Cpu(Temp("Core (Tctl/Tdie)", 59.0)) };

        var readings = SensorReadingBuilder.Build(hardware, new[] { "hw:/lpc/nct6798d/0/temperature/4" });

        Assert.Equal(new[] { "cpu", "hw:/lpc/nct6798d/0/temperature/4" }, readings.Select(r => r.Id));
    }

    [Fact]
    public void Build_DuplicateIdentifiers_FirstMatchWins()
    {
        var hardware = new List<HardwareSnapshot>
        {
            new(HardwareType.Storage, "Disk A", new[] { new SensorSnapshot("Temperature", SensorType.Temperature, 30.0, "/dup/0") }, Array.Empty<HardwareSnapshot>()),
            new(HardwareType.Storage, "Disk B", new[] { new SensorSnapshot("Temperature", SensorType.Temperature, 99.0, "/dup/0") }, Array.Empty<HardwareSnapshot>()),
        };

        var reading = Assert.Single(SensorReadingBuilder.Build(hardware, new[] { "hw:/dup/0" }));

        Assert.Equal(30.0, reading.ValueCelsius);
        Assert.Equal("Disk A", reading.HardwareName);
    }

    [Theory]
    [InlineData(HardwareType.Cpu, SensorKind.CpuOther)]
    [InlineData(HardwareType.GpuNvidia, SensorKind.GpuOther)]
    [InlineData(HardwareType.GpuAmd, SensorKind.GpuOther)]
    [InlineData(HardwareType.GpuIntel, SensorKind.GpuOther)]
    [InlineData(HardwareType.Motherboard, SensorKind.Motherboard)]
    [InlineData(HardwareType.SuperIO, SensorKind.Motherboard)]
    [InlineData(HardwareType.Storage, SensorKind.Storage)]
    [InlineData(HardwareType.Memory, SensorKind.Memory)]
    [InlineData(HardwareType.Network, SensorKind.OtherTemp)]
    [InlineData(HardwareType.Cooler, SensorKind.OtherTemp)]
    public void KindFor_MapsHardwareTypesToKinds(HardwareType type, SensorKind expected)
    {
        Assert.Equal(expected, SensorReadingBuilder.KindFor(type));
    }
}
