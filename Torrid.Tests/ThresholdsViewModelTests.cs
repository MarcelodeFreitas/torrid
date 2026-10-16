using Torrid.App.ViewModels;
using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

namespace Torrid.Tests;

public class ThresholdsViewModelTests
{
    private const string Cpu5950X = "AMD Ryzen 9 5950X";
    private const string Rtx3090 = "NVIDIA GeForce RTX 3090";
    private const string Board = "ASUS ROG CROSSHAIR VIII";
    private const string VrmId = "hw:/lpc/nct6798d/0/temperature/4";
    private const string NotDetected = "Not detected right now";

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

    private static (ThresholdsViewModel Vm, FakeSensorSourceForTests Source, ConfigManager Config) Make(
        AppConfig? config = null, IReadOnlyList<SensorReading>? readings = null, ISensorCatalogSource? catalog = null)
    {
        var source = new FakeSensorSourceForTests();
        if (readings is not null) source.Publish(readings);
        var manager = new ConfigManager(new InMemoryConfigStore(), config ?? new AppConfig(), TimeSpan.Zero);
        return (new ThresholdsViewModel(source, manager, catalog), source, manager);
    }

    private static SensorThresholdViewModel Item(ThresholdsViewModel vm, string id) =>
        vm.Groups.SelectMany(g => g.Items).Single(i => i.Id == id);

    private static ThresholdRowViewModel Row(ThresholdsViewModel vm, SensorKind kind) => vm.DefaultRows.Single(r => r.Kind == kind);

    private static (double Blue, double Green, double Yellow) Band(ThresholdBand band) => (band.BlueMax, band.GreenMax, band.YellowMax);

    private static (double Blue, double Green, double Yellow) Limits(SensorThresholdViewModel item) => (item.BlueMax, item.GreenMax, item.YellowMax);

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

    // ---- Defaults section (the ten kind rows, unchanged behaviour) ----

    [Fact]
    public void DefaultRows_OnePerKind_SeededFromConfig()
    {
        var config = new AppConfig();
        config.Thresholds[SensorKind.GpuVram] = new ThresholdBand { BlueMax = 55, GreenMax = 88, YellowMax = 100 };

        var (vm, _, _) = Make(config);

        Assert.Equal(Enum.GetValues<SensorKind>(), vm.DefaultRows.Select(r => r.Kind));
        Assert.Equal((55d, 88d, 100d), (Row(vm, SensorKind.GpuVram).BlueMax, Row(vm, SensorKind.GpuVram).GreenMax, Row(vm, SensorKind.GpuVram).YellowMax));
        Assert.Equal("CPU temperature", Row(vm, SensorKind.CpuPackage).Title);
        Assert.All(vm.DefaultRows, r => Assert.Null(r.Error));
    }

    [Theory]
    [InlineData(SensorKind.CpuPackage, "CPU temperature", 40, 70, 85)]
    [InlineData(SensorKind.GpuCore, "GPU core", 40, 75, 85)]
    [InlineData(SensorKind.GpuHotSpot, "GPU hot spot", 50, 85, 95)]
    [InlineData(SensorKind.GpuVram, "GPU memory junction (VRAM)", 50, 85, 95)]
    [InlineData(SensorKind.CpuOther, "Other CPU sensors", 40, 70, 85)]
    [InlineData(SensorKind.GpuOther, "Other GPU sensors", 50, 85, 95)]
    [InlineData(SensorKind.Motherboard, "Motherboard / VRM", 45, 70, 90)]
    [InlineData(SensorKind.Storage, "Storage", 40, 55, 70)]
    [InlineData(SensorKind.Memory, "Memory", 40, 60, 80)]
    [InlineData(SensorKind.OtherTemp, "Other sensors", 45, 70, 85)]
    public void DefaultRows_ExistForEveryKind_WithTheirTitleAndDefaultBand(
        SensorKind kind, string title, double blue, double green, double yellow)
    {
        var (vm, _, _) = Make();

        var row = Row(vm, kind);
        Assert.Equal(title, row.Title);
        Assert.Equal((blue, green, yellow), (row.BlueMax, row.GreenMax, row.YellowMax));
        Assert.Null(row.Error);
        Assert.Equal(10, vm.DefaultRows.Count);
    }

    [Fact]
    public void DefaultRows_InvalidBandInConfig_IsShownWithAnError_AndLeftAlone()
    {
        var config = new AppConfig();
        config.Thresholds[SensorKind.GpuHotSpot] = new ThresholdBand { BlueMax = 50, GreenMax = 99, YellowMax = 95 };

        var (vm, _, manager) = Make(config);

        Assert.Equal(ThresholdRowViewModel.OrderingError, Row(vm, SensorKind.GpuHotSpot).Error);
        Assert.Equal(99, manager.Config.Thresholds[SensorKind.GpuHotSpot].GreenMax);
    }

    [Fact]
    public void DefaultRows_ValidEdit_UpdatesTheKindDefault()
    {
        var (vm, _, config) = Make();
        var cpu = Row(vm, SensorKind.CpuPackage);

        cpu.GreenMax = 72;
        cpu.YellowMax = 90;
        cpu.BlueMax = 35;

        Assert.Equal((35d, 72d, 90d), Band(config.Config.Thresholds[SensorKind.CpuPackage]));
        Assert.Null(cpu.Error);
    }

    [Fact]
    public void DefaultRows_ResetRestoresTheKindDefault()
    {
        var config = new AppConfig();
        config.Thresholds[SensorKind.GpuCore] = new ThresholdBand { BlueMax = 10, GreenMax = 20, YellowMax = 30 };
        var (vm, _, manager) = Make(config);
        var core = Row(vm, SensorKind.GpuCore);
        core.BlueMax = 25; // invalid (>= GreenMax 20) so an error is pending

        core.ResetCommand.Execute(null);

        var expected = AppConfig.DefaultThresholds()[SensorKind.GpuCore];
        Assert.Equal(Band(expected), Band(manager.Config.Thresholds[SensorKind.GpuCore]));
        Assert.Equal(Band(expected), (core.BlueMax, core.GreenMax, core.YellowMax));
        Assert.Null(core.Error);
    }

    [Fact]
    public void DefaultRows_PreviewWidths_AreProportionalToTheBand()
    {
        var (vm, _, _) = Make();
        var cpu = Row(vm, SensorKind.CpuPackage); // 40 / 70 / 85

        Assert.True(cpu.BlueWidth.IsStar);
        Assert.Equal(40, cpu.BlueWidth.Value);
        Assert.Equal(30, cpu.GreenWidth.Value);
        Assert.Equal(15, cpu.YellowWidth.Value);
        Assert.Equal(85 * 0.25, cpu.RedWidth.Value);
    }

    // ---- Sensor rows: parity, placement and the row set ----

    [Fact]
    public void Groups_SameDeviceTitlesAndOrderAndMembersAsTheSensorsPage()
    {
        var config = WithAdded(
            ("hw:/battery/0/temperature/0", "Battery", "Battery", SensorKind.OtherTemp),
            ("hw:/nvme/0/temperature/0", "Composite", "Samsung SSD 980 PRO 1TB", SensorKind.Storage),
            ("hw:/ram/temperature/0", "DIMM #1", "G.Skill", SensorKind.Memory),
            (VrmId, "VRM", Board, SensorKind.Motherboard));
        var source = new FakeSensorSourceForTests();
        var readings = CpuAndGpu0().Append(Gpu(1, SensorKind.GpuHotSpot)).ToList();
        source.Publish(readings);
        var manager = new ConfigManager(new InMemoryConfigStore(), config, TimeSpan.Zero);
        var sensors = new SensorsViewModel(source, manager);
        var thresholds = new ThresholdsViewModel(source, manager);

        Assert.Equal(sensors.Groups.Select(g => g.Title), thresholds.Groups.Select(g => g.Title));
        foreach (var (sensorGroup, thresholdGroup) in sensors.Groups.Zip(thresholds.Groups))
        {
            Assert.Equal(sensorGroup.Items.Select(i => i.Id), thresholdGroup.Items.Select(i => i.Id));
            Assert.Equal(sensorGroup.Subtitle, thresholdGroup.Subtitle);
        }

        Assert.DoesNotContain(thresholds.Groups, g => g.IsNotShown);
    }

    [Fact]
    public void NotShownSensors_GoToTheLastGroup_CollapsedByDefault_AndLeaveWhenOneSwitchComesBackOn()
    {
        var (vm, _, manager) = Make(readings: CpuAndGpu0());
        Assert.Equal(new[] { "CPU", "GPU 0" }, vm.Groups.Select(g => g.Title));

        manager.Update(c => SensorPreferences.Update(c, "cpu", p =>
        {
            p.ShowInWindow = false;
            p.ShowInTray = false;
            p.Notify = false;
        }));

        var notShown = vm.Groups.Last();
        Assert.True(notShown.IsNotShown);
        Assert.Equal("Not shown", notShown.Title);
        Assert.False(notShown.IsExpanded);
        Assert.Equal(new[] { "cpu" }, notShown.Items.Select(i => i.Id));
        Assert.Equal("CPU", Item(vm, "cpu").DeviceTitle);
        Assert.DoesNotContain(vm.Groups.Where(g => !g.IsNotShown), g => g.Items.Any(i => i.Id == "cpu"));

        manager.Update(c => SensorPreferences.Update(c, "cpu", p => p.Notify = true));

        Assert.Equal(new[] { "CPU", "GPU 0" }, vm.Groups.Select(g => g.Title));
        Assert.Equal(new[] { "cpu" }, vm.Groups[0].Items.Select(i => i.Id));
    }

    [Fact]
    public void NewSensorId_AddsARow_WithoutTouchingTheOthers()
    {
        var (vm, source, _) = Make(readings: CpuAndGpu0());
        var cpuBefore = Item(vm, "cpu");
        var groupsBefore = vm.Groups.ToList();

        source.Publish(CpuAndGpu0().Append(Gpu(1, SensorKind.GpuHotSpot)).ToList());

        Assert.Equal(new[] { "CPU", "GPU 0", "GPU 1" }, vm.Groups.Select(g => g.Title));
        Assert.Same(cpuBefore, Item(vm, "cpu"));
        Assert.Same(groupsBefore[0], vm.Groups[0]);
        Assert.NotNull(Item(vm, "gpu1-hotspot"));
    }

    [Fact]
    public void UnreadableSensor_HasARow_WithDashesAndNotDetectedCaption()
    {
        var config = new AppConfig();
        config.Sensors["gpu1-hotspot"] = new SensorPreference { Label = "EVGA Hot" };

        var (vm, _, _) = Make(config, CpuAndGpu0());

        var item = Item(vm, "gpu1-hotspot");
        Assert.Equal("EVGA Hot", item.Label);
        Assert.Equal(NotDetected, item.Caption);
        Assert.Equal("--", item.ValueText);
        Assert.Equal(ColorZone.Blue, item.Zone);
        Assert.Equal("GPU 1", vm.Groups.Last().Title);
    }

    [Fact]
    public void DetectedSensorWithoutAValue_ShowsDashes()
    {
        var (vm, _, _) = Make(readings: new[] { Cpu(null) });

        Assert.Equal("--", Item(vm, "cpu").ValueText);
        Assert.NotEqual(NotDetected, Item(vm, "cpu").Caption);
    }

    [Fact]
    public void GarbageKeys_AreIgnored()
    {
        var config = new AppConfig();
        config.AddedSensors["garbage"] = new AddedSensor { Name = "Nope", Kind = SensorKind.Motherboard };
        config.AddedSensors["hw:/a b"] = new AddedSensor { Name = "Spaces", Kind = SensorKind.Motherboard };
        config.AddedSensors["hw:"] = new AddedSensor { Name = "Empty", Kind = SensorKind.Motherboard };
        config.Sensors["garbage"] = new SensorPreference { Label = "Nope", ShowInWindow = true };
        config.Sensors["hw:/not/added"] = new SensorPreference { ShowInWindow = true };

        var (vm, _, _) = Make(config, CpuAndGpu0());

        var ids = vm.Groups.SelectMany(g => g.Items).Select(i => i.Id).ToList();
        Assert.Equal(new[] { "cpu", "gpu0-core", "gpu0-hotspot", "gpu0-vram" }, ids.OrderBy(i => i, StringComparer.Ordinal));
    }

    [Fact]
    public void AddedSensor_HasARow_WithItsStoredNameAndKind()
    {
        var config = WithAdded((VrmId, "VRM", Board, SensorKind.Motherboard));

        var (vm, _, _) = Make(config, CpuAndGpu0());

        var vrm = Item(vm, VrmId);
        Assert.Equal("VRM", vrm.Label);
        Assert.Equal(SensorKind.Motherboard, vrm.Identity.Kind);
        Assert.Equal("Motherboard", vrm.DeviceTitle);
        Assert.Equal(NotDetected, vrm.Caption);
    }

    [Fact]
    public void ConfigChangedElsewhere_RefreshesLabelsAndPlacement()
    {
        var (vm, _, manager) = Make(readings: CpuAndGpu0());

        manager.Update(c => SensorPreferences.Update(c, "gpu0-core", p => p.Label = "Core!"));

        Assert.Equal("Core!", Item(vm, "gpu0-core").Label);
    }

    // ---- Per-sensor limits: editing, Default / Custom, reset ----

    [Fact]
    public void ValidTrio_CreatesACustomBand_ForOnlyThatSensor_AndFlipsIsCustom()
    {
        var (vm, _, manager) = Make(readings: CpuAndGpu0());
        var core = Item(vm, "gpu0-core");
        Assert.False(core.IsCustom);
        Assert.Equal("Default", core.BadgeText);

        core.BlueMax = 10;
        core.GreenMax = 20;
        core.YellowMax = 30;

        Assert.Equal((10d, 20d, 30d), Band(manager.Config.Sensors["gpu0-core"].Thresholds!));
        Assert.True(core.IsCustom);
        Assert.Equal("Custom", core.BadgeText);
        Assert.False(Item(vm, "gpu0-hotspot").IsCustom);
        Assert.False(Item(vm, "cpu").IsCustom);
        Assert.Equal(Band(AppConfig.DefaultThresholds()[SensorKind.GpuCore]), Band(manager.Config.Thresholds[SensorKind.GpuCore]));
    }

    [Fact]
    public void InvalidTrio_SetsError_AndLeavesConfigUntouched()
    {
        var (vm, _, manager) = Make(readings: CpuAndGpu0());
        var core = Item(vm, "gpu0-core");
        var changes = 0;
        manager.Changed += (_, _) => changes++;

        core.GreenMax = 90; // above YellowMax 85

        Assert.Equal(ThresholdRowViewModel.OrderingError, core.Error);
        Assert.True(core.HasError);
        Assert.False(manager.Config.Sensors.ContainsKey("gpu0-core"));
        Assert.Equal(0, changes);
        Assert.False(core.IsCustom);

        core.YellowMax = 151;
        Assert.Equal(ThresholdRowViewModel.RangeError, core.Error);
        Assert.Equal(0, changes);

        core.GreenMax = 80;
        core.YellowMax = 85;
        Assert.Null(core.Error);
        Assert.Equal((40d, 80d, 85d), Band(manager.Config.Sensors["gpu0-core"].Thresholds!));
    }

    [Fact]
    public void TypingAnInvalidValueThenTheCurrentOne_WritesNothing()
    {
        var (vm, _, manager) = Make(readings: CpuAndGpu0());
        var core = Item(vm, "gpu0-core");
        var changes = 0;
        manager.Changed += (_, _) => changes++;

        core.GreenMax = 90;
        core.GreenMax = 75; // back to the default value

        Assert.Null(core.Error);
        Assert.False(core.IsCustom);
        Assert.Equal(0, changes);
        Assert.False(manager.Config.Sensors.ContainsKey("gpu0-core"));
    }

    [Fact]
    public void Reset_ClearsTheCustomBand_AndBoxesFollowTheKindDefault()
    {
        var (vm, _, manager) = Make(readings: CpuAndGpu0());
        var core = Item(vm, "gpu0-core");
        core.BlueMax = 10;
        core.GreenMax = 20;
        core.YellowMax = 30;
        core.GreenMax = 90; // a pending invalid edit must not survive the reset

        core.ResetCommand.Execute(null);

        var expected = AppConfig.DefaultThresholds()[SensorKind.GpuCore];
        Assert.False(core.IsCustom);
        Assert.Equal("Default", core.BadgeText);
        Assert.False(manager.Config.Sensors.ContainsKey("gpu0-core"));
        Assert.Equal((expected.BlueMax, expected.GreenMax, expected.YellowMax), Limits(core));
        Assert.Null(core.Error);
    }

    [Fact]
    public void KindDefaultEdit_ChangesUnEditedSensors_ButNotCustomisedOnes()
    {
        var (vm, _, manager) = Make(readings: CpuAndGpu0().Append(Gpu(1, SensorKind.GpuCore)).ToList());
        var custom = Item(vm, "gpu0-core");
        custom.BlueMax = 10;
        custom.GreenMax = 20;
        custom.YellowMax = 30;

        Row(vm, SensorKind.GpuCore).GreenMax = 80; // 40 / 80 / 85 is valid

        Assert.Equal((40d, 80d, 85d), Limits(Item(vm, "gpu1-core")));
        Assert.Equal((10d, 20d, 30d), Limits(custom));
        Assert.Equal((40d, 80d, 85d), Band(manager.Config.Thresholds[SensorKind.GpuCore]));
    }

    [Fact]
    public void ResetAfterTheKindDefaultChanged_FollowsTheNewDefault()
    {
        var (vm, _, _) = Make(readings: CpuAndGpu0());
        var core = Item(vm, "gpu0-core");
        core.BlueMax = 10;
        core.GreenMax = 20;
        core.YellowMax = 30;

        Row(vm, SensorKind.GpuCore).GreenMax = 80;
        core.ResetCommand.Execute(null);

        Assert.Equal((40d, 80d, 85d), Limits(core));
    }

    [Fact]
    public void TwoSensorsOfTheSameKind_KeepIndependentBands_AndZones()
    {
        var (vm, _, _) = Make(readings: new[] { Gpu(0, SensorKind.GpuCore, 60), Gpu(1, SensorKind.GpuCore, 60) });
        var custom = Item(vm, "gpu0-core");
        custom.BlueMax = 10;
        custom.GreenMax = 20;
        custom.YellowMax = 30;

        Assert.Equal((10d, 20d, 30d), Limits(custom));
        Assert.Equal((40d, 75d, 85d), Limits(Item(vm, "gpu1-core")));
        Assert.Equal(ColorZone.Red, custom.Zone);
        Assert.Equal(ColorZone.Green, Item(vm, "gpu1-core").Zone);
        Assert.False(Item(vm, "gpu1-core").IsCustom);
    }

    [Fact]
    public void PendingInvalidEdit_IsNotOverwrittenByAConfigRefresh()
    {
        var (vm, _, manager) = Make(readings: CpuAndGpu0());
        var core = Item(vm, "gpu0-core");
        core.GreenMax = 90; // invalid, so it stays in the box with an error

        manager.Update(c => c.Thresholds[SensorKind.GpuCore] = new ThresholdBand { BlueMax = 45, GreenMax = 80, YellowMax = 90 });

        Assert.Equal(90d, core.GreenMax);
        Assert.Equal(ThresholdRowViewModel.OrderingError, core.Error);
    }

    [Fact]
    public void DefaultSensor_FollowsTheKindDefault_OnceAnInvalidLoadedDefaultIsFixed()
    {
        var config = new AppConfig();
        config.Thresholds[SensorKind.GpuCore] = new ThresholdBand { BlueMax = 50, GreenMax = 40, YellowMax = 30 };
        var (vm, _, manager) = Make(config, CpuAndGpu0());
        var core = Item(vm, "gpu0-core");
        Assert.Equal(ThresholdRowViewModel.OrderingError, core.Error);

        manager.Update(c => c.Thresholds[SensorKind.GpuCore] = new ThresholdBand { BlueMax = 40, GreenMax = 75, YellowMax = 85 });

        Assert.Null(core.Error);
        Assert.Equal((40d, 75d, 85d), Limits(core));
    }

    [Fact]
    public void ValueZone_FollowsTheSensorsOwnBand_AndUpdatesWhenTheBandChanges()
    {
        var (vm, _, _) = Make(readings: new[] { Gpu(0, SensorKind.GpuCore, 60) });
        var core = Item(vm, "gpu0-core");
        Assert.Equal(ColorZone.Green, core.Zone);

        core.BlueMax = 10;
        core.GreenMax = 20;
        core.YellowMax = 30;
        Assert.Equal(ColorZone.Red, core.Zone);

        core.ResetCommand.Execute(null);
        Assert.Equal(ColorZone.Green, core.Zone);
    }

    [Fact]
    public void PreviewWidths_FollowTheSensorsBand()
    {
        var (vm, _, _) = Make(readings: CpuAndGpu0());
        var cpu = Item(vm, "cpu"); // 40 / 70 / 85

        Assert.True(cpu.BlueWidth.IsStar);
        Assert.Equal(40, cpu.BlueWidth.Value);
        Assert.Equal(30, cpu.GreenWidth.Value);
        Assert.Equal(15, cpu.YellowWidth.Value);
        Assert.Equal(85 * 0.25, cpu.RedWidth.Value);

        cpu.YellowMax = 100;
        Assert.Equal(30, cpu.YellowWidth.Value);
        Assert.Equal(25, cpu.RedWidth.Value);
    }

    // ---- Live values and the row set over time ----

    private static IReadOnlyList<SensorReading> SixtySensors(double value) =>
        Enumerable.Range(0, 20).SelectMany(index => new[]
        {
            Gpu(index, SensorKind.GpuCore, value),
            Gpu(index, SensorKind.GpuHotSpot, value),
            Gpu(index, SensorKind.GpuVram, value),
        }).ToList();

    [Fact]
    public void SixtySensors_PerTickUpdates_CreateNoNewRowInstances_AndRaiseNoCollectionChanges()
    {
        var (vm, source, _) = Make(readings: SixtySensors(50));
        var groupsBefore = vm.Groups.ToList();
        var itemsBefore = vm.Groups.SelectMany(g => g.Items).ToList();
        Assert.Equal(60, itemsBefore.Count);

        var collectionChanges = 0;
        vm.Groups.CollectionChanged += (_, _) => collectionChanges++;
        foreach (var group in vm.Groups) group.Items.CollectionChanged += (_, _) => collectionChanges++;

        source.Publish(SixtySensors(91));
        source.Publish(SixtySensors(20));

        Assert.Equal(0, collectionChanges);
        Assert.Equal(groupsBefore, vm.Groups.ToList());
        var itemsAfter = vm.Groups.SelectMany(g => g.Items).ToList();
        Assert.Equal(itemsBefore.Count, itemsAfter.Count);
        for (var i = 0; i < itemsBefore.Count; i++) Assert.Same(itemsBefore[i], itemsAfter[i]);
        Assert.Equal("20 °C", Item(vm, "gpu5-core").ValueText);
    }

    [Fact]
    public void SixtySensors_SearchAndFocus_CreateNoNewRowInstances()
    {
        var (vm, _, _) = Make(readings: SixtySensors(50));
        var itemsBefore = vm.Groups.SelectMany(g => g.Items).ToList();

        vm.SearchText = "GPU 7";
        vm.Focus("gpu15-hotspot");
        vm.SearchText = "";

        var itemsAfter = vm.Groups.SelectMany(g => g.Items).ToList();
        Assert.Equal(itemsBefore.Count, itemsAfter.Count);
        for (var i = 0; i < itemsBefore.Count; i++) Assert.Same(itemsBefore[i], itemsAfter[i]);
    }

    // ---- Search ----

    [Fact]
    public void SearchText_FiltersVisibility_WithoutRecreatingInstances_AndClears()
    {
        var (vm, _, _) = Make(readings: CpuAndGpu0());
        var all = vm.Groups.SelectMany(g => g.Items).ToList();

        vm.SearchText = "gpu 0";
        Assert.False(Item(vm, "cpu").IsVisible);
        Assert.True(Item(vm, "gpu0-core").IsVisible);
        Assert.False(vm.Groups.Single(g => g.Key == "cpu").IsVisible);

        vm.SearchText = "HOT";
        Assert.Equal(new[] { "gpu0-hotspot" }, all.Where(i => i.IsVisible).Select(i => i.Id));

        vm.SearchText = "";
        Assert.All(all, i => Assert.True(i.IsVisible));
        Assert.True(vm.Groups.All(g => g.IsVisible));
        Assert.Equal(all, vm.Groups.SelectMany(g => g.Items).ToList());
    }

    [Fact]
    public void SearchText_ExpandsACollapsedGroupWithAMatch()
    {
        var (vm, _, manager) = Make(readings: CpuAndGpu0());
        manager.Update(c => SensorPreferences.Update(c, "cpu", p => { p.ShowInWindow = false; p.ShowInTray = false; p.Notify = false; }));
        var notShown = vm.Groups.Last();
        Assert.False(notShown.IsExpanded);

        vm.SearchText = "ryzen";

        Assert.True(notShown.IsExpanded);
        Assert.True(Item(vm, "cpu").IsVisible);
    }

    [Fact]
    public void SearchText_IsCaseInsensitiveAndTrimmed()
    {
        var (vm, _, _) = Make(readings: CpuAndGpu0());

        vm.SearchText = "  cpu  ";

        Assert.True(Item(vm, "cpu").IsVisible);
        Assert.False(Item(vm, "gpu0-core").IsVisible);
    }

    // ---- View mode and focus ----

    [Fact]
    public void IsListView_WritesTheConfigFlag_AndLoadsIt()
    {
        var (vm, _, manager) = Make();
        Assert.False(vm.IsListView);

        vm.IsListView = true;

        Assert.True(manager.Config.ThresholdsListView);
        var (reloaded, _, _) = Make(new AppConfig { ThresholdsListView = true });
        Assert.True(reloaded.IsListView);
    }

    [Fact]
    public void Focus_ExpandsTheGroupContainingTheSensor_AndRaisesTheFocusRequest()
    {
        var (vm, _, _) = Make(readings: SixtySensors(50));
        var gpu15 = vm.Groups.Single(g => g.Key == "gpu15");
        var gpu3 = vm.Groups.Single(g => g.Key == "gpu3");
        gpu15.IsExpanded = false;
        gpu3.IsExpanded = false;
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Focus("gpu15-hotspot");

        Assert.True(gpu15.IsExpanded);
        Assert.False(gpu3.IsExpanded);
        Assert.Equal("gpu15-hotspot", vm.FocusedSensorId);
        Assert.Contains(nameof(ThresholdsViewModel.FocusedSensorId), raised);
    }

    [Fact]
    public void Focus_RaisesAgainForTheSameSensor_AndKeepsTheLatestTarget()
    {
        var (vm, _, _) = Make(readings: CpuAndGpu0());
        vm.Focus("cpu");
        var raised = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ThresholdsViewModel.FocusedSensorId)) raised++; };

        vm.Focus("gpu0-core");
        vm.Focus("gpu0-core");

        Assert.Equal("gpu0-core", vm.FocusedSensorId);
        Assert.Equal(2, raised);
    }

    [Fact]
    public void Focus_OnANotShownSensor_ExpandsTheNotShownGroup()
    {
        var (vm, _, manager) = Make(readings: CpuAndGpu0());
        manager.Update(c => SensorPreferences.Update(c, "cpu", p => { p.ShowInWindow = false; p.ShowInTray = false; p.Notify = false; }));

        vm.Focus("cpu");

        Assert.True(vm.Groups.Last().IsExpanded);
        Assert.Equal("cpu", vm.FocusedSensorId);
    }

    [Fact]
    public void Focus_ClearsASearchThatWouldHideTheSensor()
    {
        var (vm, _, _) = Make(readings: CpuAndGpu0());
        vm.SearchText = "hot";

        vm.Focus("cpu");

        Assert.True(Item(vm, "cpu").IsVisible);
        Assert.Equal("", vm.SearchText);
    }

    [Fact]
    public void Focus_UnknownOrGarbageIdChangesNothing()
    {
        var (vm, _, _) = Make(readings: CpuAndGpu0());
        var raised = 0;
        vm.PropertyChanged += (_, _) => raised++;

        vm.Focus("garbage");
        vm.Focus("hw:/not/added");

        Assert.Null(vm.FocusedSensorId);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void TakeFocusRequest_ReturnsTheRequestOnce_SoAPageOpenedLaterDoesNotRefocus()
    {
        var (vm, _, _) = Make(readings: CpuAndGpu0());
        vm.Focus("gpu0-core");

        Assert.Equal("gpu0-core", vm.TakeFocusRequest());
        Assert.Null(vm.TakeFocusRequest());
    }

    // ---- Review carry-overs: Reset availability, instance placement, search ----

    [Fact]
    public void Reset_OnADefaultSensorWithNoPendingEdit_IsNotExecutable_AndWritesNothing()
    {
        var (vm, _, manager) = Make(readings: CpuAndGpu0());
        var core = Item(vm, "gpu0-core");
        var writes = 0;
        manager.Changed += (_, _) => writes++;

        Assert.False(core.ResetCommand.CanExecute(null));
        core.ResetCommand.Execute(null);

        Assert.Equal(0, writes);
        Assert.False(manager.Config.Sensors.ContainsKey("gpu0-core"));
    }

    [Fact]
    public void Reset_BecomesExecutable_WhenTheSensorIsCustom_AndNotifiesTheCommand()
    {
        var (vm, _, _) = Make(readings: CpuAndGpu0());
        var core = Item(vm, "gpu0-core");
        var canExecuteChanged = 0;
        core.ResetCommand.CanExecuteChanged += (_, _) => canExecuteChanged++;

        core.GreenMax = 80;

        Assert.True(core.ResetCommand.CanExecute(null));
        Assert.True(canExecuteChanged > 0);
    }

    [Fact]
    public void Reset_OnADefaultSensorWithAPendingInvalidEdit_IsExecutable_AndClearsTheEdit()
    {
        var (vm, _, manager) = Make(readings: CpuAndGpu0());
        var core = Item(vm, "gpu0-core");
        core.YellowMax = 10;
        Assert.NotNull(core.Error);

        Assert.True(core.ResetCommand.CanExecute(null));
        core.ResetCommand.Execute(null);

        Assert.Null(core.Error);
        Assert.Equal((40d, 75d, 85d), Limits(core));
        Assert.False(ThresholdResolver.HasCustom(manager.Config, "gpu0-core"));
    }

    [Fact]
    public void Reset_OnADefaultSensorWithAPendingInvalidEdit_WritesNothingToTheConfig()
    {
        var (vm, _, manager) = Make(readings: CpuAndGpu0());
        var core = Item(vm, "gpu0-core");
        core.YellowMax = 10;
        Assert.NotNull(core.Error);
        var changes = 0;
        manager.Changed += (_, _) => changes++;

        core.ResetCommand.Execute(null);

        Assert.Equal(0, changes);
        Assert.Null(core.Error);
        Assert.Equal((40d, 75d, 85d), Limits(core));
    }

    [Fact]
    public void MovingToNotShownAndBack_KeepsTheSameInstance_AndAPendingInvalidEdit()
    {
        var (vm, _, manager) = Make(readings: CpuAndGpu0());
        var core = Item(vm, "gpu0-core");
        core.YellowMax = 10;
        Assert.NotNull(core.Error);

        manager.Update(c => SensorPreferences.Update(c, "gpu0-core", p =>
        {
            p.ShowInWindow = false;
            p.ShowInTray = false;
            p.Notify = false;
        }));

        Assert.Same(core, vm.Groups.Last().Items.Single());
        Assert.Equal(10, core.YellowMax);
        Assert.NotNull(core.Error);

        manager.Update(c => SensorPreferences.Update(c, "gpu0-core", p => p.ShowInWindow = true));

        Assert.Same(core, Item(vm, "gpu0-core"));
        Assert.Equal("GPU 0", vm.Groups.Single(g => g.Items.Contains(core)).Title);
        Assert.Equal(10, core.YellowMax);
        Assert.NotNull(core.Error);
        Assert.False(ThresholdResolver.HasCustom(manager.Config, "gpu0-core"));
    }

    [Fact]
    public void Search_MatchesTheStoredHardwareName_OfAnUndetectedAddedSensor_WithoutChangingItsCaption()
    {
        var config = WithAdded((VrmId, "VRM", Board, SensorKind.Motherboard));
        var (vm, _, _) = Make(config, CpuAndGpu0());
        var vrm = Item(vm, VrmId);

        vm.SearchText = "crosshair";

        Assert.True(vrm.IsVisible);
        Assert.False(Item(vm, "cpu").IsVisible);
        Assert.Equal(NotDetected, vrm.Caption);
    }

    [Fact]
    public void Search_MatchesTheDefaultName_EvenAfterARename()
    {
        var (vm, _, manager) = Make(readings: CpuAndGpu0());
        manager.Update(c => SensorPreferences.Update(c, "gpu0-hotspot", p => p.Label = "EVGA Hot"));
        var defaultLabel = Item(vm, "gpu0-hotspot").Identity.DefaultLabel;

        vm.SearchText = defaultLabel;

        Assert.Equal("EVGA Hot", Item(vm, "gpu0-hotspot").Label);
        Assert.True(Item(vm, "gpu0-hotspot").IsVisible);
        Assert.False(Item(vm, "gpu0-core").IsVisible);
    }

    [Fact]
    public void Title_IsTheDefaultsSectionTitle()
    {
        Assert.Equal("CPU temperature", ThresholdsViewModel.Title(SensorKind.CpuPackage));
        Assert.Equal("Other sensors", ThresholdsViewModel.Title(SensorKind.OtherTemp));
    }

    // ---- Advanced: every other temperature sensor the hardware reports (listed after a scan) ----

    private const string CcdId = "hw:/amdcpu/0/temperature/3";
    private const string TctlId = "hw:/amdcpu/0/temperature/2";

    private static readonly CatalogSensor[] CatalogSensors =
    {
        new(CcdId, "Core #1 (CCD1)", Cpu5950X, SensorKind.CpuOther, 61.4),
        new(TctlId, "Core (Tctl/Tdie)", Cpu5950X, SensorKind.CpuOther, 60),
        new(VrmId, "VRM", Board, SensorKind.Motherboard, 52),
    };

    private sealed class FakeCatalog : ISensorCatalogSource
    {
        private int _calls;

        public int Calls => _calls;
        public SensorCatalogResult Result { get; set; } = new(Array.Empty<CatalogSensor>(), null);
        public Exception? Throw { get; set; }

        public Task<SensorCatalogResult> ScanAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            if (Throw is not null) return Task.FromException<SensorCatalogResult>(Throw);
            return Task.FromResult(Result);
        }
    }

    private static FakeCatalog CatalogOf(params CatalogSensor[] sensors) =>
        new() { Result = new SensorCatalogResult(sensors, null) };

    private static SensorThresholdViewModel Advanced(ThresholdsViewModel vm, string id) =>
        vm.AdvancedGroups.SelectMany(g => g.Items).Single(i => i.Id == id);

    [Fact]
    public void Advanced_BeforeAScan_IsEmpty_AndTheCatalogWasNeverCalled()
    {
        var catalog = CatalogOf(CatalogSensors);
        var (vm, source, _) = Make(catalog: catalog);

        source.Publish(CpuAndGpu0());

        Assert.Equal(0, catalog.Calls);
        Assert.Empty(vm.AdvancedGroups);
        Assert.False(vm.HasScanned);
        Assert.False(vm.IsAdvancedExpanded);
        Assert.Equal("Scan for sensors", vm.ScanButtonText);
    }

    [Fact]
    public async Task Scan_ListsTheCatalogSensorsThatHaveNoRow_GroupedByDevice()
    {
        var config = WithAdded((VrmId, "VRM", Board, SensorKind.Motherboard));
        var (vm, _, _) = Make(config, CpuAndGpu0(), CatalogOf(CatalogSensors));

        await vm.ScanAsync();

        Assert.Equal(new[] { "CPU" }, vm.AdvancedGroups.Select(g => g.Title));
        Assert.Equal(new[] { CcdId }, vm.AdvancedGroups.Single().Items.Select(i => i.Id));
        Assert.Equal("Scan again", vm.ScanButtonText);
        Assert.Null(vm.ScanMessage);
        Assert.Equal(VrmId, Item(vm, VrmId).Id);
    }

    [Fact]
    public async Task Scan_WithNoAddedSensors_ListsTheVrmUnderItsDevice()
    {
        var (vm, _, _) = Make(readings: CpuAndGpu0(), catalog: CatalogOf(CatalogSensors));

        await vm.ScanAsync();

        Assert.Equal(new[] { "CPU", "Motherboard" }, vm.AdvancedGroups.Select(g => g.Title));
        Assert.Equal(Cpu5950X, vm.AdvancedGroups[0].Subtitle);
        Assert.Equal(new[] { VrmId }, vm.AdvancedGroups[1].Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Scan_AllSensorsAlreadyListed_SetsShowAllListed()
    {
        var catalog = CatalogOf(new CatalogSensor(TctlId, "Core (Tctl/Tdie)", Cpu5950X, SensorKind.CpuOther, 60));
        var (vm, _, _) = Make(readings: CpuAndGpu0(), catalog: catalog);

        await vm.ScanAsync();

        Assert.True(vm.HasScanned);
        Assert.Empty(vm.AdvancedGroups);
        Assert.True(vm.ShowAllListed);
        Assert.Null(vm.ScanMessage);
    }

    [Fact]
    public async Task Scan_FailedAfterAllListed_KeepsTheAllListedState_AndShowsTheError()
    {
        var catalog = CatalogOf(new CatalogSensor(TctlId, "Core (Tctl/Tdie)", Cpu5950X, SensorKind.CpuOther, 60));
        var (vm, _, _) = Make(readings: CpuAndGpu0(), catalog: catalog);
        await vm.ScanAsync();

        catalog.Result = SensorCatalogResult.Unavailable("Monitoring is paused while Riot Vanguard is running.");
        await vm.ScanAsync();

        Assert.Equal("Monitoring is paused while Riot Vanguard is running.", vm.ScanMessage);
        Assert.True(vm.ShowAllListed);
    }

    [Fact]
    public async Task Scan_ACatalogSensorWithABlankName_IsLabelledWithItsIdTail()
    {
        const string id = "hw:/amdcpu/0/temperature/5";
        var (vm, _, _) = Make(catalog: CatalogOf(new CatalogSensor(id, "  ", Cpu5950X, SensorKind.CpuOther, 55)));

        await vm.ScanAsync();

        var label = Advanced(vm, id).Label;
        Assert.False(string.IsNullOrWhiteSpace(label));
        Assert.Equal("5", label);
    }

    [Fact]
    public async Task Scan_ShowsTheScanValueAndCaption()
    {
        var (vm, _, _) = Make(catalog: CatalogOf(CatalogSensors));

        await vm.ScanAsync();

        var ccd = Advanced(vm, CcdId);
        Assert.Equal("61 °C", ccd.ValueText);
        Assert.True(ccd.HasValue);
        Assert.Contains(Cpu5950X, ccd.Caption);
        Assert.Equal("Core #1 (CCD1)", ccd.Label);
        Assert.Equal(ColorZone.Green, ccd.Zone);
    }

    [Fact]
    public async Task Scan_DoesNotAddAnythingOrChangeTheConfig()
    {
        var (vm, _, manager) = Make(readings: CpuAndGpu0(), catalog: CatalogOf(CatalogSensors));
        var rowIds = vm.Groups.SelectMany(g => g.Items).Select(i => i.Id).ToList();

        await vm.ScanAsync();

        Assert.Empty(manager.Config.AddedSensors);
        Assert.Empty(manager.Config.Sensors);
        Assert.Equal(rowIds, vm.Groups.SelectMany(g => g.Items).Select(i => i.Id));
    }

    [Fact]
    public async Task EditingAnAdvancedSensor_StoresOnlyItsOwnBand()
    {
        var (vm, _, manager) = Make(catalog: CatalogOf(CatalogSensors));
        await vm.ScanAsync();

        Advanced(vm, CcdId).GreenMax = 65;

        var ccd = Advanced(vm, CcdId);
        Assert.True(ccd.IsCustom);
        Assert.Equal("Custom", ccd.BadgeText);
        Assert.True(ThresholdResolver.HasCustom(manager.Config, CcdId));
        var pref = manager.Config.Sensors[CcdId];
        Assert.Equal((40d, 65d, 85d), Band(pref.Thresholds!));
        Assert.True(pref.ShowInWindow == null && pref.ShowInTray == null && pref.Notify == null);
        Assert.Empty(manager.Config.AddedSensors);
        Assert.DoesNotContain(vm.Groups.SelectMany(g => g.Items), i => i.Id == CcdId);
        Assert.False(Advanced(vm, VrmId).IsCustom);
        Assert.Equal((45d, 70d, 90d), Limits(Advanced(vm, VrmId)));
    }

    [Fact]
    public async Task ResetOnAnAdvancedSensor_RemovesTheEntryAndFollowsTheKindDefault()
    {
        var (vm, _, manager) = Make(catalog: CatalogOf(CatalogSensors));
        await vm.ScanAsync();
        Advanced(vm, CcdId).GreenMax = 65;
        Advanced(vm, TctlId).GreenMax = 60; // its own band must survive the kind default change below

        Advanced(vm, CcdId).ResetCommand.Execute(null);

        var ccd = Advanced(vm, CcdId);
        Assert.False(manager.Config.Sensors.ContainsKey(CcdId));
        Assert.False(ccd.IsCustom);
        Assert.Equal((40d, 70d, 85d), Limits(ccd));

        Row(vm, SensorKind.CpuOther).GreenMax = 72;

        Assert.Equal(72, Advanced(vm, CcdId).GreenMax);
        Assert.Equal((40d, 60d, 85d), Limits(Advanced(vm, TctlId)));
    }

    [Fact]
    public async Task AnAdvancedSensorThatGetsAdded_MovesToItsDeviceSection_WithItsLimits()
    {
        var (vm, _, manager) = Make(catalog: CatalogOf(CatalogSensors));
        await vm.ScanAsync();
        Advanced(vm, CcdId).GreenMax = 65;

        manager.Update(c =>
        {
            c.AddedSensors = new Dictionary<string, AddedSensor>(c.AddedSensors)
            {
                [CcdId] = new AddedSensor { Name = "Core #1 (CCD1)", HardwareName = Cpu5950X, Kind = SensorKind.CpuOther },
            };
            SensorPreferences.Update(c, CcdId, p => p.ShowInWindow = true);
        });

        Assert.DoesNotContain(vm.AdvancedGroups.SelectMany(g => g.Items), i => i.Id == CcdId);
        var item = Item(vm, CcdId);
        Assert.True(item.IsCustom);
        Assert.Equal((40d, 65d, 85d), Limits(item));

        manager.Update(c =>
        {
            c.AddedSensors = new Dictionary<string, AddedSensor>();
            c.Sensors.Remove(CcdId);
        });

        Assert.DoesNotContain(vm.Groups.SelectMany(g => g.Items), i => i.Id == CcdId);
        Assert.False(Advanced(vm, CcdId).IsCustom);
    }

    [Fact]
    public async Task Scan_Unavailable_KeepsThePreviousList_AndShowsTheReason()
    {
        var catalog = CatalogOf(CatalogSensors);
        var (vm, _, _) = Make(catalog: catalog);
        await vm.ScanAsync();
        var before = vm.AdvancedGroups.SelectMany(g => g.Items).ToList();

        catalog.Result = SensorCatalogResult.Unavailable("Monitoring is paused while Riot Vanguard is running.");
        await vm.ScanAsync();

        Assert.Equal("Monitoring is paused while Riot Vanguard is running.", vm.ScanMessage);
        Assert.True(vm.HasScanMessage);
        Assert.Equal(before, vm.AdvancedGroups.SelectMany(g => g.Items).ToList());
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public async Task Scan_ThatThrows_ShowsAMessage_AndIsNotLeftScanning()
    {
        var catalog = CatalogOf(CatalogSensors);
        catalog.Throw = new InvalidOperationException("boom");
        var (vm, _, _) = Make(catalog: catalog);

        await vm.ScanAsync();

        Assert.Equal("Couldn't scan hardware: boom", vm.ScanMessage);
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public async Task Scan_WithoutACatalog_ShowsNoCatalogMessage()
    {
        var (vm, _, _) = Make();

        await vm.ScanAsync();

        Assert.Equal(AddSensorViewModel.NoCatalogMessage, vm.ScanMessage);
    }

    [Fact]
    public async Task ScanningAgain_ReusesTheRowInstances()
    {
        var (vm, _, _) = Make(catalog: CatalogOf(CatalogSensors));
        await vm.ScanAsync();
        var first = Advanced(vm, CcdId);

        await vm.ScanAsync();

        Assert.Same(first, Advanced(vm, CcdId));
    }

    [Fact]
    public async Task Search_FiltersAdvancedRowsWithoutRecreatingThem_AndOpensTheSection()
    {
        var (vm, _, _) = Make(catalog: CatalogOf(CatalogSensors));
        await vm.ScanAsync();
        var ccd = Advanced(vm, CcdId);
        Assert.False(vm.IsAdvancedExpanded);

        vm.SearchText = "ccd1";
        Assert.True(ccd.IsVisible);
        Assert.True(vm.AdvancedGroups.Single(g => g.Key == "cpu").IsVisible);
        Assert.True(vm.IsAdvancedExpanded);

        vm.SearchText = "nomatch";
        Assert.False(ccd.IsVisible);
        Assert.False(vm.AdvancedGroups.Single(g => g.Key == "cpu").IsVisible);

        vm.SearchText = "";
        Assert.True(ccd.IsVisible);
        Assert.Same(ccd, Advanced(vm, CcdId));
    }

    [Fact]
    public async Task AdvancedValues_AreNotTouchedByATick()
    {
        var (vm, source, _) = Make(readings: CpuAndGpu0(), catalog: CatalogOf(CatalogSensors));
        await vm.ScanAsync();
        var ccd = Advanced(vm, CcdId);

        var tick = CpuAndGpu0().Append(new SensorReading(CcdId, "Core #1 (CCD1)", SensorKind.CpuOther, null, 20, Cpu5950X, "Core #1 (CCD1)"));
        source.Publish(tick.ToList());

        Assert.Same(ccd, Advanced(vm, CcdId));
        Assert.Equal("61 °C", ccd.ValueText);
    }
}
