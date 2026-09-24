namespace Torrid.Core.Sensors;

using Torrid.Core.Models;

public interface ISensorSource
{
    event EventHandler<IReadOnlyList<SensorReading>>? ReadingsUpdated;
    IReadOnlyList<SensorReading> CurrentReadings { get; }
    void Start();
    void Stop();
}
