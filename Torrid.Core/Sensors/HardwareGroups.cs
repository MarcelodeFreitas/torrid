namespace Torrid.Core.Sensors;

using Torrid.Core.Models;

/// <summary>The LibreHardwareMonitor hardware groups Torrid can enable on a <c>Computer</c>.</summary>
[Flags]
public enum HardwareGroups
{
    None = 0,
    Cpu = 1,
    Gpu = 2,
    Motherboard = 4,
    Memory = 8,
    Storage = 16,
}

/// <summary>
/// Which hardware groups may be opened (the anti-cheat level's ceiling) and which are actually needed
/// (CPU + GPU for the built-in sensors, plus the groups of the sensors the user added). CPU, Motherboard and
/// Memory use the PawnIO driver, so they are never permitted below <see cref="HardwareAccessLevel.Full"/>.
/// </summary>
public static class HardwareGroupsPolicy
{
    private const HardwareGroups All =
        HardwareGroups.Cpu | HardwareGroups.Gpu | HardwareGroups.Motherboard | HardwareGroups.Memory | HardwareGroups.Storage;

    /// <summary>The most a level permits. Unknown levels fail closed.</summary>
    public static HardwareGroups ForLevel(HardwareAccessLevel level) => level switch
    {
        HardwareAccessLevel.Full => All,
        // Storage reads use plain user-mode IOCTLs (no PawnIO), so they are allowed next to an anti-cheat.
        HardwareAccessLevel.GpuOnly => HardwareGroups.Gpu | HardwareGroups.Storage,
        _ => HardwareGroups.None,
    };

    /// <summary>
    /// CPU + GPU (the built-in sensors) plus the group each added kind lives in. <see cref="SensorKind.OtherTemp"/>
    /// adds nothing: such sensors are only found, best effort, in groups that are already open.
    /// </summary>
    public static HardwareGroups Wanted(IEnumerable<SensorKind>? addedKinds)
    {
        var wanted = HardwareGroups.Cpu | HardwareGroups.Gpu;
        if (addedKinds is null) return wanted;

        foreach (var kind in addedKinds) wanted |= GroupFor(kind);
        return wanted;
    }

    /// <summary>What is opened: what is wanted, capped by what the level permits.</summary>
    public static HardwareGroups Effective(HardwareAccessLevel level, IEnumerable<SensorKind>? addedKinds) =>
        ForLevel(level) & Wanted(addedKinds);

    /// <summary>The group a sensor of this kind is read from (<see cref="HardwareGroups.None"/> for <see cref="SensorKind.OtherTemp"/>).</summary>
    public static HardwareGroups GroupFor(SensorKind kind) => kind switch
    {
        SensorKind.CpuPackage or SensorKind.CpuOther => HardwareGroups.Cpu,
        SensorKind.GpuCore or SensorKind.GpuHotSpot or SensorKind.GpuVram or SensorKind.GpuOther => HardwareGroups.Gpu,
        SensorKind.Motherboard => HardwareGroups.Motherboard,
        SensorKind.Memory => HardwareGroups.Memory,
        SensorKind.Storage => HardwareGroups.Storage,
        _ => HardwareGroups.None,
    };
}
