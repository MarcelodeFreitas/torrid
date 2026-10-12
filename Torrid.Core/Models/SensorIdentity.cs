namespace Torrid.Core.Models;

using System.Text.RegularExpressions;
using Torrid.Core.Config;

public sealed record SensorIdentity(string Id, string DefaultLabel, SensorKind Kind, int? GpuIndex)
{
    public const string GenericPrefix = "hw:";
    public const int MaxGenericTailLength = 200;

    private static readonly Regex GpuId = new(@"^gpu(0|[1-9][0-9]*)-(core|hotspot|vram)\z", RegexOptions.Compiled);

    public static SensorIdentity FromReading(SensorReading reading) =>
        new(reading.Id, reading.Label, reading.Kind, reading.GpuIndex);

    /// <summary>
    /// A generic sensor id: <c>hw:</c> followed by 1..200 characters with no whitespace or control characters.
    /// </summary>
    public static bool IsGenericId(string? id)
    {
        if (id is null || !id.StartsWith(GenericPrefix, StringComparison.Ordinal)) return false;

        var tail = id.AsSpan(GenericPrefix.Length);
        if (tail.IsEmpty || tail.Length > MaxGenericTailLength) return false;

        foreach (var c in tail)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c)) return false;
        }

        return true;
    }

    /// <summary>The identity for a built-in id, or a placeholder for a generic <c>hw:</c> id (see <see cref="ForAdded"/>).</summary>
    public static SensorIdentity? TryParse(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (id == "cpu") return new SensorIdentity("cpu", "CPU", SensorKind.CpuPackage, null);

        if (IsGenericId(id))
        {
            return new SensorIdentity(id, id.Substring(GenericPrefix.Length), SensorKind.OtherTemp, null);
        }

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

    public static SensorIdentity ForAdded(string id, AddedSensor info) =>
        new(id, info.Name, info.Kind, null);
}
