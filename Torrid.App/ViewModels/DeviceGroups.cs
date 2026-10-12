namespace Torrid.App.ViewModels;

using Torrid.Core.Models;

/// <summary>Which device group ("CPU", "GPU 0", "Motherboard", ...) a sensor belongs to, and the order of the groups.</summary>
internal readonly record struct DeviceGroup(string Key, string Title, int Rank, int Index);

/// <summary>
/// Shared by the Home and Sensors pages: built-in sensors group by GPU index (CPU otherwise); added sensors group by kind,
/// with extra CPU sensors joining the CPU and extra GPU sensors joining the GPU whose hardware name they share.
/// </summary>
internal static class DeviceGroups
{
    public static readonly DeviceGroup Cpu = new("cpu", "CPU", 0, 0);
    public static readonly DeviceGroup Motherboard = new("motherboard", "Motherboard", 2, 0);
    public static readonly DeviceGroup Memory = new("memory", "Memory", 3, 0);
    public static readonly DeviceGroup Storage = new("storage", "Storage", 4, 0);
    public static readonly DeviceGroup Other = new("other", "Other", 5, 0);

    public static DeviceGroup Gpu(int index) => new($"gpu{index}", $"GPU {index}", 1, index);

    /// <param name="gpuNames">The hardware name of each detected GPU index (from the built-in GPU readings).</param>
    public static DeviceGroup For(SensorKind kind, int? gpuIndex, string? hardwareName, IReadOnlyDictionary<int, string?> gpuNames)
    {
        if (gpuIndex is int index) return Gpu(index);

        return kind switch
        {
            SensorKind.CpuPackage or SensorKind.CpuOther => Cpu,
            SensorKind.GpuOther or SensorKind.GpuCore or SensorKind.GpuHotSpot or SensorKind.GpuVram =>
                GpuIndexFor(hardwareName, gpuNames) is int match ? Gpu(match) : Other,
            SensorKind.Motherboard => Motherboard,
            SensorKind.Memory => Memory,
            SensorKind.Storage => Storage,
            _ => Other,
        };
    }

    /// <summary>The hardware name of every GPU index among <paramref name="readings"/> (first non-blank name wins).</summary>
    public static IReadOnlyDictionary<int, string?> GpuNames(IEnumerable<SensorReading> readings)
    {
        var names = new Dictionary<int, string?>();
        foreach (var reading in readings)
        {
            if (reading.GpuIndex is not int index) continue;
            if (!names.TryGetValue(index, out var existing) || string.IsNullOrWhiteSpace(existing)) names[index] = reading.HardwareName;
        }

        return names;
    }

    /// <summary>The distinct non-blank hardware names, joined for a group subtitle (null when there are none).</summary>
    public static string? Subtitle(IEnumerable<string?> hardwareNames)
    {
        var names = hardwareNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal).ToList();
        return names.Count == 0 ? null : string.Join(" · ", names);
    }

    /// <summary>The single GPU index with this hardware name; null when there is none or several GPUs share the name.</summary>
    private static int? GpuIndexFor(string? hardwareName, IReadOnlyDictionary<int, string?> gpuNames)
    {
        if (string.IsNullOrWhiteSpace(hardwareName)) return null;

        int? match = null;
        foreach (var (index, name) in gpuNames)
        {
            if (!string.Equals(name, hardwareName, StringComparison.Ordinal)) continue;
            if (match is not null) return null;
            match = index;
        }

        return match;
    }
}
