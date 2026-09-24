namespace Torrid.Core.Sensors;

using LibreHardwareMonitor.Hardware;

public sealed record HardwareSnapshot(
    HardwareType HardwareType,
    string Name,
    IReadOnlyList<SensorSnapshot> Sensors,
    IReadOnlyList<HardwareSnapshot> SubHardware);

public sealed record SensorSnapshot(
    string Name,
    SensorType SensorType,
    double? Value);
