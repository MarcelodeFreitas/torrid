namespace Torrid.Core.Sensors;

using System.Diagnostics;
using LibreHardwareMonitor.Hardware;
using Torrid.Core.Models;

public sealed class SensorService : ISensorSource, IHardwareStatusSource, IDisposable
{
    private readonly System.Timers.Timer _timer;
    private readonly UpdateVisitor _visitor = new();
    private readonly Func<AntiCheatBehavior> _behavior;
    private readonly Func<IReadOnlyList<string>> _extraProcesses;
    private readonly IProcessLister _processLister;
    private readonly object _gate = new();
    private Computer? _computer;
    private HardwareAccessLevel? _openLevel;
    private volatile bool _disposed;

    public SensorService(
        int pollIntervalMs,
        Func<AntiCheatBehavior>? behavior = null,
        Func<IReadOnlyList<string>>? extraProcesses = null,
        IProcessLister? processLister = null)
    {
        _behavior = behavior ?? (() => AntiCheatBehavior.Pause);
        _extraProcesses = extraProcesses ?? (() => Array.Empty<string>());
        _processLister = processLister ?? new SystemProcessLister();

        _timer = new System.Timers.Timer(pollIntervalMs) { AutoReset = false };
        _timer.Elapsed += (_, _) => PollAndReschedule();

        Poll();
    }

    public IReadOnlyList<SensorReading> CurrentReadings { get; private set; } = Array.Empty<SensorReading>();

    public HardwareStatus Status { get; private set; } = HardwareStatus.FullAccess;

    public int PollIntervalMs
    {
        get => (int)_timer.Interval;
        set => _timer.Interval = Math.Max(1, value);
    }

    public event EventHandler<IReadOnlyList<SensorReading>>? ReadingsUpdated;

    public event EventHandler? StatusChanged;

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
            try
            {
                if (!_disposed) _timer.Start();
            }
            catch (ObjectDisposedException)
            {
                // Dispose() raced with this tick; nothing left to reschedule.
            }
        }
    }

    private void Poll()
    {
        IReadOnlyList<SensorReading> readings;
        var statusChanged = false;

        lock (_gate)
        {
            if (_disposed) return;

            HardwareStatus? decided = null;
            try
            {
                // Policy first: an already-running anti-cheat means the hardware (and its driver) is never opened.
                decided = HardwareAccessPolicy.Evaluate(
                    _behavior(), _processLister.GetRunningProcessNames(), _extraProcesses());
            }
            catch (Exception ex)
            {
                // If we cannot tell whether an anti-cheat is running, fail closed: release the hardware, report unavailable.
                Debug.WriteLine($"SensorService: anti-cheat policy evaluation failed, closing hardware: {ex}");
                CloseComputer();
            }

            if (decided is null)
            {
                readings = SensorReadingBuilder.WithCpuPlaceholder(SensorReadingBuilder.ToUnavailable(CurrentReadings));
            }
            else
            {
                try
                {
                    statusChanged = !Equals(decided, Status);
                    Status = decided;

                    EnsureComputerFor(decided.Level);
                    readings = decided.Level == HardwareAccessLevel.Paused
                        ? SensorReadingBuilder.WithCpuPlaceholder(SensorReadingBuilder.ToUnavailable(CurrentReadings))
                        : ReadHardware(decided.Level);
                }
                catch
                {
                    readings = SensorReadingBuilder.ToUnavailable(CurrentReadings);
                }
            }

            CurrentReadings = readings;
        }

        // Raised outside the lock: handlers marshal to the UI thread and must never be able to deadlock Dispose().
        if (statusChanged) StatusChanged?.Invoke(this, EventArgs.Empty);
        ReadingsUpdated?.Invoke(this, readings);
    }

    private void EnsureComputerFor(HardwareAccessLevel level)
    {
        if (_openLevel == level) return;

        CloseComputer();
        if (level == HardwareAccessLevel.Paused) return;

        var computer = new Computer { IsCpuEnabled = level == HardwareAccessLevel.Full, IsGpuEnabled = true };
        try
        {
            computer.Open();
        }
        catch
        {
            try { computer.Close(); } catch { /* best effort: don't leak a half-opened Computer */ }
            throw;
        }

        _computer = computer;
        _openLevel = level;
    }

    private IReadOnlyList<SensorReading> ReadHardware(HardwareAccessLevel level)
    {
        _computer!.Accept(_visitor);
        var readings = SensorReadingBuilder.Build(_computer.Hardware.Select(ToSnapshot).ToList());
        return level == HardwareAccessLevel.GpuOnly ? SensorReadingBuilder.WithCpuPlaceholder(readings) : readings;
    }

    private void CloseComputer()
    {
        var computer = _computer;
        _computer = null;
        _openLevel = null;
        try { computer?.Close(); }
        catch { /* releasing hardware must never take the app down */ }
    }

    private static HardwareSnapshot ToSnapshot(IHardware hardware) => new(
        hardware.HardwareType,
        hardware.Name,
        hardware.Sensors.Select(s => new SensorSnapshot(s.Name, s.SensorType, s.Value, s.Identifier.ToString())).ToList(),
        hardware.SubHardware.Select(ToSnapshot).ToList(),
        hardware.Identifier.ToString());

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _timer.Dispose();
            CloseComputer();
        }
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
