namespace Torrid.Core.Sensors;

using System.Diagnostics;
using Torrid.Core.Config;
using Torrid.Core.Models;

public sealed class SensorService : ISensorSource, IHardwareStatusSource, ISensorCatalogSource, IDisposable
{
    private readonly System.Timers.Timer _timer;
    private readonly Func<AntiCheatBehavior> _behavior;
    private readonly Func<IReadOnlyList<string>> _extraProcesses;
    private readonly IProcessLister _processLister;
    private readonly Func<IReadOnlyDictionary<string, AddedSensor>> _addedSensors;
    private readonly Func<HardwareGroups, IHardwareComputer> _openComputer;
    private readonly object _gate = new();
    private IHardwareComputer? _computer;
    private HardwareGroups _openGroups = HardwareGroups.None;
    private IReadOnlyDictionary<string, AddedSensor>? _addedSource;
    private AddedView _added = AddedView.Empty;
    private volatile bool _disposed;

    /// <param name="addedSensors">
    /// The sensors the user added (<see cref="AppConfig.AddedSensors"/>). Read on the poll thread: the UI thread must
    /// replace the dictionary wholesale, never mutate it in place.
    /// </param>
    public SensorService(
        int pollIntervalMs,
        Func<AntiCheatBehavior>? behavior = null,
        Func<IReadOnlyList<string>>? extraProcesses = null,
        IProcessLister? processLister = null,
        Func<IReadOnlyDictionary<string, AddedSensor>>? addedSensors = null)
        : this(pollIntervalMs, behavior, extraProcesses, processLister, addedSensors, LhmHardwareComputer.Open)
    {
    }

    internal SensorService(
        int pollIntervalMs,
        Func<AntiCheatBehavior>? behavior,
        Func<IReadOnlyList<string>>? extraProcesses,
        IProcessLister? processLister,
        Func<IReadOnlyDictionary<string, AddedSensor>>? addedSensors,
        Func<HardwareGroups, IHardwareComputer> openComputer)
    {
        _behavior = behavior ?? (() => AntiCheatBehavior.Pause);
        _extraProcesses = extraProcesses ?? (() => Array.Empty<string>());
        _processLister = processLister ?? new SystemProcessLister();
        _addedSensors = addedSensors ?? (() => AddedView.Empty.Map);
        _openComputer = openComputer;

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

    /// <summary>Polls once, synchronously (tests drive the service with this instead of the timer).</summary>
    internal void PollNow() => Poll();

    /// <summary>
    /// Lists every temperature sensor the current anti-cheat level permits, on a thread-pool thread. Refused while
    /// paused (no hardware is touched); at GPU-only only GPU and storage are scanned.
    /// </summary>
    public Task<SensorCatalogResult> ScanAsync(CancellationToken ct = default) => Task.Run(() => Scan(ct), ct);

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
                decided = EvaluatePolicy();
            }
            catch (Exception ex)
            {
                // If we cannot tell whether an anti-cheat is running, fail closed: release the hardware, report unavailable.
                Debug.WriteLine($"SensorService: anti-cheat policy evaluation failed, closing hardware: {ex}");
                CloseComputer();
            }

            var added = ReadAddedSensors();

            if (decided is null)
            {
                readings = UnavailableReadings(added);
            }
            else
            {
                try
                {
                    statusChanged = !Equals(decided, Status);
                    Status = decided;

                    EnsureComputerFor(HardwareGroupsPolicy.Effective(decided.Level, added.Kinds));
                    readings = _computer is null ? UnavailableReadings(added) : ReadHardware(decided.Level, added);
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

    private SensorCatalogResult Scan(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_disposed) return SensorCatalogResult.Failed("Torrid is shutting down.");

            HardwareStatus status;
            try
            {
                status = EvaluatePolicy();
            }
            catch (Exception ex)
            {
                // Same fail-closed rule as Poll: unknown anti-cheat state means no hardware at all.
                Debug.WriteLine($"SensorService: anti-cheat policy evaluation failed during scan, closing hardware: {ex}");
                CloseComputer();
                return SensorCatalogResult.Failed(ex.Message);
            }

            var groups = HardwareGroupsPolicy.ForLevel(status.Level);
            if (status.Level == HardwareAccessLevel.Paused || groups == HardwareGroups.None)
            {
                // An anti-cheat started since the last poll: release the live hardware now rather than on the next tick.
                CloseComputer();
                return SensorCatalogResult.PausedFor(status.AntiCheat);
            }

            ct.ThrowIfCancellationRequested();

            IHardwareComputer? temporary = null;
            try
            {
                IHardwareComputer computer;
                if (_computer is not null && _openGroups == groups)
                {
                    computer = _computer;
                }
                else
                {
                    // LibreHardwareMonitor's Computer.Close() releases process-wide state (CPUID op-codes, the ISA/SMBus
                    // mutexes), so two Computers must never be open at once: release the live one here; the next poll
                    // reopens exactly the groups it needs.
                    CloseComputer();
                    temporary = _openComputer(groups);
                    computer = temporary;
                }

                return new SensorCatalogResult(SensorCatalogBuilder.Build(computer.Read()), null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SensorService: hardware scan failed: {ex}");
                return SensorCatalogResult.Failed(ex.Message);
            }
            finally
            {
                if (temporary is not null)
                {
                    try { temporary.Close(); }
                    catch { /* releasing hardware must never take the app down */ }
                }
            }
        }
    }

    private HardwareStatus EvaluatePolicy() =>
        HardwareAccessPolicy.Evaluate(_behavior(), _processLister.GetRunningProcessNames(), _extraProcesses());

    /// <summary>The added sensors, re-derived only when the config replaced its dictionary (reference change).</summary>
    private AddedView ReadAddedSensors()
    {
        try
        {
            var source = _addedSensors();
            if (!ReferenceEquals(source, _addedSource))
            {
                _addedSource = source;
                _added = source is null ? AddedView.Empty : AddedView.From(source);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SensorService: reading the added sensors failed, ignoring them: {ex}");
            _addedSource = null;
            _added = AddedView.Empty;
        }

        return _added;
    }

    private void EnsureComputerFor(HardwareGroups groups)
    {
        if (_computer is not null && _openGroups == groups) return;

        CloseComputer();
        if (groups == HardwareGroups.None) return;

        _computer = _openComputer(groups);
        _openGroups = groups;
    }

    private IReadOnlyList<SensorReading> ReadHardware(HardwareAccessLevel level, AddedView added)
    {
        var readings = SensorReadingBuilder.Build(_computer!.Read(), added.Ids.Count == 0 ? null : added.Ids, added.Info);
        return level == HardwareAccessLevel.GpuOnly ? SensorReadingBuilder.WithCpuPlaceholder(readings) : readings;
    }

    /// <summary>
    /// No hardware: the last built-in readings nulled (plus the CPU placeholder) and a null reading per added sensor,
    /// so every row keeps its name and shows "--".
    /// </summary>
    private IReadOnlyList<SensorReading> UnavailableReadings(AddedView added)
    {
        var builtIns = CurrentReadings.Where(r => !SensorIdentity.IsGenericId(r.Id)).ToList();
        var readings = SensorReadingBuilder.WithCpuPlaceholder(SensorReadingBuilder.ToUnavailable(builtIns));
        if (added.Ids.Count == 0) return readings;

        var list = readings.ToList();
        list.AddRange(SensorReadingBuilder.Build(Array.Empty<HardwareSnapshot>(), added.Ids, added.Info));
        return list;
    }

    private void CloseComputer()
    {
        var computer = _computer;
        _computer = null;
        _openGroups = HardwareGroups.None;
        try { computer?.Close(); }
        catch { /* releasing hardware must never take the app down */ }
    }

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

    /// <summary>The valid added ids, their kinds and a lookup, derived once per config dictionary.</summary>
    private sealed class AddedView
    {
        public static AddedView Empty { get; } = new(new Dictionary<string, AddedSensor>(), Array.Empty<string>());

        private AddedView(IReadOnlyDictionary<string, AddedSensor> map, IReadOnlyList<string> ids)
        {
            Map = map;
            Ids = ids;
            Kinds = ids.Select(id => map[id].Kind).ToArray();
            Info = id => map.TryGetValue(id, out var info) ? info : null;
        }

        public IReadOnlyDictionary<string, AddedSensor> Map { get; }

        public IReadOnlyList<string> Ids { get; }

        public IReadOnlyList<SensorKind> Kinds { get; }

        public Func<string, AddedSensor?> Info { get; }

        /// <summary>Hand-edited garbage (invalid ids, null entries) is ignored rather than producing rows.</summary>
        public static AddedView From(IReadOnlyDictionary<string, AddedSensor> source)
        {
            var ids = source
                .Where(kv => kv.Value is not null && SensorIdentity.IsGenericId(kv.Key))
                .Select(kv => kv.Key)
                .ToArray();
            return new AddedView(source, ids);
        }
    }
}
