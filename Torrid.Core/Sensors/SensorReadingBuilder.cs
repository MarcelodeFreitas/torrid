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
            readings.Add(new SensorReading(
                "cpu", "CPU", SensorKind.CpuPackage, null,
                FindTemperature(cpu, CpuTemperatureSensorName)));
        }

        var gpuIndex = 0;
        foreach (var gpu in hardware.Where(h => GpuHardwareTypes.Contains(h.HardwareType)))
        {
            readings.Add(new SensorReading(
                $"gpu{gpuIndex}-core", $"GPU{gpuIndex} Core", SensorKind.GpuCore, gpuIndex,
                FindTemperature(gpu, GpuCoreTemperatureSensorName)));
            readings.Add(new SensorReading(
                $"gpu{gpuIndex}-hotspot", $"GPU{gpuIndex} Hot", SensorKind.GpuHotSpot, gpuIndex,
                FindTemperature(gpu, GpuHotSpotTemperatureSensorName)));
            readings.Add(new SensorReading(
                $"gpu{gpuIndex}-vram", $"GPU{gpuIndex} VRAM", SensorKind.GpuVram, gpuIndex,
                FindTemperature(gpu, GpuVramTemperatureSensorName)));
            gpuIndex++;
        }

        return readings;
    }

    private static double? FindTemperature(HardwareSnapshot hardware, string sensorName) =>
        hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name == sensorName)?.Value;
}
