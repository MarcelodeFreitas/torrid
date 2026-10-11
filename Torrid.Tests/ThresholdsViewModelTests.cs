using Torrid.App.ViewModels;
using Torrid.Core.Config;
using Torrid.Core.Models;

namespace Torrid.Tests;

public class ThresholdsViewModelTests
{
    private static (ThresholdsViewModel Vm, ConfigManager Config) Make(AppConfig? config = null)
    {
        var manager = new ConfigManager(new InMemoryConfigStore(), config ?? new AppConfig(), TimeSpan.Zero);
        return (new ThresholdsViewModel(manager), manager);
    }

    private static ThresholdRowViewModel Row(ThresholdsViewModel vm, SensorKind kind) => vm.Rows.Single(r => r.Kind == kind);

    [Fact]
    public void Rows_OnePerKind_SeededFromConfig()
    {
        var config = new AppConfig();
        config.Thresholds[SensorKind.GpuVram] = new ThresholdBand { BlueMax = 55, GreenMax = 88, YellowMax = 100 };

        var (vm, _) = Make(config);

        Assert.Equal(Enum.GetValues<SensorKind>(), vm.Rows.Select(r => r.Kind));
        var vram = Row(vm, SensorKind.GpuVram);
        Assert.Equal((55d, 88d, 100d), (vram.BlueMax, vram.GreenMax, vram.YellowMax));
        Assert.Equal("CPU temperature", Row(vm, SensorKind.CpuPackage).Title);
        Assert.All(vm.Rows, r => Assert.Null(r.Error));
    }

    [Fact]
    public void ValidEdit_UpdatesConfig_WithoutError()
    {
        var (vm, config) = Make();
        var cpu = Row(vm, SensorKind.CpuPackage);

        cpu.GreenMax = 72;
        cpu.YellowMax = 90;
        cpu.BlueMax = 35;

        var band = config.Config.Thresholds[SensorKind.CpuPackage];
        Assert.Equal((35d, 72d, 90d), (band.BlueMax, band.GreenMax, band.YellowMax));
        Assert.Null(cpu.Error);
    }

    [Theory]
    [InlineData(70, 70, 85)]   // Blue >= Green
    [InlineData(75, 70, 85)]   // Blue > Green
    [InlineData(40, 85, 85)]   // Green >= Yellow
    [InlineData(-1, 70, 85)]   // negative
    [InlineData(40, 70, 151)]  // above 150
    public void InvalidOrdering_LeavesConfigUntouched_AndSetsError(double blue, double green, double yellow)
    {
        var (vm, config) = Make();
        var cpu = Row(vm, SensorKind.CpuPackage);
        var before = config.Config.Thresholds[SensorKind.CpuPackage];

        cpu.BlueMax = blue;
        cpu.GreenMax = green;
        cpu.YellowMax = yellow;

        var after = config.Config.Thresholds[SensorKind.CpuPackage];
        Assert.Equal((40d, 70d, 85d), (after.BlueMax, after.GreenMax, after.YellowMax));
        Assert.Same(before, after);
        Assert.False(string.IsNullOrWhiteSpace(cpu.Error));
    }

    [Fact]
    public void FixingAnInvalidValue_ClearsError_AndApplies()
    {
        var (vm, config) = Make();
        var hot = Row(vm, SensorKind.GpuHotSpot);

        hot.GreenMax = 99; // above YellowMax 95
        Assert.NotNull(hot.Error);
        Assert.Equal(85, config.Config.Thresholds[SensorKind.GpuHotSpot].GreenMax);

        hot.YellowMax = 105;
        Assert.Null(hot.Error);
        var band = config.Config.Thresholds[SensorKind.GpuHotSpot];
        Assert.Equal((50d, 99d, 105d), (band.BlueMax, band.GreenMax, band.YellowMax));
    }

    [Fact]
    public void Reset_RestoresDefaultBand()
    {
        var config = new AppConfig();
        config.Thresholds[SensorKind.GpuCore] = new ThresholdBand { BlueMax = 10, GreenMax = 20, YellowMax = 30 };
        var (vm, manager) = Make(config);
        var core = Row(vm, SensorKind.GpuCore);
        core.BlueMax = 25; // invalid (>= GreenMax 20) so an error is pending

        core.ResetCommand.Execute(null);

        var expected = AppConfig.DefaultThresholds()[SensorKind.GpuCore];
        var band = manager.Config.Thresholds[SensorKind.GpuCore];
        Assert.Equal((expected.BlueMax, expected.GreenMax, expected.YellowMax), (band.BlueMax, band.GreenMax, band.YellowMax));
        Assert.Equal((expected.BlueMax, expected.GreenMax, expected.YellowMax), (core.BlueMax, core.GreenMax, core.YellowMax));
        Assert.Null(core.Error);
    }

    [Fact]
    public void PreviewWidths_AreProportionalToTheBand()
    {
        var (vm, _) = Make();
        var cpu = Row(vm, SensorKind.CpuPackage); // 40 / 70 / 85

        Assert.True(cpu.BlueWidth.IsStar);
        Assert.Equal(40, cpu.BlueWidth.Value);
        Assert.Equal(30, cpu.GreenWidth.Value);
        Assert.Equal(15, cpu.YellowWidth.Value);
        Assert.Equal(85 * 0.25, cpu.RedWidth.Value);

        cpu.YellowMax = 100;
        Assert.Equal(30, cpu.YellowWidth.Value);
        Assert.Equal(25, cpu.RedWidth.Value);
    }

    [Fact]
    public void PropertyChanged_RaisedForWidthsAndError()
    {
        var (vm, _) = Make();
        var cpu = Row(vm, SensorKind.CpuPackage);
        var raised = new List<string?>();
        cpu.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        cpu.BlueMax = 50;
        cpu.BlueMax = 80;

        Assert.Contains(nameof(ThresholdRowViewModel.BlueWidth), raised);
        Assert.Contains(nameof(ThresholdRowViewModel.GreenWidth), raised);
        Assert.Contains(nameof(ThresholdRowViewModel.Error), raised);
    }
}
