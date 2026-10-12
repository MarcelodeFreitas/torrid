using LibreHardwareMonitor.Hardware;
using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

namespace Torrid.Tests;

/// <summary>
/// The group bookkeeping of <see cref="SensorService"/>, driven through its internal hardware seam so no real
/// LibreHardwareMonitor <c>Computer</c> (or driver) is ever opened by these tests.
/// </summary>
public class SensorServiceHardwareGroupsTests
{
    private const HardwareGroups All =
        HardwareGroups.Cpu | HardwareGroups.Gpu | HardwareGroups.Motherboard | HardwareGroups.Memory | HardwareGroups.Storage;

    private const string VrmId = "hw:/lpc/nct6798d/0/temperature/4";
    private const string NvmeId = "hw:/nvme/0/temperature/0";

    private sealed class MutableLister : IProcessLister
    {
        public string[] Running { get; set; } = { "explorer" };
        public IReadOnlyCollection<string> GetRunningProcessNames() => Running;
    }

    /// <summary>Records every open/close and exposes only the hardware of the groups it was opened with.</summary>
    private sealed class FakeHardware
    {
        public List<HardwareGroups> Opened { get; } = new();
        public int Closed { get; private set; }
        public int OpenNow { get; private set; }
        public int MaxOpenAtOnce { get; private set; }
        public int OpenAttempts { get; private set; }
        public Exception? ThrowOnOpen { get; set; }
        public Exception? ThrowOnRead { get; set; }

        public IHardwareComputer Open(HardwareGroups groups)
        {
            OpenAttempts++;
            if (ThrowOnOpen is not null) throw ThrowOnOpen;
            Opened.Add(groups);
            OpenNow++;
            MaxOpenAtOnce = Math.Max(MaxOpenAtOnce, OpenNow);
            return new Computer(this, groups);
        }

        private sealed class Computer : IHardwareComputer
        {
            private readonly FakeHardware _owner;
            private readonly HardwareGroups _groups;
            private bool _closed;

            public Computer(FakeHardware owner, HardwareGroups groups) => (_owner, _groups) = (owner, groups);

            public IReadOnlyList<HardwareSnapshot> Read()
            {
                if (_closed) throw new InvalidOperationException("read after close");
                if (_owner.ThrowOnRead is not null) throw _owner.ThrowOnRead;

                var hardware = new List<HardwareSnapshot>();
                if (_groups.HasFlag(HardwareGroups.Cpu))
                {
                    hardware.Add(new HardwareSnapshot(HardwareType.Cpu, "Ryzen", new[]
                    {
                        new SensorSnapshot("Core (Tctl/Tdie)", SensorType.Temperature, 55, "/amdcpu/0/temperature/2"),
                        new SensorSnapshot("CCD1 (Tdie)", SensorType.Temperature, 50, "/amdcpu/0/temperature/3"),
                    }, Array.Empty<HardwareSnapshot>(), "/amdcpu/0"));
                }

                if (_groups.HasFlag(HardwareGroups.Gpu))
                {
                    hardware.Add(new HardwareSnapshot(HardwareType.GpuNvidia, "RTX", new[]
                    {
                        new SensorSnapshot("GPU Core", SensorType.Temperature, 60, "/gpu-nvidia/0/temperature/0"),
                    }, Array.Empty<HardwareSnapshot>(), "/gpu-nvidia/0"));
                }

                if (_groups.HasFlag(HardwareGroups.Motherboard))
                {
                    var superIo = new HardwareSnapshot(HardwareType.SuperIO, "NCT6798D", new[]
                    {
                        new SensorSnapshot("VRM", SensorType.Temperature, 48, "/lpc/nct6798d/0/temperature/4"),
                    }, Array.Empty<HardwareSnapshot>(), "/lpc/nct6798d/0");
                    hardware.Add(new HardwareSnapshot(HardwareType.Motherboard, "Board",
                        Array.Empty<SensorSnapshot>(), new[] { superIo }, "/motherboard"));
                }

                if (_groups.HasFlag(HardwareGroups.Storage))
                {
                    hardware.Add(new HardwareSnapshot(HardwareType.Storage, "SSD", new[]
                    {
                        new SensorSnapshot("Composite", SensorType.Temperature, 40, "/nvme/0/temperature/0"),
                    }, Array.Empty<HardwareSnapshot>(), "/nvme/0"));
                }

                return hardware;
            }

            public void Close()
            {
                if (_closed) return;
                _closed = true;
                _owner.Closed++;
                _owner.OpenNow--;
            }
        }
    }

    private sealed class Rig : IDisposable
    {
        public FakeHardware Hardware { get; } = new();
        public MutableLister Lister { get; } = new();
        public AntiCheatBehavior Behavior { get; set; } = AntiCheatBehavior.Pause;
        public IReadOnlyDictionary<string, AddedSensor> Added { get; set; } = new Dictionary<string, AddedSensor>();
        public SensorService Service { get; }

        public Rig(Action<Rig>? before = null)
        {
            before?.Invoke(this);
            Service = new SensorService(60_000, () => Behavior, () => Array.Empty<string>(), Lister, () => Added, Hardware.Open);
        }

        public void Dispose() => Service.Dispose();
    }

    private static Dictionary<string, AddedSensor> Added(params (string Id, SensorKind Kind)[] sensors) =>
        sensors.ToDictionary(s => s.Id, s => new AddedSensor { Name = "Added " + s.Kind, Kind = s.Kind });

    [Fact]
    public void Full_NothingAdded_OpensCpuAndGpuOnce()
    {
        using var rig = new Rig();
        rig.Service.PollNow();
        rig.Service.PollNow();

        Assert.Equal(new[] { HardwareGroups.Cpu | HardwareGroups.Gpu }, rig.Hardware.Opened);
        Assert.Equal(0, rig.Hardware.Closed);
        Assert.Equal(new[] { "cpu", "gpu0-core", "gpu0-hotspot", "gpu0-vram" }, rig.Service.CurrentReadings.Select(r => r.Id));
    }

    [Fact]
    public void AddingTheFirstMotherboardSensor_ReopensExactlyOnceWithMotherboard_AndRemovingTheLastReopensWithout()
    {
        using var rig = new Rig();

        rig.Added = Added((VrmId, SensorKind.Motherboard));
        rig.Service.PollNow();
        rig.Service.PollNow();

        Assert.Equal(new[] { HardwareGroups.Cpu | HardwareGroups.Gpu, HardwareGroups.Cpu | HardwareGroups.Gpu | HardwareGroups.Motherboard },
            rig.Hardware.Opened);
        Assert.Equal(1, rig.Hardware.Closed);
        Assert.Equal(1, rig.Hardware.MaxOpenAtOnce);
        var vrm = Assert.Single(rig.Service.CurrentReadings, r => r.Id == VrmId);
        Assert.Equal(48, vrm.ValueCelsius);
        Assert.Equal(SensorKind.Motherboard, vrm.Kind);

        rig.Added = new Dictionary<string, AddedSensor>();
        rig.Service.PollNow();

        Assert.Equal(HardwareGroups.Cpu | HardwareGroups.Gpu, rig.Hardware.Opened[^1]);
        Assert.Equal(3, rig.Hardware.Opened.Count);
        Assert.Equal(2, rig.Hardware.Closed);
        Assert.DoesNotContain(rig.Service.CurrentReadings, r => r.Id == VrmId);
    }

    [Fact]
    public void ReplacingTheDictionaryWithoutChangingTheKinds_DoesNotReopen()
    {
        using var rig = new Rig(r => r.Added = Added((VrmId, SensorKind.Motherboard)));

        rig.Added = Added((VrmId, SensorKind.Motherboard), ("hw:/lpc/nct6798d/0/temperature/1", SensorKind.Motherboard));
        rig.Service.PollNow();

        Assert.Single(rig.Hardware.Opened);
        Assert.Equal(0, rig.Hardware.Closed);
    }

    [Fact]
    public void GpuOnly_NeverOpensMotherboardEvenWhenAdded_AndKeepsTheCpuPlaceholder()
    {
        using var rig = new Rig(r =>
        {
            r.Behavior = AntiCheatBehavior.GpuOnly;
            r.Lister.Running = new[] { "vgc" };
            r.Added = Added((VrmId, SensorKind.Motherboard));
        });

        Assert.Equal(new[] { HardwareGroups.Gpu }, rig.Hardware.Opened);
        var ids = rig.Service.CurrentReadings.Select(r => r.Id).ToList();
        Assert.Equal(new[] { "cpu", "gpu0-core", "gpu0-hotspot", "gpu0-vram", VrmId }, ids);
        Assert.Null(rig.Service.CurrentReadings[0].ValueCelsius);
        Assert.Null(rig.Service.CurrentReadings[^1].ValueCelsius);
    }

    [Fact]
    public void GpuOnly_WithStorageAdded_OpensGpuAndStorage()
    {
        using var rig = new Rig(r =>
        {
            r.Behavior = AntiCheatBehavior.GpuOnly;
            r.Lister.Running = new[] { "vgc" };
            r.Added = Added((NvmeId, SensorKind.Storage));
        });

        Assert.Equal(new[] { HardwareGroups.Gpu | HardwareGroups.Storage }, rig.Hardware.Opened);
        Assert.Equal(40, Assert.Single(rig.Service.CurrentReadings, r => r.Id == NvmeId).ValueCelsius);
    }

    [Fact]
    public void AntiCheatStarting_ClosesEverything_AndStoppingReopensWhatIsWanted()
    {
        using var rig = new Rig(r => r.Added = Added((VrmId, SensorKind.Motherboard)));

        rig.Lister.Running = new[] { "vgc" };
        rig.Service.PollNow();

        Assert.Single(rig.Hardware.Opened);
        Assert.Equal(1, rig.Hardware.Closed);
        Assert.Equal(new[] { "cpu", "gpu0-core", "gpu0-hotspot", "gpu0-vram", VrmId }, rig.Service.CurrentReadings.Select(r => r.Id));
        Assert.All(rig.Service.CurrentReadings, r => Assert.Null(r.ValueCelsius));

        rig.Lister.Running = new[] { "explorer" };
        rig.Service.PollNow();

        Assert.Equal(HardwareGroups.Cpu | HardwareGroups.Gpu | HardwareGroups.Motherboard, rig.Hardware.Opened[^1]);
        Assert.Equal(2, rig.Hardware.Opened.Count);
    }

    [Fact]
    public void Paused_OpensNothing()
    {
        using var rig = new Rig(r =>
        {
            r.Lister.Running = new[] { "vgc" };
            r.Added = Added((VrmId, SensorKind.Motherboard), (NvmeId, SensorKind.Storage));
        });
        rig.Service.PollNow();

        Assert.Empty(rig.Hardware.Opened);
    }

    [Fact]
    public void AddedSensorsDelegateThrowing_IsTreatedAsNothingAdded()
    {
        var hardware = new FakeHardware();
        var service = new SensorService(60_000, () => AntiCheatBehavior.Pause, () => Array.Empty<string>(), new MutableLister(),
            () => throw new InvalidOperationException("config"), hardware.Open);
        using (service)
        {
            service.PollNow();

            Assert.Equal(new[] { HardwareGroups.Cpu | HardwareGroups.Gpu }, hardware.Opened);
            Assert.Equal(4, service.CurrentReadings.Count);
        }
    }

    [Fact]
    public async Task Scan_WhenAnAntiCheatStartedAfterTheLiveComputerOpened_ClosesTheLiveComputer()
    {
        using var rig = new Rig();
        Assert.Equal(1, rig.Hardware.OpenNow);

        rig.Lister.Running = new[] { "vgc" };
        var result = await rig.Service.ScanAsync();

        Assert.Equal("Monitoring is paused while Riot Vanguard is running.", result.UnavailableReason);
        Assert.Empty(result.Sensors);
        Assert.Single(rig.Hardware.Opened);
        Assert.Equal(1, rig.Hardware.Closed);
        Assert.Equal(0, rig.Hardware.OpenNow);
    }

    [Fact]
    public void Dispose_ClosesTheLiveComputer()
    {
        var rig = new Rig();
        rig.Dispose();

        Assert.Equal(1, rig.Hardware.Closed);
        Assert.Equal(0, rig.Hardware.OpenNow);
    }

    [Fact]
    public async Task Scan_AtFull_ClosesTheLiveComputerFirst_ScansAllGroups_ClosesTheTemporaryOne_AndTheNextPollReopens()
    {
        using var rig = new Rig();

        var result = await rig.Service.ScanAsync();

        Assert.Null(result.UnavailableReason);
        Assert.Equal(new[] { HardwareGroups.Cpu | HardwareGroups.Gpu, All }, rig.Hardware.Opened);
        Assert.Equal(2, rig.Hardware.Closed);
        Assert.Equal(0, rig.Hardware.OpenNow);
        Assert.Equal(1, rig.Hardware.MaxOpenAtOnce);
        Assert.Equal(
            new[] { "hw:/amdcpu/0/temperature/3", "hw:/amdcpu/0/temperature/2", "hw:/gpu-nvidia/0/temperature/0", VrmId, NvmeId },
            result.Sensors.Select(s => s.Id));
        Assert.Equal("Board (NCT6798D)", Assert.Single(result.Sensors, s => s.Id == VrmId).HardwareName);

        rig.Service.PollNow();

        Assert.Equal(HardwareGroups.Cpu | HardwareGroups.Gpu, rig.Hardware.Opened[^1]);
        Assert.Equal(1, rig.Hardware.OpenNow);
    }

    [Fact]
    public async Task Scan_WhenTheLiveComputerAlreadyHasEveryGroup_ReusesIt()
    {
        using var rig = new Rig(r => r.Added = Added(
            (VrmId, SensorKind.Motherboard), (NvmeId, SensorKind.Storage), ("hw:/ram/temperature/0", SensorKind.Memory)));

        var result = await rig.Service.ScanAsync();

        Assert.Null(result.UnavailableReason);
        Assert.Equal(new[] { All }, rig.Hardware.Opened);
        Assert.Equal(0, rig.Hardware.Closed);
        Assert.Equal(5, result.Sensors.Count);
    }

    [Fact]
    public async Task Scan_AtGpuOnly_ScansOnlyGpuAndStorage()
    {
        using var rig = new Rig(r =>
        {
            r.Behavior = AntiCheatBehavior.GpuOnly;
            r.Lister.Running = new[] { "vgc" };
        });

        var result = await rig.Service.ScanAsync();

        Assert.Equal(new[] { HardwareGroups.Gpu, HardwareGroups.Gpu | HardwareGroups.Storage }, rig.Hardware.Opened);
        Assert.Equal(new[] { SensorKind.GpuOther, SensorKind.Storage }, result.Sensors.Select(s => s.Kind));
        Assert.Equal(0, rig.Hardware.OpenNow);
    }

    [Fact]
    public async Task Scan_WhilePaused_OpensNothing()
    {
        using var rig = new Rig(r => r.Lister.Running = new[] { "EasyAntiCheat" });

        var result = await rig.Service.ScanAsync();

        Assert.Empty(rig.Hardware.Opened);
        Assert.Empty(result.Sensors);
        Assert.Equal("Monitoring is paused while Easy Anti-Cheat is running.", result.UnavailableReason);
    }

    [Fact]
    public async Task Scan_ReadFailure_IsReported_AndTheTemporaryComputerIsStillClosed()
    {
        using var rig = new Rig();
        rig.Hardware.ThrowOnRead = new InvalidOperationException("driver gone");

        var result = await rig.Service.ScanAsync();

        Assert.Empty(result.Sensors);
        Assert.Equal("Couldn't scan hardware: driver gone", result.UnavailableReason);
        Assert.Equal(0, rig.Hardware.OpenNow);
    }

    [Fact]
    public void Poll_WhoseOpenThrows_LeavesStateConsistent_AndTheNextPollRetriesExactlyOnce()
    {
        using var rig = new Rig(r => r.Hardware.ThrowOnOpen = new InvalidOperationException("driver failed"));
        var attemptsAfterConstruction = rig.Hardware.OpenAttempts;

        rig.Service.PollNow();

        Assert.Equal(attemptsAfterConstruction + 1, rig.Hardware.OpenAttempts);
        Assert.Empty(rig.Hardware.Opened);
        Assert.Equal(0, rig.Hardware.OpenNow);
        Assert.All(rig.Service.CurrentReadings, r => Assert.Null(r.ValueCelsius));

        rig.Hardware.ThrowOnOpen = null;
        rig.Service.PollNow();

        Assert.Equal(attemptsAfterConstruction + 2, rig.Hardware.OpenAttempts);
        Assert.Equal(new[] { HardwareGroups.Cpu | HardwareGroups.Gpu }, rig.Hardware.Opened);
        Assert.Equal(1, rig.Hardware.OpenNow);
        Assert.Contains(rig.Service.CurrentReadings, r => r.ValueCelsius is not null);

        rig.Service.PollNow();

        Assert.Equal(attemptsAfterConstruction + 2, rig.Hardware.OpenAttempts);
        Assert.Equal(0, rig.Hardware.Closed);
    }

    [Fact]
    public async Task Scan_OpenFailure_IsReported()
    {
        using var rig = new Rig();
        rig.Hardware.ThrowOnOpen = new InvalidOperationException("no access");

        var result = await rig.Service.ScanAsync();

        Assert.Empty(result.Sensors);
        Assert.Equal("Couldn't scan hardware: no access", result.UnavailableReason);
        Assert.Equal(0, rig.Hardware.OpenNow);
    }

    [Fact]
    public async Task Scan_AfterDispose_IsReportedWithoutOpening()
    {
        var rig = new Rig();
        rig.Dispose();

        var result = await rig.Service.ScanAsync();

        Assert.Empty(result.Sensors);
        Assert.NotNull(result.UnavailableReason);
        Assert.Single(rig.Hardware.Opened);
    }
}
