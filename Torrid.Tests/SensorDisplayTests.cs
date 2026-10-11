using Torrid.Core.Config;
using Torrid.Core.Models;

namespace Torrid.Tests;

public class SensorDisplayTests
{
    private static readonly SensorIdentity Cpu = new("cpu", "CPU", SensorKind.CpuPackage, null);
    private static readonly SensorIdentity Core = new("gpu0-core", "GPU0 Core", SensorKind.GpuCore, 0);
    private static readonly SensorIdentity Hot = new("gpu0-hotspot", "GPU0 Hot", SensorKind.GpuHotSpot, 0);
    private static readonly SensorIdentity Vram = new("gpu0-vram", "GPU0 VRAM", SensorKind.GpuVram, 0);

    [Fact]
    public void ByDefault_OnlyCpuAndHotSpotAreShown()
    {
        var config = new AppConfig();

        Assert.True(SensorDisplay.IsShownInWindow(config, Cpu));
        Assert.True(SensorDisplay.IsShownInWindow(config, Hot));
        Assert.False(SensorDisplay.IsShownInWindow(config, Core));
        Assert.False(SensorDisplay.IsShownInWindow(config, Vram));
        Assert.True(SensorDisplay.IsShownInTray(config, Cpu));
        Assert.False(SensorDisplay.IsShownInTray(config, Vram));
    }

    [Fact]
    public void ExplicitOverride_BeatsTheKindDefault_InBothDirections()
    {
        var config = new AppConfig();
        SensorPreferences.Update(config, "gpu0-vram", p => { p.ShowInWindow = true; p.ShowInTray = true; });
        SensorPreferences.Update(config, "cpu", p => { p.ShowInWindow = false; p.ShowInTray = false; });

        Assert.True(SensorDisplay.IsShownInWindow(config, Vram));
        Assert.True(SensorDisplay.IsShownInTray(config, Vram));
        Assert.False(SensorDisplay.IsShownInWindow(config, Cpu));
        Assert.False(SensorDisplay.IsShownInTray(config, Cpu));
    }

    [Fact]
    public void WindowAndTrayAreIndependent()
    {
        var config = new AppConfig();
        SensorPreferences.Update(config, "gpu0-hotspot", p => p.ShowInTray = false);

        Assert.True(SensorDisplay.IsShownInWindow(config, Hot));
        Assert.False(SensorDisplay.IsShownInTray(config, Hot));
    }

    [Fact]
    public void ResolveLabel_UsesCustomLabelOtherwiseTheDefault()
    {
        var config = new AppConfig();
        Assert.Equal("GPU0 Hot", SensorDisplay.ResolveLabel(config, Hot));

        SensorPreferences.Update(config, "gpu0-hotspot", p => p.Label = "MSI Hot Spot");
        Assert.Equal("MSI Hot Spot", SensorDisplay.ResolveLabel(config, Hot));
    }

    [Fact]
    public void ResolveLabel_WhitespaceLabelFallsBackToDefault()
    {
        var config = new AppConfig();
        config.Sensors["cpu"] = new SensorPreference { Label = "   " };

        Assert.Equal("CPU", SensorDisplay.ResolveLabel(config, Cpu));
    }

    [Fact]
    public void Notifications_AreOnByDefault_AndCanBeSwitchedOffPerSensor()
    {
        var config = new AppConfig();
        Assert.True(SensorDisplay.NotificationsEnabled(config, Vram));

        SensorPreferences.Update(config, "gpu0-vram", p => p.Notify = false);

        Assert.False(SensorDisplay.NotificationsEnabled(config, Vram));
        Assert.True(SensorDisplay.NotificationsEnabled(config, Hot));
    }

    [Fact]
    public void ReadingOverloads_MatchTheIdentityOverloads()
    {
        var config = new AppConfig();
        SensorPreferences.Update(config, "gpu0-core", p => { p.ShowInWindow = true; p.Label = "Core!"; p.Notify = false; });
        var reading = new SensorReading("gpu0-core", "GPU0 Core", SensorKind.GpuCore, 0, 50.0);

        Assert.True(SensorDisplay.IsShownInWindow(config, reading));
        Assert.False(SensorDisplay.IsShownInTray(config, reading));
        Assert.False(SensorDisplay.NotificationsEnabled(config, reading));
        Assert.Equal("Core!", SensorDisplay.ResolveLabel(config, reading));
    }
}
