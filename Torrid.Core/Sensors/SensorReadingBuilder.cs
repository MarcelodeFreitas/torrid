namespace Torrid.Core.Sensors;

using LibreHardwareMonitor.Hardware;
using Torrid.Core.Config;
using Torrid.Core.Models;

public static class SensorReadingBuilder
{
    public const string CpuTemperatureSensorName = "Core (Tctl/Tdie)";
    public const string GpuCoreTemperatureSensorName = "GPU Core";
    public const string GpuHotSpotTemperatureSensorName = "GPU Hot Spot";
    public const string GpuVramTemperatureSensorName = "GPU Memory Junction";

    /// <summary>
    /// Temperature sensors that stand for "the CPU temperature", in priority order: AMD's Tctl/Tdie first,
    /// then the names Intel (and other) CPUs expose.
    /// </summary>
    public static readonly IReadOnlyList<string> CpuTemperatureSensorNames = new[]
    {
        CpuTemperatureSensorName, "CPU Package", "Core Max", "Package", "Core Average",
    };

    private static readonly HardwareType[] GpuHardwareTypes =
    {
        HardwareType.GpuNvidia, HardwareType.GpuAmd, HardwareType.GpuIntel,
    };

    public static IReadOnlyList<SensorReading> Build(
        IReadOnlyList<HardwareSnapshot> hardware,
        IReadOnlyCollection<string>? addedSensorIds = null,
        Func<string, AddedSensor?>? addedInfo = null)
    {
        var readings = new List<SensorReading>();

        var cpu = hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        if (cpu is not null)
        {
            readings.Add(MakeCpuReading(cpu));
        }

        var gpuIndex = 0;
        foreach (var gpu in hardware.Where(h => GpuHardwareTypes.Contains(h.HardwareType)))
        {
            readings.Add(MakeReading($"gpu{gpuIndex}-core", $"GPU{gpuIndex} Core", SensorKind.GpuCore, gpuIndex, gpu, GpuCoreTemperatureSensorName));
            readings.Add(MakeReading($"gpu{gpuIndex}-hotspot", $"GPU{gpuIndex} Hot", SensorKind.GpuHotSpot, gpuIndex, gpu, GpuHotSpotTemperatureSensorName));
            readings.Add(MakeReading($"gpu{gpuIndex}-vram", $"GPU{gpuIndex} VRAM", SensorKind.GpuVram, gpuIndex, gpu, GpuVramTemperatureSensorName));
            gpuIndex++;
        }

        if (addedSensorIds is not null)
        {
            foreach (var id in addedSensorIds)
            {
                var info = addedInfo?.Invoke(id);
                var found = Find(hardware, id);
                if (found is null && info is null) continue;

                var sensor = found?.Sensor;
                var owner = found?.Hardware;
                readings.Add(new SensorReading(
                    id,
                    info?.Name ?? sensor!.Name,
                    info?.Kind ?? KindFor(owner!.HardwareType),
                    null,
                    sensor is { SensorType: SensorType.Temperature } ? sensor.Value : null,
                    info?.HardwareName ?? owner?.Name,
                    sensor?.Name));
            }
        }

        return readings;
    }

    /// <summary>The generic sensor kind for a hardware type (used when no stored <see cref="AddedSensor"/> says otherwise).</summary>
    public static SensorKind KindFor(HardwareType hardwareType) => hardwareType switch
    {
        HardwareType.Cpu => SensorKind.CpuOther,
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => SensorKind.GpuOther,
        HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController => SensorKind.Motherboard,
        HardwareType.Storage => SensorKind.Storage,
        HardwareType.Memory => SensorKind.Memory,
        _ => SensorKind.OtherTemp,
    };

    private readonly record struct Match(HardwareSnapshot Hardware, SensorSnapshot Sensor);

    /// <summary>First sensor (depth-first through sub-hardware) whose identifier is the part of <paramref name="id"/> after <c>hw:</c>.</summary>
    private static Match? Find(IReadOnlyList<HardwareSnapshot> hardware, string id)
    {
        if (!id.StartsWith(SensorIdentity.GenericPrefix, StringComparison.Ordinal)) return null;

        foreach (var h in hardware)
        {
            var match = Find(h, id);
            if (match is not null) return match;
        }

        return null;
    }

    private static Match? Find(HardwareSnapshot hardware, string id)
    {
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.Identifier is { } identifier
                && id.Length == SensorIdentity.GenericPrefix.Length + identifier.Length
                && id.AsSpan(SensorIdentity.GenericPrefix.Length).SequenceEqual(identifier))
            {
                return new Match(hardware, sensor);
            }
        }

        foreach (var sub in hardware.SubHardware)
        {
            var match = Find(sub, id);
            if (match is not null) return match;
        }

        return null;
    }

    private static SensorReading MakeCpuReading(HardwareSnapshot cpu)
    {
        foreach (var name in CpuTemperatureSensorNames)
        {
            var sensor = cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name == name);
            if (sensor is not null)
            {
                return new SensorReading("cpu", "CPU", SensorKind.CpuPackage, null, sensor.Value, cpu.Name, name);
            }
        }

        return new SensorReading("cpu", "CPU", SensorKind.CpuPackage, null, null, cpu.Name, CpuTemperatureSensorName);
    }

    private static SensorReading MakeReading(string id, string label, SensorKind kind, int? gpuIndex, HardwareSnapshot hardware, string sensorName) =>
        new(id, label, kind, gpuIndex,
            hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name == sensorName)?.Value,
            hardware.Name,
            sensorName);

    public static IReadOnlyList<SensorReading> ToUnavailable(IReadOnlyList<SensorReading> readings) =>
        readings.Select(r => r with { ValueCelsius = null }).ToList();

    public static IReadOnlyList<SensorReading> WithCpuPlaceholder(IReadOnlyList<SensorReading> readings)
    {
        if (readings.Any(r => r.Id == "cpu")) return readings;

        var list = new List<SensorReading> { new("cpu", "CPU", SensorKind.CpuPackage, null, null) };
        list.AddRange(readings);
        return list;
    }
}
