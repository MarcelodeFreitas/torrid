namespace Torrid.Core.Models;

/// <remarks>
/// Serialized as dictionary keys in config.json: append new values, never rename or reorder existing ones.
/// </remarks>
public enum SensorKind
{
    CpuPackage,
    GpuCore,
    GpuHotSpot,
    GpuVram,
    CpuOther,
    GpuOther,
    Motherboard,
    Storage,
    Memory,
    OtherTemp
}
