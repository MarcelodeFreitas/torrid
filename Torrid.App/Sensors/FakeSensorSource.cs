namespace Torrid.App.Sensors;

using System.Windows.Threading;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

public sealed class FakeSensorSource : ISensorSource, IHardwareStatusSource
{
    private readonly DispatcherTimer _timer;
    // Fixed seed: demo screenshots start from the same values every run.
    private readonly Random _random = new(7);
    private HardwareStatus _status = HardwareStatus.FullAccess;

    public FakeSensorSource()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Tick();
        CurrentReadings = BuildReadings();
    }

    public IReadOnlyList<SensorReading> CurrentReadings { get; private set; }

    public HardwareStatus Status
    {
        get => _status;
        set
        {
            _status = value;
            CurrentReadings = BuildReadings();
            StatusChanged?.Invoke(this, EventArgs.Empty);
            ReadingsUpdated?.Invoke(this, CurrentReadings);
        }
    }

    public event EventHandler<IReadOnlyList<SensorReading>>? ReadingsUpdated;

    public event EventHandler? StatusChanged;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    private void Tick()
    {
        CurrentReadings = BuildReadings();
        ReadingsUpdated?.Invoke(this, CurrentReadings);
    }

    private IReadOnlyList<SensorReading> BuildReadings()
    {
        const string CpuName = "AMD Ryzen 9 5950X";
        const string GpuName = "NVIDIA GeForce RTX 3090";

        var readings = new List<SensorReading>
        {
            // Values wander a little around fixed points so every colour zone except red is on screen
            // (red would fire real toasts while demoing).
            new("cpu", "CPU", SensorKind.CpuPackage, null, Around(57), CpuName, "Core (Tctl/Tdie)"),
            new("gpu0-core", "GPU0 Core", SensorKind.GpuCore, 0, Around(66), GpuName, "GPU Core"),
            new("gpu0-hotspot", "GPU0 Hot", SensorKind.GpuHotSpot, 0, Around(88), GpuName, "GPU Hot Spot"),
            new("gpu0-vram", "GPU0 VRAM", SensorKind.GpuVram, 0, Around(80), GpuName, "GPU Memory Junction"),
            new("gpu1-core", "GPU1 Core", SensorKind.GpuCore, 1, Around(36), GpuName, "GPU Core"),
            new("gpu1-hotspot", "GPU1 Hot", SensorKind.GpuHotSpot, 1, Around(44), GpuName, "GPU Hot Spot"),
            new("gpu1-vram", "GPU1 VRAM", SensorKind.GpuVram, 1, Around(48), GpuName, "GPU Memory Junction"),
        };

        return Status.Level == HardwareAccessLevel.Paused
            ? readings.Select(r => r with { ValueCelsius = null }).ToList()
            : readings;
    }

    private double Around(double centre) => centre + (_random.NextDouble() - 0.5) * 3;
}
