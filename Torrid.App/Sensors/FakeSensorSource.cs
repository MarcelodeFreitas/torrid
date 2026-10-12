namespace Torrid.App.Sensors;

using System.Windows.Threading;
using LibreHardwareMonitor.Hardware;
using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

public sealed class FakeSensorSource : ISensorSource, IHardwareStatusSource, ISensorCatalogSource
{
    private const string CpuName = "AMD Ryzen 9 5950X";
    private const string GpuName = "NVIDIA GeForce RTX 3090";
    private const string BoardName = "ASUS ROG CROSSHAIR VIII";
    private const string SsdName = "Samsung SSD 980 PRO 1TB";

    /// <summary>The extra temperature sensors the demo "hardware" offers to the Add sensor panel, with their centre values.</summary>
    private static readonly (HardwareType Type, string HardwareName, string HardwareId, (string Name, string Id, double Centre)[] Sensors)[] DemoHardware =
    {
        (HardwareType.Cpu, CpuName, "/amdcpu/0", new[]
        {
            ("CCD1 (Tdie)", "/amdcpu/0/temperature/3", 61.0),
            ("CCD2 (Tdie)", "/amdcpu/0/temperature/4", 58.0),
            ("SoC", "/amdcpu/0/temperature/6", 47.0),
        }),
        (HardwareType.Motherboard, BoardName, "/lpc/nct6798d/0", new[]
        {
            ("VRM", "/lpc/nct6798d/0/temperature/4", 52.0),
            ("System", "/lpc/nct6798d/0/temperature/1", 36.0),
            ("Chipset", "/lpc/nct6798d/0/temperature/5", 55.0),
        }),
        (HardwareType.Storage, SsdName, "/nvme/0", new[]
        {
            ("Composite", "/nvme/0/temperature/0", 41.0),
        }),
    };

    private readonly DispatcherTimer _timer;
    private readonly Func<IReadOnlyDictionary<string, AddedSensor>> _addedSensors;
    // Fixed seeds: demo screenshots start from the same values every run. Added sensors draw from their own
    // generator so adding one never changes the built-in values.
    private readonly Random _random = new(7);
    private readonly Random _addedRandom = new(11);
    private HardwareStatus _status = HardwareStatus.FullAccess;

    public FakeSensorSource(Func<IReadOnlyDictionary<string, AddedSensor>>? addedSensors = null)
    {
        _addedSensors = addedSensors ?? (() => new Dictionary<string, AddedSensor>());
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

    /// <summary>The deterministic demo catalog, following the same anti-cheat rules as the real scan.</summary>
    public Task<SensorCatalogResult> ScanAsync(CancellationToken ct = default)
    {
        if (Status.Level == HardwareAccessLevel.Paused)
        {
            return Task.FromResult(SensorCatalogResult.PausedFor(Status.AntiCheat));
        }

        var catalog = SensorCatalogBuilder.Build(Snapshots(Status.Level, centre => centre));
        return Task.FromResult(new SensorCatalogResult(catalog, null));
    }

    private void Tick()
    {
        CurrentReadings = BuildReadings();
        ReadingsUpdated?.Invoke(this, CurrentReadings);
    }

    private IReadOnlyList<SensorReading> BuildReadings()
    {
        var readings = new List<SensorReading>
        {
            // Values wander a little around fixed points so every colour zone except red is on screen
            // (red would fire real toasts while demoing).
            new("cpu", "CPU", SensorKind.CpuPackage, null, Around(_random, 57), CpuName, "Core (Tctl/Tdie)"),
            new("gpu0-core", "GPU0 Core", SensorKind.GpuCore, 0, Around(_random, 66), GpuName, "GPU Core"),
            new("gpu0-hotspot", "GPU0 Hot", SensorKind.GpuHotSpot, 0, Around(_random, 88), GpuName, "GPU Hot Spot"),
            new("gpu0-vram", "GPU0 VRAM", SensorKind.GpuVram, 0, Around(_random, 80), GpuName, "GPU Memory Junction"),
            new("gpu1-core", "GPU1 Core", SensorKind.GpuCore, 1, Around(_random, 36), GpuName, "GPU Core"),
            new("gpu1-hotspot", "GPU1 Hot", SensorKind.GpuHotSpot, 1, Around(_random, 44), GpuName, "GPU Hot Spot"),
            new("gpu1-vram", "GPU1 VRAM", SensorKind.GpuVram, 1, Around(_random, 48), GpuName, "GPU Memory Junction"),
        };

        var added = _addedSensors() ?? new Dictionary<string, AddedSensor>();
        var addedIds = added
            .Where(kv => kv.Value is not null && SensorIdentity.IsGenericId(kv.Key))
            .Select(kv => kv.Key)
            .ToList();
        if (addedIds.Count > 0)
        {
            // Only the added sensors are read from the demo tree; the built-ins above keep their own values.
            var addedReadings = SensorReadingBuilder.Build(
                Snapshots(Status.Level, centre => Around(_addedRandom, centre)),
                addedIds,
                id => added.TryGetValue(id, out var info) ? info : null);
            readings.AddRange(addedReadings.Where(r => SensorIdentity.IsGenericId(r.Id)));
        }

        return Status.Level == HardwareAccessLevel.Paused
            ? readings.Select(r => r with { ValueCelsius = null }).ToList()
            : readings;
    }

    /// <summary>The demo hardware tree, limited to the groups the level permits (none while paused).</summary>
    private static IReadOnlyList<HardwareSnapshot> Snapshots(HardwareAccessLevel level, Func<double, double> value)
    {
        var permitted = HardwareGroupsPolicy.ForLevel(level);
        return DemoHardware
            .Where(h => permitted.HasFlag(HardwareGroupsPolicy.GroupFor(SensorReadingBuilder.KindFor(h.Type))))
            .Select(h => new HardwareSnapshot(
                h.Type,
                h.HardwareName,
                h.Sensors.Select(s => new SensorSnapshot(s.Name, SensorType.Temperature, value(s.Centre), s.Id)).ToList(),
                Array.Empty<HardwareSnapshot>(),
                h.HardwareId))
            .ToList();
    }

    private static double Around(Random random, double centre) => centre + (random.NextDouble() - 0.5) * 3;
}
