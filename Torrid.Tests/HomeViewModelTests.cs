using Torrid.App.ViewModels;
using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

namespace Torrid.Tests;

public sealed class FakeSensorSourceForTests : ISensorSource, IHardwareStatusSource
{
    public IReadOnlyList<SensorReading> CurrentReadings { get; private set; } = Array.Empty<SensorReading>();
    public HardwareStatus Status { get; private set; } = HardwareStatus.FullAccess;
    public event EventHandler<IReadOnlyList<SensorReading>>? ReadingsUpdated;
    public event EventHandler? StatusChanged;

    public void Start() { }
    public void Stop() { }

    public void Publish(IReadOnlyList<SensorReading> readings)
    {
        CurrentReadings = readings;
        ReadingsUpdated?.Invoke(this, readings);
    }

    public void SetStatus(HardwareStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class InMemoryConfigStore : IConfigStore
{
    public AppConfig Load() => new();
    public void Save(AppConfig config) { }
}

public class HomeViewModelTests
{
    private const string Cpu5950X = "AMD Ryzen 9 5950X";
    private const string Rtx3090 = "NVIDIA GeForce RTX 3090";

    private static SensorReading Cpu(double? value = 67.6) =>
        new("cpu", "CPU", SensorKind.CpuPackage, null, value, Cpu5950X, "Core (Tctl/Tdie)");

    private static SensorReading Gpu0Core(double? value = 50) =>
        new("gpu0-core", "GPU0 Core", SensorKind.GpuCore, 0, value, Rtx3090, "GPU Core");

    private static SensorReading Gpu0Hot(double? value = 80) =>
        new("gpu0-hotspot", "GPU0 Hot", SensorKind.GpuHotSpot, 0, value, Rtx3090, "GPU Hot Spot");

    private static SensorReading Gpu0Vram(double? value = 82) =>
        new("gpu0-vram", "GPU0 VRAM", SensorKind.GpuVram, 0, value, Rtx3090, "GPU Memory Junction");

    private static SensorReading[] AllGpu0AndCpu() => new[] { Gpu0Vram(), Gpu0Hot(), Gpu0Core(), Cpu() };

    private static (HomeViewModel Vm, FakeSensorSourceForTests Source, ConfigManager Config) Make(AppConfig? config = null)
    {
        var source = new FakeSensorSourceForTests();
        var manager = new ConfigManager(new InMemoryConfigStore(), config ?? new AppConfig(), TimeSpan.Zero);
        return (new HomeViewModel(source, source, manager), source, manager);
    }

    private static SensorCardViewModel Card(HomeViewModel vm, string id) =>
        vm.Groups.SelectMany(g => g.Cards).Single(c => c.Id == id);

    [Fact]
    public void DefaultConfig_ShowsCpuAndGpuHotSpotInTwoGroups()
    {
        var (vm, source, _) = Make();

        source.Publish(AllGpu0AndCpu());

        Assert.Equal(new[] { "CPU", "GPU 0" }, vm.Groups.Select(g => g.Title));
        Assert.Equal(new[] { "cpu" }, vm.Groups[0].Cards.Select(c => c.Id));
        Assert.Equal(new[] { "gpu0-hotspot" }, vm.Groups[1].Cards.Select(c => c.Id));
    }

    [Fact]
    public void GroupSubtitle_IsTheHardwareName()
    {
        var (vm, source, _) = Make();

        source.Publish(AllGpu0AndCpu());

        Assert.Equal(Cpu5950X, vm.Groups[0].Subtitle);
        Assert.Equal(Rtx3090, vm.Groups[1].Subtitle);
    }

    [Fact]
    public void Groups_AreOrderedCpuFirstThenGpusByIndex_AndCardsByKind()
    {
        var config = new AppConfig();
        config.Sensors["gpu0-core"] = new SensorPreference { ShowInWindow = true };
        config.Sensors["gpu1-core"] = new SensorPreference { ShowInWindow = true };
        var (vm, source, _) = Make(config);

        source.Publish(new[]
        {
            new SensorReading("gpu1-core", "GPU1 Core", SensorKind.GpuCore, 1, 40, Rtx3090, "GPU Core"),
            Gpu0Hot(),
            Gpu0Core(),
            Cpu(),
        });

        Assert.Equal(new[] { "CPU", "GPU 0", "GPU 1" }, vm.Groups.Select(g => g.Title));
        Assert.Equal(new[] { "gpu0-core", "gpu0-hotspot" }, vm.Groups[1].Cards.Select(c => c.Id));
    }

    [Fact]
    public void Card_ShowsLabelCaptionRoundedValueAndZone()
    {
        var config = new AppConfig();
        config.Sensors["cpu"] = new SensorPreference { Label = "5950X" };
        var (vm, source, _) = Make(config);

        source.Publish(new[] { Cpu(67.6) });

        var card = Card(vm, "cpu");
        Assert.Equal("5950X", card.Label);
        Assert.Equal("Core (Tctl/Tdie)", card.Caption);
        Assert.Equal("68", card.ValueText);
        Assert.True(card.IsAvailable);
        Assert.Equal(ColorZone.Green, card.Zone);
    }

    [Theory]
    [InlineData(30.0, ColorZone.Blue)]
    [InlineData(84.0, ColorZone.Yellow)]
    [InlineData(90.0, ColorZone.Red)]
    public void Card_ZoneFollowsThresholds(double value, ColorZone expected)
    {
        var (vm, source, _) = Make();

        source.Publish(new[] { Cpu(value) });

        Assert.Equal(expected, Card(vm, "cpu").Zone);
    }

    [Fact]
    public void Card_WithNullValue_IsUnavailable()
    {
        var (vm, source, _) = Make();

        source.Publish(new[] { Cpu(null) });

        var card = Card(vm, "cpu");
        Assert.Equal("--", card.ValueText);
        Assert.False(card.IsAvailable);
        Assert.Equal(0, card.Fill);
    }

    [Fact]
    public void Card_FillIsValueOverYellowMaxPlusFifteenPercent()
    {
        var (vm, source, config) = Make();

        source.Publish(new[] { Cpu(67.6) });

        var yellowMax = config.Config.Thresholds[SensorKind.CpuPackage].YellowMax;
        Assert.Equal(Math.Clamp(67.6 / (yellowMax * 1.15), 0, 1), Card(vm, "cpu").Fill, 6);
    }

    [Fact]
    public void Card_FillIsClampedToOne()
    {
        var (vm, source, _) = Make();

        source.Publish(new[] { Cpu(150) });

        Assert.Equal(1, Card(vm, "cpu").Fill);
    }

    [Fact]
    public void Card_IsReusedAcrossReadings()
    {
        var (vm, source, _) = Make();
        source.Publish(new[] { Cpu(50) });
        var first = Card(vm, "cpu");

        source.Publish(new[] { Cpu(60) });

        Assert.Same(first, Card(vm, "cpu"));
        Assert.Equal("60", first.ValueText);
    }

    [Fact]
    public void Card_RaisesPropertyChangedWhenValueChanges()
    {
        var (vm, source, _) = Make();
        source.Publish(new[] { Cpu(50) });
        var raised = new List<string?>();
        Card(vm, "cpu").PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        source.Publish(new[] { Cpu(60) });

        Assert.Contains(nameof(SensorCardViewModel.ValueText), raised);
        Assert.Contains(nameof(SensorCardViewModel.Fill), raised);
    }

    [Fact]
    public void ConfigUpdate_SwitchingSensorOn_AddsCardWithoutNewReading()
    {
        var (vm, source, config) = Make();
        source.Publish(AllGpu0AndCpu());

        config.Update(c => SensorPreferences.Update(c, "gpu0-vram", p => p.ShowInWindow = true));

        Assert.Equal(new[] { "gpu0-hotspot", "gpu0-vram" }, vm.Groups[1].Cards.Select(c => c.Id));
    }

    [Fact]
    public void ConfigUpdate_Renaming_ChangesLabelWithoutNewReading()
    {
        var (vm, source, config) = Make();
        source.Publish(AllGpu0AndCpu());

        config.Update(c => SensorPreferences.Update(c, "cpu", p => p.Label = "Ryzen"));

        Assert.Equal("Ryzen", Card(vm, "cpu").Label);
    }

    [Fact]
    public void ConfigUpdate_SwitchingLastSensorOff_RemovesTheGroup()
    {
        var (vm, source, config) = Make();
        source.Publish(AllGpu0AndCpu());

        config.Update(c => SensorPreferences.Update(c, "gpu0-hotspot", p => p.ShowInWindow = false));

        Assert.Equal(new[] { "CPU" }, vm.Groups.Select(g => g.Title));
    }

    [Fact]
    public void IsEmpty_TracksWhetherAnythingIsShown()
    {
        var (vm, source, config) = Make();
        source.Publish(new[] { Cpu() });
        Assert.False(vm.IsEmpty);

        config.Update(c => SensorPreferences.Update(c, "cpu", p => p.ShowInWindow = false));

        Assert.True(vm.IsEmpty);
        Assert.Empty(vm.Groups);
        Assert.Contains("Sensors", vm.EmptyMessage);
    }

    [Fact]
    public void EmptyReadings_DoNotThrow_AndExplainThereIsNoData()
    {
        var (vm, _, _) = Make();

        Assert.True(vm.IsEmpty);
        Assert.Empty(vm.Groups);
        Assert.False(string.IsNullOrWhiteSpace(vm.EmptyMessage));
        Assert.DoesNotContain("switched on", vm.EmptyMessage);
    }

    [Fact]
    public void ReadingsDisappearing_ClearsTheGroups()
    {
        var (vm, source, _) = Make();
        source.Publish(AllGpu0AndCpu());

        source.Publish(Array.Empty<SensorReading>());

        Assert.Empty(vm.Groups);
        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public void StatusMessage_IsNullWithFullAccess()
    {
        var (vm, _, _) = Make();

        Assert.Null(vm.StatusMessage);
        Assert.False(vm.HasStatusMessage);
    }

    [Fact]
    public void StatusMessage_WhenPaused_NamesTheProduct()
    {
        var (vm, source, _) = Make();

        source.SetStatus(new HardwareStatus(HardwareAccessLevel.Paused, new AntiCheatMatch("Riot Vanguard", "vgc")));

        Assert.True(vm.HasStatusMessage);
        Assert.StartsWith("Monitoring paused while Riot Vanguard is running", vm.StatusMessage);
    }

    [Fact]
    public void StatusMessage_WhenGpuOnly_NamesTheProduct()
    {
        var (vm, source, _) = Make();

        source.SetStatus(new HardwareStatus(HardwareAccessLevel.GpuOnly, new AntiCheatMatch("Riot Vanguard", "vgc")));

        Assert.StartsWith("CPU temperature is hidden while Riot Vanguard is running", vm.StatusMessage);
    }

    [Fact]
    public void StatusMessage_ForACustomProcess_NamesTheProcess()
    {
        var (vm, source, _) = Make();

        source.SetStatus(new HardwareStatus(HardwareAccessLevel.Paused, new AntiCheatMatch("Custom", "mygame")));

        Assert.Contains("mygame", vm.StatusMessage);
    }

    [Fact]
    public void StatusMessage_ClearsWhenAccessReturns()
    {
        var (vm, source, _) = Make();
        source.SetStatus(new HardwareStatus(HardwareAccessLevel.Paused, new AntiCheatMatch("Riot Vanguard", "vgc")));
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        source.SetStatus(HardwareStatus.FullAccess);

        Assert.Null(vm.StatusMessage);
        Assert.False(vm.HasStatusMessage);
        Assert.Contains(nameof(HomeViewModel.StatusMessage), raised);
        Assert.Contains(nameof(HomeViewModel.HasStatusMessage), raised);
    }
}
