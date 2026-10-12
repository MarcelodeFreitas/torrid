namespace Torrid.Core.Sensors;

using LibreHardwareMonitor.Hardware;
using Torrid.Core.Models;

/// <summary>One temperature sensor LibreHardwareMonitor can see, as offered by the Add sensor panel.</summary>
public sealed record CatalogSensor(string Id, string Name, string HardwareName, SensorKind Kind, double? ValueCelsius);

public static class SensorCatalogBuilder
{
    /// <summary>
    /// Every temperature sensor in the hardware tree (sub-hardware included) that has an identifier,
    /// de-duplicated by id and ordered CPU, GPU, motherboard, memory, storage, other, then by hardware and sensor name.
    /// </summary>
    public static IReadOnlyList<CatalogSensor> Build(IReadOnlyList<HardwareSnapshot> hardware)
    {
        var found = new List<CatalogSensor>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var h in hardware) Collect(h, parent: null, found, seen);

        return found
            .OrderBy(s => KindOrder(s.Kind))
            .ThenBy(s => s.HardwareName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void Collect(HardwareSnapshot hardware, HardwareSnapshot? parent, List<CatalogSensor> into, HashSet<string> seen)
    {
        var kind = SensorReadingBuilder.KindFor(hardware.HardwareType);
        var hardwareName = DisplayName(hardware, parent);

        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.SensorType != SensorType.Temperature || string.IsNullOrEmpty(sensor.Identifier)) continue;

            var id = SensorIdentity.GenericPrefix + sensor.Identifier;
            if (!seen.Add(id)) continue;

            into.Add(new CatalogSensor(id, sensor.Name, hardwareName, kind, sensor.Value));
        }

        foreach (var sub in hardware.SubHardware) Collect(sub, hardware, into, seen);
    }

    /// <summary>A Super I/O chip under a motherboard is shown as "Board (Chip)"; everything else uses its own name.</summary>
    private static string DisplayName(HardwareSnapshot hardware, HardwareSnapshot? parent)
    {
        if (hardware.HardwareType == HardwareType.SuperIO && parent is not null && !string.IsNullOrWhiteSpace(parent.Name))
        {
            return string.IsNullOrWhiteSpace(hardware.Name) ? parent.Name : $"{parent.Name} ({hardware.Name})";
        }

        return hardware.Name;
    }

    private static int KindOrder(SensorKind kind) => kind switch
    {
        SensorKind.CpuOther => 0,
        SensorKind.GpuOther => 1,
        SensorKind.Motherboard => 2,
        SensorKind.Memory => 3,
        SensorKind.Storage => 4,
        _ => 5,
    };
}
