namespace Torrid.Core.Sensors;

using LibreHardwareMonitor.Hardware;
using Torrid.Core.Models;

public static class SensorReadingBuilder
{
    public const string CpuTemperatureSensorName = "Core (Tctl/Tdie)";
    public const string GpuCoreTemperatureSensorName = "GPU Core";
    public const string GpuHotSpotTemperatureSensorName = "GPU Hot Spot";
    public const string GpuVramTemperatureSensorName = "GPU Memory Junction";

    private static readonly HardwareType[] GpuHardwareTypes =
    {
        HardwareType.GpuNvidia, HardwareType.GpuAmd, HardwareType.GpuIntel,
    };

    public static IReadOnlyList<SensorReading> Build(IReadOnlyList<HardwareSnapshot> hardware)
    {
        var readings = new List<SensorReading>();

        var cpu = hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        if (cpu is not null)
        {
            readings.Add(MakeReading("cpu", "CPU", SensorKind.CpuPackage, null, cpu, CpuTemperatureSensorName));
        }

        var gpuIndex = 0;
        foreach (var gpu in hardware.Where(h => GpuHardwareTypes.Contains(h.HardwareType)))
        {
            readings.Add(MakeReading($"gpu{gpuIndex}-core", $"GPU{gpuIndex} Core", SensorKind.GpuCore, gpuIndex, gpu, GpuCoreTemperatureSensorName));
            readings.Add(MakeReading($"gpu{gpuIndex}-hotspot", $"GPU{gpuIndex} Hot", SensorKind.GpuHotSpot, gpuIndex, gpu, GpuHotSpotTemperatureSensorName));
            readings.Add(MakeReading($"gpu{gpuIndex}-vram", $"GPU{gpuIndex} VRAM", SensorKind.GpuVram, gpuIndex, gpu, GpuVramTemperatureSensorName));
            gpuIndex++;
        }

        return readings;
    }

    private static SensorReading MakeReading(string id, string label, SensorKind kind, int? gpuIndex, HardwareSnapshot hardware, string sensorName) =>
        new(id, label, kind, gpuIndex,
            hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name == sensorName)?.Value,
            hardware.Name,
            sensorName);

    public static IReadOnlyList<SensorReading> ToUnavailable(IReadOnlyList<SensorReading> readings) =>
        readings.Select(r => r with { ValueCelsius = null }).ToList();
}
