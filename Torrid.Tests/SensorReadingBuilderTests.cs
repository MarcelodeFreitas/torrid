using LibreHardwareMonitor.Hardware;
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
}
