using Torrid.App.ViewModels;
using Torrid.Core.Config;
using Torrid.Core.Models;

namespace Torrid.Tests;

public class SensorsViewModelTests
{
    private const string Cpu5950X = "AMD Ryzen 9 5950X";
    private const string Rtx3090 = "NVIDIA GeForce RTX 3090";

    private static SensorReading Cpu(double? value = 61) =>
        new("cpu", "CPU", SensorKind.CpuPackage, null, value, Cpu5950X, "Core (Tctl/Tdie)");

    private static SensorReading Gpu(int index, SensorKind kind, double? value = 70)
    {
        var (suffix, label, source) = kind switch
        {
            SensorKind.GpuCore => ("core", "Core", "GPU Core"),
            SensorKind.GpuHotSpot => ("hotspot", "Hot", "GPU Hot Spot"),
            _ => ("vram", "VRAM", "GPU Memory Junction"),
        };
        return new SensorReading($"gpu{index}-{suffix}", $"GPU{index} {label}", kind, index, value, Rtx3090, source);
    }

    private static SensorReading[] CpuAndGpu0() => new[]
    {
        Gpu(0, SensorKind.GpuVram), Gpu(0, SensorKind.GpuHotSpot), Cpu(), Gpu(0, SensorKind.GpuCore),
    };

    private static (SensorsViewModel Vm, FakeSensorSourceForTests Source, ConfigManager Config) Make(
        AppConfig? config = null, IReadOnlyList<SensorReading>? readings = null)
    {
        var source = new FakeSensorSourceForTests();
        if (readings is not null) source.Publish(readings);
        var manager = new ConfigManager(new InMemoryConfigStore(), config ?? new AppConfig(), TimeSpan.Zero);
        return (new SensorsViewModel(source, manager), source, manager);
    }

    private static SensorListItemViewModel Item(SensorsViewModel vm, string id) =>
        vm.Groups.SelectMany(g => g.Items).Single(i => i.Id == id);

    [Fact]
    public void Groups_OneGroupPerDevice_ItemsInKindOrder()
    {
        var (vm, _, _) = Make(readings: CpuAndGpu0());

        Assert.Equal(new[] { "CPU", "GPU 0" }, vm.Groups.Select(g => g.Title));
        Assert.Equal(new[] { "cpu" }, vm.Groups[0].Items.Select(i => i.Id));
        Assert.Equal(new[] { "gpu0-core", "gpu0-hotspot", "gpu0-vram" }, vm.Groups[1].Items.Select(i => i.Id));
    }

    [Fact]
    public void Groups_GpusOrderedByIndex()
    {
        var readings = new[] { Gpu(1, SensorKind.GpuHotSpot), Gpu(0, SensorKind.GpuHotSpot), Cpu() };

        var (vm, _, _) = Make(readings: readings);

        Assert.Equal(new[] { "CPU", "GPU 0", "GPU 1" }, vm.Groups.Select(g => g.Title));
    }

    [Fact]
    public void UndetectedButConfiguredSensor_StillGetsAnEditableRow()
    {
        var config = new AppConfig();
        config.Sensors["gpu1-hotspot"] = new SensorPreference { Label = "EVGA Hot" };

        var (vm, _, _) = Make(config, CpuAndGpu0());

        var item = Item(vm, "gpu1-hotspot");
        Assert.Equal("EVGA Hot", item.Label);
        Assert.Equal("GPU1 Hot", item.DefaultLabel);
        Assert.False(item.IsDetected);
        Assert.Contains("Not detected right now", item.Caption);
        Assert.Equal("GPU 1", vm.Groups.Last().Title);
    }

    [Fact]
    public void UnparsableConfigKey_IsIgnored()
    {
        var config = new AppConfig();
        config.Sensors["garbage"] = new SensorPreference { Label = "Nope", ShowInWindow = true };

        var (vm, _, _) = Make(config, CpuAndGpu0());

        Assert.DoesNotContain(vm.Groups.SelectMany(g => g.Items), i => i.Id == "garbage");
        Assert.Equal(4, vm.Groups.SelectMany(g => g.Items).Count());
    }

    [Fact]
    public void NoReadingsAndNoConfig_YieldsNoGroups()
    {
        var (vm, _, _) = Make();

        Assert.Empty(vm.Groups);
    }

    [Fact]
    public void Caption_ForDetectedSensor_ShowsRealSensorAndHardwareNames()
    {
        var (vm, _, _) = Make(readings: CpuAndGpu0());

        var caption = Item(vm, "gpu0-hotspot").Caption;
        Assert.Contains("GPU Hot Spot", caption);
        Assert.Contains(Rtx3090, caption);
        Assert.Contains("Core (Tctl/Tdie)", Item(vm, "cpu").Caption);
        Assert.Contains(Cpu5950X, Item(vm, "cpu").Caption);
    }

    [Fact]
    public void Toggles_ReflectKindDefaults()
    {
        var (vm, _, _) = Make(readings: CpuAndGpu0());

        Assert.True(Item(vm, "cpu").ShowInWindow);
        Assert.True(Item(vm, "gpu0-hotspot").ShowInTray);
        Assert.False(Item(vm, "gpu0-core").ShowInWindow);
        Assert.False(Item(vm, "gpu0-vram").ShowInTray);
        Assert.True(Item(vm, "gpu0-vram").Notify);
    }

    [Fact]
    public void ShowInWindow_NonDefaultStoresOverride_DefaultRemovesIt()
    {
        var (vm, _, config) = Make(readings: CpuAndGpu0());
        var vram = Item(vm, "gpu0-vram");

        vram.ShowInWindow = true;
        Assert.True(config.Config.Sensors["gpu0-vram"].ShowInWindow);

        vram.ShowInWindow = false;
        Assert.False(config.Config.Sensors.ContainsKey("gpu0-vram"));
    }

    [Fact]
    public void ShowInTray_NonDefaultStoresOverride_DefaultRemovesIt()
    {
        var (vm, _, config) = Make(readings: CpuAndGpu0());
        var cpu = Item(vm, "cpu");

        cpu.ShowInTray = false;
        Assert.False(config.Config.Sensors["cpu"].ShowInTray);
        Assert.False(cpu.ShowInTray);

        cpu.ShowInTray = true;
        Assert.False(config.Config.Sensors.ContainsKey("cpu"));
        Assert.True(cpu.ShowInTray);
    }

    [Fact]
    public void Notify_NonDefaultStoresOverride_DefaultRemovesIt()
    {
        var (vm, _, config) = Make(readings: CpuAndGpu0());
        var core = Item(vm, "gpu0-core");

        core.Notify = false;
        Assert.False(config.Config.Sensors["gpu0-core"].Notify);

        core.Notify = true;
        Assert.False(config.Config.Sensors.ContainsKey("gpu0-core"));
    }

    [Fact]
    public void Label_CustomIsStored_DefaultOrWhitespaceClearsIt()
    {
        var (vm, _, config) = Make(readings: CpuAndGpu0());
        var hot = Item(vm, "gpu0-hotspot");

        hot.Label = "  Main GPU  ";
        Assert.Equal("Main GPU", config.Config.Sensors["gpu0-hotspot"].Label);
        Assert.Equal("Main GPU", hot.Label);
        Assert.True(hot.HasCustomLabel);

        hot.Label = "GPU0 Hot";
        Assert.False(config.Config.Sensors.ContainsKey("gpu0-hotspot"));
        Assert.Equal("GPU0 Hot", hot.Label);

        hot.Label = "Renamed";
        hot.Label = "   ";
        Assert.False(config.Config.Sensors.ContainsKey("gpu0-hotspot"));
        Assert.Equal("GPU0 Hot", hot.Label);
        Assert.False(hot.HasCustomLabel);
    }

    [Fact]
    public void Label_ClearedKeepsOtherOverrides()
    {
        var (vm, _, config) = Make(readings: CpuAndGpu0());
        var vram = Item(vm, "gpu0-vram");

        vram.ShowInWindow = true;
        vram.Label = "VRAM hot";
        vram.ResetLabelCommand.Execute(null);

        Assert.Null(config.Config.Sensors["gpu0-vram"].Label);
        Assert.True(config.Config.Sensors["gpu0-vram"].ShowInWindow);
        Assert.Equal("GPU0 VRAM", vram.Label);
    }

    [Fact]
    public void ResetToDefaults_EmptiesSensorPreferences()
    {
        var config = new AppConfig();
        config.Sensors["gpu1-hotspot"] = new SensorPreference { Label = "EVGA Hot" };
        var (vm, _, manager) = Make(config, CpuAndGpu0());
        Item(vm, "cpu").ShowInWindow = false;

        vm.ResetToDefaultsCommand.Execute(null);

        Assert.Empty(manager.Config.Sensors);
        Assert.True(Item(vm, "cpu").ShowInWindow);
        Assert.DoesNotContain(vm.Groups.SelectMany(g => g.Items), i => i.Id == "gpu1-hotspot");
    }

    [Fact]
    public void NewReadingId_AddsRow_AndKeepsExistingInstances()
    {
        var (vm, source, _) = Make(readings: CpuAndGpu0());
        var cpuBefore = Item(vm, "cpu");

        source.Publish(CpuAndGpu0().Append(Gpu(1, SensorKind.GpuHotSpot)).ToList());

        Assert.Equal(new[] { "CPU", "GPU 0", "GPU 1" }, vm.Groups.Select(g => g.Title));
        Assert.Same(cpuBefore, Item(vm, "cpu"));
    }

    [Fact]
    public void PerTickPublish_UpdatesValues_WithoutRecreatingRows()
    {
        var (vm, source, _) = Make(readings: CpuAndGpu0());
        var groupsBefore = vm.Groups.ToList();
        var itemsBefore = vm.Groups.SelectMany(g => g.Items).ToList();

        source.Publish(new[]
        {
            Gpu(0, SensorKind.GpuVram), Gpu(0, SensorKind.GpuHotSpot), Cpu(91.4), Gpu(0, SensorKind.GpuCore),
        });

        Assert.Equal(groupsBefore, vm.Groups.ToList());
        var itemsAfter = vm.Groups.SelectMany(g => g.Items).ToList();
        Assert.Equal(itemsBefore.Count, itemsAfter.Count);
        for (var i = 0; i < itemsBefore.Count; i++) Assert.Same(itemsBefore[i], itemsAfter[i]);
        Assert.Equal("91 °C", Item(vm, "cpu").ValueText);
        Assert.Equal(ColorZone.Red, Item(vm, "cpu").Zone);
    }

    [Fact]
    public void ValueText_ShowsDashesWhenNoValue()
    {
        var (vm, _, _) = Make(readings: new[] { Cpu(null) });

        Assert.Equal("--", Item(vm, "cpu").ValueText);
        Assert.False(Item(vm, "cpu").HasValue);
        Assert.True(Item(vm, "cpu").IsDetected);
    }

    [Fact]
    public void ConfigChangedElsewhere_RefreshesRows()
    {
        var (vm, _, manager) = Make(readings: CpuAndGpu0());

        manager.Update(c => SensorPreferences.Update(c, "gpu0-core", p => p.Label = "Core!"));
        manager.Update(c => SensorPreferences.Update(c, "gpu2-vram", p => p.ShowInTray = true));

        Assert.Equal("Core!", Item(vm, "gpu0-core").Label);
        Assert.True(Item(vm, "gpu2-vram").ShowInTray);
        Assert.Equal("GPU 2", vm.Groups.Last().Title);
    }
}
