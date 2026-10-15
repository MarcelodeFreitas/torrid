using Torrid.App.ViewModels;
using Torrid.Core.Config;
using Torrid.Core.Models;

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
        AppConfig? config = null, IReadOnlyList<SensorReading>? readings = null)
    {
        var source = new FakeSensorSourceForTests();
        if (readings is not null) source.Publish(readings);
        var manager = new ConfigManager(new InMemoryConfigStore(), config ?? new AppConfig(), TimeSpan.Zero);
        return (new ThresholdsViewModel(source, manager), source, manager);
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
        Assert.False(item.IsDetected);
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
        Assert.True(Item(vm, "cpu").IsDetected);
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
        Assert.False(vrm.IsDetected);
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
    public void SixtySensors_PerTickUpdates_CreateNoNewRowInstances()
    {
        var (vm, source, _) = Make(readings: SixtySensors(50));
        var groupsBefore = vm.Groups.ToList();
        var itemsBefore = vm.Groups.SelectMany(g => g.Items).ToList();
        Assert.Equal(60, itemsBefore.Count);

        source.Publish(SixtySensors(91));
        source.Publish(SixtySensors(20));

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
        Assert.True(Item(vm, "gpu15-hotspot").IsFocused);
        Assert.False(Item(vm, "gpu15-core").IsFocused);
    }

    [Fact]
    public void Focus_MovesTheFocusFlag_AndRaisesAgainForTheSameSensor()
    {
        var (vm, _, _) = Make(readings: CpuAndGpu0());
        vm.Focus("cpu");
        var raised = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ThresholdsViewModel.FocusedSensorId)) raised++; };

        vm.Focus("gpu0-core");
        vm.Focus("gpu0-core");

        Assert.False(Item(vm, "cpu").IsFocused);
        Assert.True(Item(vm, "gpu0-core").IsFocused);
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
    public void Title_IsTheDefaultsSectionTitle()
    {
        Assert.Equal("CPU temperature", ThresholdsViewModel.Title(SensorKind.CpuPackage));
        Assert.Equal("Other sensors", ThresholdsViewModel.Title(SensorKind.OtherTemp));
    }
}
