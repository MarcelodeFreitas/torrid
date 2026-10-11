using Torrid.Core.Config;
using Torrid.Core.Models;

namespace Torrid.Tests;

public class SensorDefaultsTests
{
    [Theory]
    [InlineData(SensorKind.CpuPackage, true)]
    [InlineData(SensorKind.GpuHotSpot, true)]
    [InlineData(SensorKind.GpuCore, false)]
    [InlineData(SensorKind.GpuVram, false)]
    public void ShownByDefault_OnlyForCpuAndGpuHotSpot(SensorKind kind, bool expected)
    {
        Assert.Equal(expected, SensorDefaults.ShownInWindow(kind));
        Assert.Equal(expected, SensorDefaults.ShownInTray(kind));
    }

    [Theory]
    [InlineData(SensorKind.CpuPackage)]
    [InlineData(SensorKind.GpuCore)]
    [InlineData(SensorKind.GpuHotSpot)]
    [InlineData(SensorKind.GpuVram)]
    public void Notify_IsOnForEveryKindByDefault(SensorKind kind)
    {
        Assert.True(SensorDefaults.Notify(kind));
    }
}
