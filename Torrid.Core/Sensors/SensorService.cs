namespace Torrid.Core.Sensors;

using LibreHardwareMonitor.Hardware;
using Torrid.Core.Models;

public sealed class SensorService : ISensorSource, IDisposable
{
    private readonly Computer _computer;
    private readonly System.Timers.Timer _timer;
    private readonly UpdateVisitor _visitor = new();

    public SensorService(int pollIntervalMs)
    {
        _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
        _computer.Open();

        _timer = new System.Timers.Timer(pollIntervalMs) { AutoReset = false };
        _timer.Elapsed += (_, _) => PollAndReschedule();

        Poll();
    }

    public IReadOnlyList<SensorReading> CurrentReadings { get; private set; } = Array.Empty<SensorReading>();

    public int PollIntervalMs
    {
        get => (int)_timer.Interval;
        set => _timer.Interval = Math.Max(1, value);
    }

    public event EventHandler<IReadOnlyList<SensorReading>>? ReadingsUpdated;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    private void PollAndReschedule()
    {
        try
        {
            Poll();
        }
        finally
        {
            _timer.Start();
        }
    }

    private void Poll()
    {
        try
        {
            _computer.Accept(_visitor);
            var snapshot = _computer.Hardware.Select(ToSnapshot).ToList();
            var readings = SensorReadingBuilder.Build(snapshot);
            CurrentReadings = readings;
            ReadingsUpdated?.Invoke(this, readings);
        }
        catch
        {
            var unavailable = SensorReadingBuilder.ToUnavailable(CurrentReadings);
            CurrentReadings = unavailable;
            ReadingsUpdated?.Invoke(this, unavailable);
        }
    }

    private static HardwareSnapshot ToSnapshot(IHardware hardware) => new(
        hardware.HardwareType,
        hardware.Name,
        hardware.Sensors.Select(s => new SensorSnapshot(s.Name, s.SensorType, s.Value)).ToList(),
        hardware.SubHardware.Select(ToSnapshot).ToList());

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        _computer.Close();
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) sub.Accept(this);
        }

        public void VisitSensor(ISensor sensor) { }

        public void VisitParameter(IParameter parameter) { }
    }
}
