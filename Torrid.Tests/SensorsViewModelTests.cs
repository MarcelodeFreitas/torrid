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
    public void Label_UnchangedValue_DoesNotWriteConfig()
    {
        var (vm, _, config) = Make(readings: CpuAndGpu0());
        var hot = Item(vm, "gpu0-hotspot");
        hot.Label = "Main GPU";
        var changes = 0;
        config.Changed += (_, _) => changes++;

        hot.Label = "Main GPU";
        Item(vm, "cpu").Label = "CPU";

        Assert.Equal(0, changes);
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
    public void ResetToDefaults_EmptiesSensorPreferences_WhenNothingIsAdded()
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
    public void ResetToDefaults_KeepsCustomThresholdBands_AndClearsEverythingElse()
    {
        var band = new ThresholdBand { BlueMax = 40, GreenMax = 60, YellowMax = 80 };
        var config = new AppConfig();
        config.Sensors["cpu"] = new SensorPreference { Label = "5950X", ShowInTray = false, Notify = false, Thresholds = band };
        config.Sensors["gpu0-core"] = new SensorPreference { Label = "Core", ShowInWindow = true };
        var (vm, _, manager) = Make(config, CpuAndGpu0());
        var before = manager.Config.Sensors;

        vm.ResetToDefaultsCommand.Execute(null);

        Assert.NotSame(before, manager.Config.Sensors);
        Assert.Equal(new[] { "cpu" }, manager.Config.Sensors.Keys);
        var cpu = manager.Config.Sensors["cpu"];
        Assert.Null(cpu.Label);
        Assert.Null(cpu.ShowInTray);
        Assert.Null(cpu.Notify);
        Assert.Equal(40, cpu.Thresholds!.BlueMax);
        Assert.Equal(60, cpu.Thresholds!.GreenMax);
        Assert.Equal(80, cpu.Thresholds!.YellowMax);
        Assert.Equal("CPU", Item(vm, "cpu").Label);
        Assert.True(Item(vm, "cpu").ShowInTray);
        Assert.Equal("GPU0 Core", Item(vm, "gpu0-core").Label);
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

    private const string Board = "ASUS ROG CROSSHAIR VIII";
    private const string VrmId = "hw:/lpc/nct6798d/0/temperature/4";
    private const string CcdId = "hw:/amdcpu/0/temperature/3";
    private const string NvmeId = "hw:/nvme/0/temperature/0";
    private const string RamId = "hw:/ram/temperature/0";
    private const string GpuExtraId = "hw:/gpu-nvidia/0/temperature/3";
    private const string OtherId = "hw:/battery/0/temperature/0";

    private static AppConfig WithAdded(params (string Id, string Name, string? Hardware, SensorKind Kind)[] sensors)
    {
        var config = new AppConfig
        {
            AddedSensors = sensors.ToDictionary(
                s => s.Id, s => new AddedSensor { Name = s.Name, HardwareName = s.Hardware, Kind = s.Kind }),
        };
        foreach (var s in sensors) config.Sensors[s.Id] = new SensorPreference { ShowInWindow = true };
        return config;
    }

    private static SensorReading AddedReading(string id, string name, SensorKind kind, double? value, string? hardware) =>
        new(id, name, kind, null, value, hardware, value is null ? null : name);

    [Fact]
    public void AddedButUndetectedSensor_HasARow_NotDetectedRightNow_WithTheStoredNameAndKind()
    {
        var config = WithAdded((VrmId, "VRM", Board, SensorKind.Motherboard));

        var (vm, _, _) = Make(config, CpuAndGpu0());

        var vrm = Item(vm, VrmId);
        Assert.Equal("VRM", vrm.Label);
        Assert.Equal(SensorKind.Motherboard, vrm.Identity.Kind);
        Assert.False(vrm.IsDetected);
        Assert.Contains(SensorListItemViewModel.NotDetectedText, vrm.Caption);
        Assert.Equal("--", vrm.ValueText);
        Assert.True(vrm.ShowInWindow);
    }

    [Fact]
    public void AddedSensorWhoseReadingIsTheNotFoundPlaceholder_IsNotDetected()
    {
        // SensorReadingBuilder yields a null value and no source sensor name for an added id it cannot find.
        var config = WithAdded((VrmId, "VRM", Board, SensorKind.Motherboard));
        var readings = CpuAndGpu0().Append(AddedReading(VrmId, "VRM", SensorKind.Motherboard, null, Board)).ToList();

        var (vm, _, _) = Make(config, readings);

        var vrm = Item(vm, VrmId);
        Assert.False(vrm.IsDetected);
        Assert.Contains(SensorListItemViewModel.NotDetectedText, vrm.Caption);
        Assert.Contains(Board, vrm.Caption);
    }

    [Fact]
    public void AddedAndDetectedSensor_ShowsItsValueAndRealNames()
    {
        var config = WithAdded((VrmId, "VRM", Board, SensorKind.Motherboard));
        var readings = CpuAndGpu0().Append(AddedReading(VrmId, "VRM", SensorKind.Motherboard, 52.4, Board)).ToList();

        var (vm, _, _) = Make(config, readings);

        var vrm = Item(vm, VrmId);
        Assert.True(vrm.IsDetected);
        Assert.Equal("52 °C", vrm.ValueText);
        Assert.Equal($"VRM · {Board}", vrm.Caption);
    }

    [Fact]
    public void GarbageAddedSensorKeys_AndUnknownGenericPreferenceKeys_AreIgnored()
    {
        var config = new AppConfig();
        config.AddedSensors["garbage"] = new AddedSensor { Name = "Nope", Kind = SensorKind.Motherboard };
        config.AddedSensors["hw:/a b"] = new AddedSensor { Name = "Spaces", Kind = SensorKind.Motherboard };
        config.AddedSensors["hw:"] = new AddedSensor { Name = "Empty", Kind = SensorKind.Motherboard };
        config.AddedSensors["cpu"] = new AddedSensor { Name = "Not generic", Kind = SensorKind.Motherboard };
        config.Sensors["hw:/not/added"] = new SensorPreference { ShowInWindow = true };

        var (vm, _, _) = Make(config, CpuAndGpu0());

        Assert.Equal(new[] { "cpu", "gpu0-core", "gpu0-hotspot", "gpu0-vram" }, vm.Groups.SelectMany(g => g.Items).Select(i => i.Id));
        Assert.False(Item(vm, "cpu").IsAdded);
    }

    [Fact]
    public void AReadingForARemovedGenericId_DoesNotProduceARow()
    {
        var readings = CpuAndGpu0().Append(AddedReading(VrmId, "VRM", SensorKind.Motherboard, 50, Board)).ToList();

        var (vm, _, _) = Make(readings: readings);

        Assert.DoesNotContain(vm.Groups.SelectMany(g => g.Items), i => i.Id == VrmId);
    }

    [Fact]
    public void AddedSensors_AreGroupedByKind_WithTheirTitlesAndOrder()
    {
        var config = WithAdded(
            (OtherId, "Battery", "Battery", SensorKind.OtherTemp),
            (NvmeId, "Composite", "Samsung SSD 980 PRO 1TB", SensorKind.Storage),
            (RamId, "DIMM #1", "G.Skill", SensorKind.Memory),
            (VrmId, "VRM", Board, SensorKind.Motherboard),
            (CcdId, "CCD1 (Tdie)", Cpu5950X, SensorKind.CpuOther));

        var (vm, _, _) = Make(config, CpuAndGpu0());

        Assert.Equal(new[] { "CPU", "GPU 0", "Motherboard", "Memory", "Storage", "Other" }, vm.Groups.Select(g => g.Title));
        Assert.Equal(new[] { "cpu", CcdId }, vm.Groups[0].Items.Select(i => i.Id));
        Assert.Equal(Board, vm.Groups.Single(g => g.Title == "Motherboard").Subtitle);
        Assert.Equal("Samsung SSD 980 PRO 1TB", vm.Groups.Single(g => g.Title == "Storage").Subtitle);
        Assert.Equal(Cpu5950X, vm.Groups[0].Subtitle);
    }

    [Fact]
    public void AddedGpuSensor_JoinsTheGpuWithTheSameHardwareName_OtherwiseOther()
    {
        var config = WithAdded((GpuExtraId, "GPU Memory Temp", Rtx3090, SensorKind.GpuOther));

        var (vm, source, _) = Make(config, CpuAndGpu0());

        Assert.Equal(new[] { "CPU", "GPU 0" }, vm.Groups.Select(g => g.Title));
        Assert.Contains(vm.Groups[1].Items, i => i.Id == GpuExtraId);

        // Two GPUs with the same name: the sensor can't be tied to one of them.
        source.Publish(CpuAndGpu0().Append(Gpu(1, SensorKind.GpuHotSpot)).ToList());

        Assert.Equal(new[] { "CPU", "GPU 0", "GPU 1", "Other" }, vm.Groups.Select(g => g.Title));
        Assert.Equal(new[] { GpuExtraId }, vm.Groups[^1].Items.Select(i => i.Id));
    }

    [Fact]
    public void RemoveButton_IsOnlyOnAddedRows_AndRemovesTheSensorAndItsPreferences()
    {
        var config = WithAdded((VrmId, "VRM", Board, SensorKind.Motherboard));
        var (vm, _, manager) = Make(config, CpuAndGpu0());
        var before = manager.Config.AddedSensors;

        Assert.All(vm.Groups.SelectMany(g => g.Items).Where(i => i.Id != VrmId), i => Assert.False(i.IsAdded));
        var vrm = Item(vm, VrmId);
        Assert.True(vrm.IsAdded);

        vrm.RemoveCommand.Execute(null);

        Assert.NotSame(before, manager.Config.AddedSensors);
        Assert.Empty(manager.Config.AddedSensors);
        Assert.False(manager.Config.Sensors.ContainsKey(VrmId));
        Assert.DoesNotContain(vm.Groups.SelectMany(g => g.Items), i => i.Id == VrmId);
        Assert.DoesNotContain(vm.Groups, g => g.Title == "Motherboard");
    }

    [Fact]
    public void ResetToDefaults_KeepsAddedSensorsInTheWindow_ButClearsTheirOtherOverrides()
    {
        var config = WithAdded((VrmId, "VRM", Board, SensorKind.Motherboard), (CcdId, "CCD1 (Tdie)", Cpu5950X, SensorKind.CpuOther));
        config.Sensors[VrmId] = new SensorPreference { Label = "Hot VRM", ShowInWindow = false, ShowInTray = true, Notify = false };
        config.AddedSensors["garbage"] = new AddedSensor { Name = "Nope", Kind = SensorKind.Motherboard };
        config.Sensors["cpu"] = new SensorPreference { Label = "5950X", ShowInTray = false };
        var (vm, _, manager) = Make(config, CpuAndGpu0());

        vm.ResetToDefaultsCommand.Execute(null);

        Assert.Equal(new[] { CcdId, VrmId }, manager.Config.Sensors.Keys.OrderBy(k => k, StringComparer.Ordinal));
        var vrm = manager.Config.Sensors[VrmId];
        Assert.True(vrm.ShowInWindow);
        Assert.Null(vrm.Label);
        Assert.Null(vrm.ShowInTray);
        Assert.Null(vrm.Notify);
        Assert.True(manager.Config.Sensors[CcdId].ShowInWindow);
        Assert.Equal("VRM", Item(vm, VrmId).Label);
        Assert.True(Item(vm, VrmId).ShowInWindow);
        Assert.False(Item(vm, VrmId).ShowInTray);
        Assert.True(Item(vm, VrmId).Notify);
        Assert.Equal("CPU", Item(vm, "cpu").Label);
        Assert.True(Item(vm, "cpu").ShowInTray);
    }

    [Fact]
    public void AddSensorPanel_IsAvailableAndClosedByDefault()
    {
        var (vm, _, _) = Make(readings: CpuAndGpu0());

        Assert.NotNull(vm.AddSensor);
        Assert.False(vm.AddSensor.IsOpen);
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

    [Fact]
    public void ValueZone_FollowsTheSensorsOwnBand_OthersKeepTheKindDefault()
    {
        var config = new AppConfig();
        ThresholdResolver.SetCustom(config, "gpu0-core", new ThresholdBand { BlueMax = 10, GreenMax = 20, YellowMax = 30 });
        var readings = new[] { Gpu(0, SensorKind.GpuCore, 60), Gpu(0, SensorKind.GpuHotSpot, 60), Cpu(60) };

        var (vm, _, _) = Make(config, readings);

        Assert.Equal(ColorZone.Red, Item(vm, "gpu0-core").Zone);
        Assert.Equal(ColorZone.Green, Item(vm, "gpu0-hotspot").Zone);
        Assert.Equal(ColorZone.Green, Item(vm, "cpu").Zone);
    }

    [Fact]
    public void ValueZone_TwoSensorsOfTheSameKind_KeepIndependentBands()
    {
        var config = new AppConfig();
        ThresholdResolver.SetCustom(config, "gpu0-core", new ThresholdBand { BlueMax = 10, GreenMax = 20, YellowMax = 30 });
        var readings = new[] { Gpu(0, SensorKind.GpuCore, 60), Gpu(1, SensorKind.GpuCore, 60) };

        var (vm, _, _) = Make(config, readings);

        Assert.Equal(SensorKind.GpuCore, Item(vm, "gpu0-core").Identity.Kind);
        Assert.Equal(SensorKind.GpuCore, Item(vm, "gpu1-core").Identity.Kind);
        Assert.Equal(ColorZone.Red, Item(vm, "gpu0-core").Zone);    // its own 10/20/30 band
        Assert.Equal(ColorZone.Green, Item(vm, "gpu1-core").Zone);  // the GPU core default 40/75/85
    }

    [Fact]
    public void ValueZone_UpdatesWhenASensorBandIsSetOrCleared()
    {
        var (vm, source, manager) = Make(readings: new[] { Cpu(60) });
        Assert.Equal(ColorZone.Green, Item(vm, "cpu").Zone);

        manager.Update(c => ThresholdResolver.SetCustom(c, "cpu", new ThresholdBand { BlueMax = 10, GreenMax = 20, YellowMax = 30 }));
        source.Publish(new[] { Cpu(60) });
        Assert.Equal(ColorZone.Red, Item(vm, "cpu").Zone);

        manager.Update(c => ThresholdResolver.ClearCustom(c, "cpu"));
        source.Publish(new[] { Cpu(60) });
        Assert.Equal(ColorZone.Green, Item(vm, "cpu").Zone);
    }

    [Fact]
    public void LimitsLink_CallsBackWithTheSensorIdExactlyOnce()
    {
        var calls = new List<string>();
        var source = new FakeSensorSourceForTests();
        source.Publish(CpuAndGpu0());
        var manager = new ConfigManager(new InMemoryConfigStore(), new AppConfig(), TimeSpan.Zero);
        var vm = new SensorsViewModel(source, manager, showLimits: calls.Add);

        Item(vm, "gpu0-core").LimitsCommand.Execute(null);

        Assert.Equal(new[] { "gpu0-core" }, calls);
    }
}
