namespace Torrid.App.Sensors;

using System.Windows.Threading;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

public sealed class FakeSensorSource : ISensorSource
{
    private readonly DispatcherTimer _timer;
    private readonly Random _random = new();

    public FakeSensorSource()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Tick();
        CurrentReadings = BuildReadings();
    }

    public IReadOnlyList<SensorReading> CurrentReadings { get; private set; }

    public event EventHandler<IReadOnlyList<SensorReading>>? ReadingsUpdated;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    private void Tick()
    {
        CurrentReadings = BuildReadings();
        ReadingsUpdated?.Invoke(this, CurrentReadings);
    }

    private IReadOnlyList<SensorReading> BuildReadings() => new List<SensorReading>
    {
        new("cpu", "CPU", SensorKind.CpuPackage, null, NextValue(30, 95)),
        new("gpu0-core", "GPU0 Core", SensorKind.GpuCore, 0, NextValue(30, 95)),
        new("gpu0-hotspot", "GPU0 Hot", SensorKind.GpuHotSpot, 0, NextValue(40, 100)),
        new("gpu0-vram", "GPU0 VRAM", SensorKind.GpuVram, 0, NextValue(40, 100)),
        new("gpu1-core", "GPU1 Core", SensorKind.GpuCore, 1, NextValue(30, 95)),
        new("gpu1-hotspot", "GPU1 Hot", SensorKind.GpuHotSpot, 1, NextValue(40, 100)),
        new("gpu1-vram", "GPU1 VRAM", SensorKind.GpuVram, 1, NextValue(40, 100)),
    };

    private double NextValue(double min, double max) => min + _random.NextDouble() * (max - min);
}
