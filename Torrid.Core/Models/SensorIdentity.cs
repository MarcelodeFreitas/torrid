namespace Torrid.Core.Models;

using System.Text.RegularExpressions;

public sealed record SensorIdentity(string Id, string DefaultLabel, SensorKind Kind, int? GpuIndex)
{
    private static readonly Regex GpuId = new(@"^gpu(\d+)-(core|hotspot|vram)$", RegexOptions.Compiled);

    public static SensorIdentity FromReading(SensorReading reading) =>
        new(reading.Id, reading.Label, reading.Kind, reading.GpuIndex);

    public static SensorIdentity? TryParse(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (id == "cpu") return new SensorIdentity("cpu", "CPU", SensorKind.CpuPackage, null);

        var match = GpuId.Match(id);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var index)) return null;

        return match.Groups[2].Value switch
        {
            "core" => new SensorIdentity(id, $"GPU{index} Core", SensorKind.GpuCore, index),
            "hotspot" => new SensorIdentity(id, $"GPU{index} Hot", SensorKind.GpuHotSpot, index),
            "vram" => new SensorIdentity(id, $"GPU{index} VRAM", SensorKind.GpuVram, index),
            _ => null,
        };
    }
}
