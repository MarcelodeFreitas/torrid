using Torrid.App.ViewModels;
using Torrid.Core.Config;
using Torrid.Core.Models;

namespace Torrid.Tests;

public class SettingsViewModelTests
{
    private static SensorReading Cpu() => new("cpu", "CPU", SensorKind.CpuPackage, null, 50.0);
    private static SensorReading Gpu0Core() => new("gpu0-core", "GPU0 Core", SensorKind.GpuCore, 0, 50.0);

    [Fact]
    public void Constructor_BuildsOneSensorRowPerCurrentReading()
    {
        var config = new AppConfig();
        var viewModel = new SettingsViewModel(config, new[] { Cpu(), Gpu0Core() });

        Assert.Equal(2, viewModel.SensorRows.Count);
    }

    [Fact]
    public void Constructor_WithNoGpus_BuildsJustTheCpuRowWithoutThrowing()
    {
        var config = new AppConfig();

        var viewModel = new SettingsViewModel(config, new[] { Cpu() });

        Assert.Single(viewModel.SensorRows);
    }

    [Fact]
    public void Apply_UncheckingVisible_ExcludesThatSensorFromVisibilityOnly()
    {
        var config = new AppConfig();
        var viewModel = new SettingsViewModel(config, new[] { Cpu(), Gpu0Core() });
        var cpuRow = viewModel.SensorRows.Single(r => r.SensorId == "cpu");

        cpuRow.IsVisible = false;
        viewModel.Apply();

        // Verify through the actual public contract (SensorDisplay.IsVisible), not raw
        // set membership — VisibleSensorIds stores the VISIBLE set when not all sensors
        // are visible (see the Apply() note below), so asserting membership directly
        // would silently invert if that internal representation ever changed.
        Assert.False(SensorDisplay.IsVisible(config, "cpu"));
        Assert.True(SensorDisplay.IsVisible(config, "gpu0-core"));
    }

    [Fact]
    public void Apply_SettingCustomLabel_WritesToCustomLabels()
    {
        var config = new AppConfig();
        var viewModel = new SettingsViewModel(config, new[] { Gpu0Core() });
        var row = viewModel.SensorRows.Single();

        row.CustomLabel = "MSI Core";
        viewModel.Apply();

        Assert.Equal("MSI Core", config.CustomLabels["gpu0-core"]);
    }

    [Fact]
    public void Apply_SettingCustomLabelToWhitespace_RemovesAnyExistingOverride()
    {
        var config = new AppConfig { CustomLabels = { ["cpu"] = "5950X" } };
        var viewModel = new SettingsViewModel(config, new[] { Cpu() });
        var row = viewModel.SensorRows.Single();

        row.CustomLabel = "   ";
        viewModel.Apply();

        Assert.False(config.CustomLabels.ContainsKey("cpu"));
    }

    [Fact]
    public void Apply_UncheckingNotify_AddsSensorIdToNotificationDisabledSensorIds()
    {
        var config = new AppConfig();
        var viewModel = new SettingsViewModel(config, new[] { Gpu0Core() });
        var row = viewModel.SensorRows.Single();

        row.NotificationsEnabled = false;
        viewModel.Apply();

        Assert.Contains("gpu0-core", config.NotificationDisabledSensorIds);
    }

    [Theory]
    [InlineData(0, 500)]
    [InlineData(-100, 500)]
    [InlineData(50, 500)]
    [InlineData(2000, 2000)]
    [InlineData(120000, 60000)]
    public void Apply_ClampsPollIntervalToSaneRange(int enteredMs, int expectedMs)
    {
        var config = new AppConfig();
        var viewModel = new SettingsViewModel(config, new[] { Cpu() }) { PollIntervalMs = enteredMs };

        var applied = viewModel.Apply();

        Assert.Equal(expectedMs, applied);
        Assert.Equal(expectedMs, config.PollIntervalMs);
    }

    [Fact]
    public void Apply_WritesEditedThresholdBandBackToConfig()
    {
        var config = new AppConfig();
        var viewModel = new SettingsViewModel(config, new[] { Cpu() });
        var cpuThresholds = viewModel.ThresholdRows.Single(r => r.Kind == SensorKind.CpuPackage);

        cpuThresholds.BlueMax = 35;
        cpuThresholds.GreenMax = 65;
        cpuThresholds.YellowMax = 80;
        viewModel.Apply();

        var band = config.Thresholds[SensorKind.CpuPackage];
        Assert.Equal(35, band.BlueMax);
        Assert.Equal(65, band.GreenMax);
        Assert.Equal(80, band.YellowMax);
    }
}
