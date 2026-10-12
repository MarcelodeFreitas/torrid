namespace Torrid.Core.Sensors;

using LibreHardwareMonitor.Hardware;

/// <param name="Identifier">LibreHardwareMonitor's hardware identifier string (e.g. <c>/lpc/nct6798d</c>), when known.</param>
public sealed record HardwareSnapshot(
    HardwareType HardwareType,
    string Name,
    IReadOnlyList<SensorSnapshot> Sensors,
    IReadOnlyList<HardwareSnapshot> SubHardware,
    string? Identifier = null);

/// <param name="Identifier">LibreHardwareMonitor's sensor identifier string (e.g. <c>/amdcpu/0/temperature/3</c>), when known.</param>
public sealed record SensorSnapshot(
    string Name,
    SensorType SensorType,
    double? Value,
    string? Identifier = null);
