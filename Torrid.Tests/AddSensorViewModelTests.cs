using Torrid.App.ViewModels;
using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

namespace Torrid.Tests;

public class AddSensorViewModelTests
{
    private const string Board = "ASUS ROG CROSSHAIR VIII";
    private const string Cpu = "AMD Ryzen 9 5950X";
    private const string VrmId = "hw:/lpc/nct6798d/0/temperature/4";
    private const string SystemId = "hw:/lpc/nct6798d/0/temperature/1";
    private const string CcdId = "hw:/amdcpu/0/temperature/3";

    private sealed class FakeCatalog : ISensorCatalogSource
    {
        private int _calls;

        public int Calls => _calls;
        public SensorCatalogResult Result { get; set; } = new(Array.Empty<CatalogSensor>(), null);
        public TaskCompletionSource<SensorCatalogResult>? Pending { get; set; }
        public Exception? Throw { get; set; }

        public Task<SensorCatalogResult> ScanAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            if (Throw is not null) return Task.FromException<SensorCatalogResult>(Throw);
            return Pending?.Task ?? Task.FromResult(Result);
        }
    }

    private static readonly CatalogSensor[] Catalog =
    {
        new(CcdId, "CCD1 (Tdie)", Cpu, SensorKind.CpuOther, 61.4),
        new(SystemId, "System", Board, SensorKind.Motherboard, null),
        new(VrmId, "VRM", Board, SensorKind.Motherboard, 52),
    };

    private static (AddSensorViewModel Vm, FakeCatalog Catalog, ConfigManager Config) Make(AppConfig? config = null)
    {
        var catalog = new FakeCatalog { Result = new SensorCatalogResult(Catalog, null) };
        var manager = new ConfigManager(new InMemoryConfigStore(), config ?? new AppConfig(), TimeSpan.Zero);
        return (new AddSensorViewModel(catalog, manager), catalog, manager);
    }

    private static AddSensorItemViewModel Item(AddSensorViewModel vm, string id) =>
        vm.Groups.SelectMany(g => g.Items).Single(i => i.Id == id);

    [Fact]
    public void Opening_NeverScans()
    {
        var (vm, catalog, _) = Make();

        vm.OpenCommand.Execute(null);

        Assert.True(vm.IsOpen);
        Assert.Equal(0, catalog.Calls);
        Assert.Empty(vm.Groups);
        Assert.False(vm.HasMessage);
    }

    [Fact]
    public async Task Scan_GroupsTheResultsByHardware_InCatalogOrder()
    {
        var (vm, _, _) = Make();
        vm.OpenCommand.Execute(null);

        await vm.ScanAsync();

        Assert.Equal(new[] { Cpu, Board }, vm.Groups.Select(g => g.HardwareName));
        Assert.Equal(new[] { "System", "VRM" }, vm.Groups[1].Items.Select(i => i.Name));
        Assert.Equal("61 °C", Item(vm, CcdId).ValueText);
        Assert.True(Item(vm, CcdId).HasValue);
        Assert.False(Item(vm, SystemId).HasValue);
        Assert.Equal("", Item(vm, SystemId).ValueText);
        Assert.False(vm.HasMessage);
        Assert.False(vm.IsScanning);
        Assert.Equal("Scan for sensors", vm.ScanButtonText);
    }

    [Fact]
    public async Task Scan_Unavailable_ShowsTheReason()
    {
        var (vm, catalog, _) = Make();
        catalog.Result = SensorCatalogResult.Unavailable("Monitoring is paused while Riot Vanguard is running.");
        vm.OpenCommand.Execute(null);

        await vm.ScanAsync();

        Assert.Empty(vm.Groups);
        Assert.True(vm.HasMessage);
        Assert.Equal("Monitoring is paused while Riot Vanguard is running.", vm.Message);
    }

    [Fact]
    public async Task Scan_NothingFound_SaysSo()
    {
        var (vm, catalog, _) = Make();
        catalog.Result = new SensorCatalogResult(Array.Empty<CatalogSensor>(), null);
        vm.OpenCommand.Execute(null);

        await vm.ScanAsync();

        Assert.Equal(AddSensorViewModel.NothingFoundMessage, vm.Message);
        Assert.Equal("No temperature sensors found.", AddSensorViewModel.NothingFoundMessage);
    }

    [Fact]
    public async Task Scan_WhileAScanIsRunning_IsIgnored()
    {
        var (vm, catalog, _) = Make();
        catalog.Pending = new TaskCompletionSource<SensorCatalogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.OpenCommand.Execute(null);

        var first = vm.ScanAsync();
        Assert.True(vm.IsScanning);
        Assert.Equal("Scanning…", vm.ScanButtonText);
        Assert.False(vm.ScanCommand.CanExecute(null));

        var second = vm.ScanAsync();
        vm.ScanCommand.Execute(null);
        Assert.True(second.IsCompleted);

        catalog.Pending.SetResult(new SensorCatalogResult(Catalog, null));
        await first;

        Assert.Equal(1, catalog.Calls);
        Assert.False(vm.IsScanning);
        Assert.True(vm.ScanCommand.CanExecute(null));
        Assert.Equal(3, vm.Groups.SelectMany(g => g.Items).Count());
    }

    [Fact]
    public async Task Scan_Exception_BecomesAMessage()
    {
        var (vm, catalog, _) = Make();
        catalog.Throw = new InvalidOperationException("driver exploded");
        vm.OpenCommand.Execute(null);

        await vm.ScanAsync();

        Assert.Equal("Couldn't scan hardware: driver exploded", vm.Message);
        Assert.Empty(vm.Groups);
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public async Task Scan_Cancelled_BecomesAMessage()
    {
        var (vm, catalog, _) = Make();
        catalog.Throw = new OperationCanceledException();
        vm.OpenCommand.Execute(null);

        await vm.ScanAsync();

        Assert.Equal(AddSensorViewModel.CancelledMessage, vm.Message);
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public async Task Close_DiscardsTheResults_AndAResultArrivingAfterCloseIsDropped()
    {
        var (vm, catalog, _) = Make();
        vm.OpenCommand.Execute(null);
        await vm.ScanAsync();

        vm.CloseCommand.Execute(null);

        Assert.False(vm.IsOpen);
        Assert.Empty(vm.Groups);
        Assert.False(vm.HasMessage);

        catalog.Pending = new TaskCompletionSource<SensorCatalogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.OpenCommand.Execute(null);
        var scan = vm.ScanAsync();
        vm.CloseCommand.Execute(null);
        catalog.Pending.SetResult(new SensorCatalogResult(Catalog, null));
        await scan;

        Assert.Empty(vm.Groups);
        Assert.False(vm.HasMessage);
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public async Task Add_ReplacesTheAddedSensorsDictionary_SetsTheWindowOverride_AndMarksTheItem()
    {
        var (vm, _, config) = Make();
        var before = config.Config.AddedSensors;
        vm.OpenCommand.Execute(null);
        await vm.ScanAsync();
        var vrm = Item(vm, VrmId);
        Assert.True(vrm.AddCommand.CanExecute(null));
        Assert.Equal("Add", vrm.AddButtonText);

        vrm.AddCommand.Execute(null);

        Assert.NotSame(before, config.Config.AddedSensors);
        Assert.Empty(before);
        var stored = config.Config.AddedSensors[VrmId];
        Assert.Equal(("VRM", Board, SensorKind.Motherboard), (stored.Name, stored.HardwareName, stored.Kind));
        Assert.True(config.Config.Sensors[VrmId].ShowInWindow);
        Assert.True(vrm.IsAdded);
        Assert.Equal("Added", vrm.AddButtonText);
        Assert.False(vrm.AddCommand.CanExecute(null));
        Assert.False(Item(vm, SystemId).IsAdded);
    }

    [Fact]
    public async Task AlreadyAddedSensors_ShowAsAdded_AndAddingAgainIsANoOp()
    {
        var config = new AppConfig();
        config.AddedSensors = new Dictionary<string, AddedSensor>
        {
            [VrmId] = new() { Name = "My VRM", HardwareName = Board, Kind = SensorKind.Motherboard },
        };
        var (vm, _, manager) = Make(config);
        var dictionary = manager.Config.AddedSensors;
        var changes = 0;
        manager.Changed += (_, _) => changes++;
        vm.OpenCommand.Execute(null);
        await vm.ScanAsync();

        var vrm = Item(vm, VrmId);
        Assert.True(vrm.IsAdded);

        vm.Add(vrm);

        Assert.Equal(0, changes);
        Assert.Same(dictionary, manager.Config.AddedSensors);
        Assert.Equal("My VRM", manager.Config.AddedSensors[VrmId].Name);
        Assert.False(manager.Config.Sensors.ContainsKey(VrmId));
    }

    [Fact]
    public async Task Remove_ReplacesTheDictionaryWithoutTheId_AndClearsItsPreferences()
    {
        var config = new AppConfig();
        config.AddedSensors = new Dictionary<string, AddedSensor>
        {
            [VrmId] = new() { Name = "VRM", HardwareName = Board, Kind = SensorKind.Motherboard },
            [SystemId] = new() { Name = "System", HardwareName = Board, Kind = SensorKind.Motherboard },
        };
        config.Sensors[VrmId] = new SensorPreference { ShowInWindow = true, ShowInTray = true, Label = "Hot VRM" };
        config.Sensors["cpu"] = new SensorPreference { Label = "5950X" };
        var (vm, _, manager) = Make(config);
        var before = manager.Config.AddedSensors;
        vm.OpenCommand.Execute(null);
        await vm.ScanAsync();

        vm.Remove(VrmId);

        Assert.NotSame(before, manager.Config.AddedSensors);
        Assert.Equal(2, before.Count);
        Assert.Equal(new[] { SystemId }, manager.Config.AddedSensors.Keys);
        Assert.False(manager.Config.Sensors.ContainsKey(VrmId));
        Assert.Equal("5950X", manager.Config.Sensors["cpu"].Label);
        Assert.False(Item(vm, VrmId).IsAdded);
        Assert.True(Item(vm, VrmId).AddCommand.CanExecute(null));
    }

    [Fact]
    public void Remove_UnknownId_DoesNotWrite()
    {
        var (vm, _, manager) = Make();
        var changes = 0;
        manager.Changed += (_, _) => changes++;

        vm.Remove("hw:/nothing/here");

        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task WithoutACatalog_ScanExplainsItIsUnavailable()
    {
        var manager = new ConfigManager(new InMemoryConfigStore(), new AppConfig(), TimeSpan.Zero);
        var vm = new AddSensorViewModel(null, manager);
        vm.OpenCommand.Execute(null);

        await vm.ScanAsync();

        Assert.True(vm.HasMessage);
        Assert.Empty(vm.Groups);
    }
}
