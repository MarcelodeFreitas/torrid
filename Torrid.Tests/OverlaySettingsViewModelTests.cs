using Torrid.App.ViewModels;
using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Overlay;
using Torrid.Core.Sensors;

namespace Torrid.Tests;

public class OverlaySettingsViewModelTests
{
    private const string Rtx3090 = "NVIDIA GeForce RTX 3090";
    private const string CpuPackageId = "hw:/amdcpu/0/temperature/3";

    private sealed class FakeCatalog : ISensorCatalogSource
    {
        public TaskCompletionSource<SensorCatalogResult> Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<SensorCatalogResult> ScanAsync(CancellationToken ct = default) => Pending.Task;
    }

    private static SensorReading Cpu(double? value = 67.6) =>
        new("cpu", "CPU", SensorKind.CpuPackage, null, value, "AMD Ryzen 9 5950X", "Core (Tctl/Tdie)");

    private static SensorReading Gpu0Core(double? value = 50) =>
        new("gpu0-core", "GPU0 Core", SensorKind.GpuCore, 0, value, Rtx3090, "GPU Core");

    private static SensorReading Gpu0Hot(double? value = 80) =>
        new("gpu0-hotspot", "GPU0 Hot", SensorKind.GpuHotSpot, 0, value, Rtx3090, "GPU Hot Spot");

    private static SensorReading Gpu0Vram(double? value = 82) =>
        new("gpu0-vram", "GPU0 VRAM", SensorKind.GpuVram, 0, value, Rtx3090, "GPU Memory Junction");

    private static (OverlaySettingsViewModel Vm, ConfigManager Config, FakeSensorSourceForTests Sensors) Make(
        AppConfig? config = null, ISensorCatalogSource? catalog = null)
    {
        var sensors = new FakeSensorSourceForTests();
        var manager = new ConfigManager(new InMemoryConfigStore(), config ?? new AppConfig(), TimeSpan.Zero);
        return (new OverlaySettingsViewModel(manager, sensors, catalog), manager, sensors);
    }

    [Fact]
    public void HotkeyText_DefaultsToCtrlAltO()
    {
        var (vm, _, _) = Make();

        Assert.Equal("Ctrl+Alt+O", vm.HotkeyText);
    }

    [Fact]
    public void TrySetHotkey_Valid_StoresTheFormattedString()
    {
        var (vm, config, _) = Make();

        Assert.True(vm.TrySetHotkey(new Hotkey(HotkeyModifiers.Shift, 0x78))); // F9

        Assert.Equal("Shift+F9", config.Config.OverlayHotkey);
        Assert.Equal("Shift+F9", vm.HotkeyText);
    }

    [Fact]
    public void TrySetHotkey_WithoutAModifier_IsRejectedAndNothingChanges()
    {
        var (vm, config, _) = Make();

        Assert.False(vm.TrySetHotkey(new Hotkey(HotkeyModifiers.None, 0x4F)));

        Assert.Equal("Ctrl+Alt+O", config.Config.OverlayHotkey);
        Assert.Equal("Ctrl+Alt+O", vm.HotkeyText);
    }

    [Fact]
    public void HotkeyError_SetAndClear_RaisesHasHotkeyError()
    {
        var (vm, _, _) = Make();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.False(vm.HasHotkeyError);

        vm.SetHotkeyError("Ctrl+Alt+O is already used by another program. Choose a different shortcut.");

        Assert.True(vm.HasHotkeyError);
        Assert.Equal("Ctrl+Alt+O is already used by another program. Choose a different shortcut.", vm.HotkeyError);
        Assert.Contains(nameof(OverlaySettingsViewModel.HasHotkeyError), raised);

        vm.SetHotkeyError(null);

        Assert.False(vm.HasHotkeyError);
        Assert.Null(vm.HotkeyError);
    }

    [Theory]
    [InlineData(5, 10)]
    [InlineData(99, 48)]
    [InlineData(20, 20)]
    public void TextSize_IsClampedAndStored(int requested, int expected)
    {
        var (vm, config, _) = Make();

        vm.TextSize = requested;

        Assert.Equal(expected, vm.TextSize);
        Assert.Equal(expected, config.Config.OverlayTextSize);
    }

    [Fact]
    public void TextColor_ValidIsStored_InvalidIsIgnored()
    {
        var (vm, config, _) = Make();

        vm.TextColor = "#4ED36B";
        Assert.Equal("#4ED36B", config.Config.OverlayTextColor);

        vm.TextColor = "red";
        Assert.Equal("#4ED36B", config.Config.OverlayTextColor);
        Assert.Equal("#4ED36B", vm.TextColor);
    }

    [Fact]
    public void Sensors_ListsTheSensorsPageRows_WithTheTrayDefaultChecked()
    {
        var (vm, _, sensors) = Make();

        sensors.Publish(new[] { Cpu(), Gpu0Core(), Gpu0Hot(), Gpu0Vram() });

        Assert.Equal(new[] { "cpu", "gpu0-core", "gpu0-hotspot", "gpu0-vram" }, vm.Sensors.Select(s => s.Id));
        Assert.Equal(new[] { true, false, true, false }, vm.Sensors.Select(s => s.IsShown));
        Assert.Equal("CPU", vm.Sensors[0].DeviceTitle);
        Assert.Equal("GPU 0", vm.Sensors[1].DeviceTitle);
    }

    [Fact]
    public void Sensors_ToggleWritesOnlyShowInOverlay_AndBackToDefaultRemovesTheEntry()
    {
        var (vm, config, sensors) = Make();
        sensors.Publish(new[] { Cpu() });
        var cpu = vm.Sensors.Single(s => s.Id == "cpu");

        cpu.IsShown = false; // the CPU default is shown, so this is stored explicitly

        Assert.False(cpu.IsShown);
        var stored = config.Config.Sensors["cpu"];
        Assert.False(stored.ShowInOverlay);
        Assert.Null(stored.ShowInTray);
        Assert.Null(stored.ShowInWindow);
        Assert.Null(stored.Label);

        cpu.IsShown = true; // back to the default: the entry goes away

        Assert.True(cpu.IsShown);
        Assert.False(config.Config.Sensors.ContainsKey("cpu"));
    }

    [Fact]
    public void Sensors_NewReadings_AddRowsWithoutRecreatingExistingOnes()
    {
        var (vm, _, sensors) = Make();
        sensors.Publish(new[] { Cpu(), Gpu0Core() });
        var cpu = vm.Sensors.Single(s => s.Id == "cpu");
        var core = vm.Sensors.Single(s => s.Id == "gpu0-core");

        sensors.Publish(new[] { Cpu(), Gpu0Core(), Gpu0Hot() });

        Assert.Equal(3, vm.Sensors.Count);
        Assert.True(ReferenceEquals(cpu, vm.Sensors.Single(s => s.Id == "cpu")));
        Assert.True(ReferenceEquals(core, vm.Sensors.Single(s => s.Id == "gpu0-core")));
    }

    [Fact]
    public void Sensors_Label_Edit_StoresACustomLabel()
    {
        var (vm, config, sensors) = Make();
        sensors.Publish(new[] { Cpu() });
        var cpu = vm.Sensors.Single(s => s.Id == "cpu");

        cpu.Label = "5950X";

        Assert.Equal("5950X", config.Config.Sensors["cpu"].Label);
        Assert.Equal("5950X", cpu.Label);
    }

    [Fact]
    public void Sensors_Label_BlankOrDefault_ClearsTheCustomLabel()
    {
        var (vm, config, sensors) = Make();
        sensors.Publish(new[] { Cpu() });
        var cpu = vm.Sensors.Single(s => s.Id == "cpu");

        cpu.Label = "5950X";
        cpu.Label = "   ";

        Assert.False(config.Config.Sensors.ContainsKey("cpu"));
        Assert.Equal("CPU", cpu.Label);

        cpu.Label = "5950X";
        cpu.Label = "CPU";

        Assert.False(config.Config.Sensors.ContainsKey("cpu"));
        Assert.Equal("CPU", cpu.Label);
    }

    [Fact]
    public void Sensors_Label_Edit_KeepsOtherPreferences()
    {
        var (vm, config, sensors) = Make();
        sensors.Publish(new[] { Cpu() });
        var cpu = vm.Sensors.Single(s => s.Id == "cpu");

        cpu.IsShown = false;
        cpu.Label = "5950X";

        Assert.False(config.Config.Sensors["cpu"].ShowInOverlay);
        Assert.Equal("5950X", config.Config.Sensors["cpu"].Label);
    }

    [Fact]
    public void Sensors_Label_ComesBackFromTheConfig_WhenChangedElsewhere()
    {
        var (vm, config, sensors) = Make();
        sensors.Publish(new[] { Cpu() });

        config.Update(c => SensorPreferences.Update(c, "cpu", p => p.Label = "Renamed"));
        sensors.Publish(new[] { Cpu() });

        Assert.Equal("Renamed", vm.Sensors.Single(s => s.Id == "cpu").Label);
    }

    [Fact]
    public void Sensors_Caption_ShowsTheRealSensorName_EvenAfterRenaming()
    {
        var (vm, _, sensors) = Make();
        sensors.Publish(new[] { Cpu() });
        var cpu = vm.Sensors.Single(s => s.Id == "cpu");

        Assert.Equal("Sensor: CPU · CPU", cpu.Caption);

        cpu.Label = "5950X";

        Assert.Equal("Sensor: CPU · CPU", cpu.Caption);
    }

    [Fact]
    public async Task Sync_Scan_ListsTheCatalog_AndAddingShowsTheSensorInTheList()
    {
        var catalog = new FakeCatalog();
        catalog.Pending.SetResult(new SensorCatalogResult(
            new[] { new CatalogSensor(CpuPackageId, "CPU Package", "AMD Ryzen 9 5950X", SensorKind.OtherTemp, 40.0) }, null));
        var (vm, config, _) = Make(catalog: catalog);

        await vm.Sync.ScanAsync();
        var item = vm.Sync.Groups.Single().Items.Single();
        vm.Sync.Add(item);

        Assert.True(config.Config.AddedSensors?.ContainsKey(CpuPackageId));
        Assert.Contains(vm.Sensors, s => s.Id == CpuPackageId);
    }

    [Fact]
    public async Task Sync_ButtonReadsSyncing_WhileAScanRuns()
    {
        var catalog = new FakeCatalog();
        var (vm, _, _) = Make(catalog: catalog);

        var scan = vm.Sync.ScanAsync();
        Assert.Equal("Syncing…", vm.SyncButtonText);

        catalog.Pending.SetResult(new SensorCatalogResult(Array.Empty<CatalogSensor>(), null));
        await scan;

        Assert.Equal("Sync sensors", vm.SyncButtonText);
    }

    [Fact]
    public void HiddenFromCapture_DefaultsToTrue()
    {
        var (vm, _, _) = Make();

        Assert.True(vm.HiddenFromCapture);
    }

    [Fact]
    public void HiddenFromCapture_Set_WritesTheConfig()
    {
        var (vm, config, _) = Make();

        vm.HiddenFromCapture = false;

        Assert.False(config.Config.OverlayHiddenFromCapture);
        Assert.False(vm.HiddenFromCapture);
    }

    [Fact]
    public void HiddenFromCapture_ComesBackFromTheConfig()
    {
        var (vm, config, _) = Make();

        config.Update(c => c.OverlayHiddenFromCapture = false);

        Assert.False(vm.HiddenFromCapture);
    }

    [Fact]
    public void Sensors_Caption_ShowsTheAddedSensorsHardwareName()
    {
        var config = new AppConfig();
        config.AddedSensors = new Dictionary<string, AddedSensor>
        {
            [CpuPackageId] = new AddedSensor { Name = "CPU Package", HardwareName = "AMD Ryzen 9 5950X", Kind = SensorKind.OtherTemp },
        };
        var (vm, _, _) = Make(config);

        Assert.Equal("Sensor: CPU Package · Other", vm.Sensors.Single(s => s.Id == CpuPackageId).Caption);
    }
}
