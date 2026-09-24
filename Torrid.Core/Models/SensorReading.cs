namespace Torrid.Core.Models;

public sealed record SensorReading(
    string Id,
    string Label,
    SensorKind Kind,
    int? GpuIndex,
    double? ValueCelsius);
