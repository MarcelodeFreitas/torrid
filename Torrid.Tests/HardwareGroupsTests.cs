using Torrid.Core.Models;
using Torrid.Core.Sensors;

namespace Torrid.Tests;

public class HardwareGroupsTests
{
    private const HardwareGroups All =
        HardwareGroups.Cpu | HardwareGroups.Gpu | HardwareGroups.Motherboard | HardwareGroups.Memory | HardwareGroups.Storage;

    [Fact]
    public void ForLevel_IsTheCeilingEachLevelPermits()
    {
        Assert.Equal(HardwareGroups.None, HardwareGroupsPolicy.ForLevel(HardwareAccessLevel.Paused));
        Assert.Equal(HardwareGroups.Gpu | HardwareGroups.Storage, HardwareGroupsPolicy.ForLevel(HardwareAccessLevel.GpuOnly));
        Assert.Equal(All, HardwareGroupsPolicy.ForLevel(HardwareAccessLevel.Full));
    }

    [Fact]
    public void ForLevel_UnknownLevel_FailsClosed()
    {
        Assert.Equal(HardwareGroups.None, HardwareGroupsPolicy.ForLevel((HardwareAccessLevel)42));
    }

    [Fact]
    public void ForLevel_GpuOnly_NeverIncludesAPawnIoGroup()
    {
        var gpuOnly = HardwareGroupsPolicy.ForLevel(HardwareAccessLevel.GpuOnly);

        Assert.Equal(HardwareGroups.None, gpuOnly & (HardwareGroups.Cpu | HardwareGroups.Motherboard | HardwareGroups.Memory));
    }

    [Fact]
    public void Wanted_WithNothingAdded_IsCpuAndGpu()
    {
        Assert.Equal(HardwareGroups.Cpu | HardwareGroups.Gpu, HardwareGroupsPolicy.Wanted(Array.Empty<SensorKind>()));
    }

    [Theory]
    [InlineData(SensorKind.Motherboard, HardwareGroups.Motherboard)]
    [InlineData(SensorKind.Memory, HardwareGroups.Memory)]
    [InlineData(SensorKind.Storage, HardwareGroups.Storage)]
    public void Wanted_AddsTheGroupOfAnAddedKind(SensorKind kind, HardwareGroups extra)
    {
        Assert.Equal(HardwareGroups.Cpu | HardwareGroups.Gpu | extra, HardwareGroupsPolicy.Wanted(new[] { kind }));
    }

    [Theory]
    [InlineData(SensorKind.CpuOther)]
    [InlineData(SensorKind.GpuOther)]
    [InlineData(SensorKind.OtherTemp)]
    [InlineData(SensorKind.CpuPackage)]
    [InlineData(SensorKind.GpuCore)]
    public void Wanted_CpuOtherGpuOtherAndOtherTemp_AddNoExtraGroup(SensorKind kind)
    {
        Assert.Equal(HardwareGroups.Cpu | HardwareGroups.Gpu, HardwareGroupsPolicy.Wanted(new[] { kind }));
    }

    [Fact]
    public void GroupFor_MapsKindsToGroups()
    {
        Assert.Equal(HardwareGroups.Cpu, HardwareGroupsPolicy.GroupFor(SensorKind.CpuOther));
        Assert.Equal(HardwareGroups.Cpu, HardwareGroupsPolicy.GroupFor(SensorKind.CpuPackage));
        Assert.Equal(HardwareGroups.Gpu, HardwareGroupsPolicy.GroupFor(SensorKind.GpuOther));
        Assert.Equal(HardwareGroups.Gpu, HardwareGroupsPolicy.GroupFor(SensorKind.GpuVram));
        Assert.Equal(HardwareGroups.Motherboard, HardwareGroupsPolicy.GroupFor(SensorKind.Motherboard));
        Assert.Equal(HardwareGroups.Memory, HardwareGroupsPolicy.GroupFor(SensorKind.Memory));
        Assert.Equal(HardwareGroups.Storage, HardwareGroupsPolicy.GroupFor(SensorKind.Storage));
        Assert.Equal(HardwareGroups.None, HardwareGroupsPolicy.GroupFor(SensorKind.OtherTemp));
    }

    [Fact]
    public void Wanted_CombinesSeveralKinds()
    {
        var wanted = HardwareGroupsPolicy.Wanted(new[] { SensorKind.Motherboard, SensorKind.Storage, SensorKind.Motherboard });

        Assert.Equal(HardwareGroups.Cpu | HardwareGroups.Gpu | HardwareGroups.Motherboard | HardwareGroups.Storage, wanted);
    }

    [Fact]
    public void Effective_FullWithNothingAdded_IsCpuAndGpu_AsBeforePhase5()
    {
        Assert.Equal(HardwareGroups.Cpu | HardwareGroups.Gpu,
            HardwareGroupsPolicy.Effective(HardwareAccessLevel.Full, Array.Empty<SensorKind>()));
    }

    [Fact]
    public void Effective_FullWithMotherboard_AddsMotherboard()
    {
        Assert.Equal(HardwareGroups.Cpu | HardwareGroups.Gpu | HardwareGroups.Motherboard,
            HardwareGroupsPolicy.Effective(HardwareAccessLevel.Full, new[] { SensorKind.Motherboard }));
    }

    [Fact]
    public void Effective_GpuOnlyWithNothingAdded_IsGpu_AsBeforePhase5()
    {
        Assert.Equal(HardwareGroups.Gpu, HardwareGroupsPolicy.Effective(HardwareAccessLevel.GpuOnly, Array.Empty<SensorKind>()));
    }

    [Fact]
    public void Effective_GpuOnlyWithMotherboard_IsGpuOnly()
    {
        Assert.Equal(HardwareGroups.Gpu,
            HardwareGroupsPolicy.Effective(HardwareAccessLevel.GpuOnly, new[] { SensorKind.Motherboard, SensorKind.Memory, SensorKind.CpuOther }));
    }

    [Fact]
    public void Effective_GpuOnlyWithStorage_IsGpuAndStorage()
    {
        Assert.Equal(HardwareGroups.Gpu | HardwareGroups.Storage,
            HardwareGroupsPolicy.Effective(HardwareAccessLevel.GpuOnly, new[] { SensorKind.Storage }));
    }

    [Fact]
    public void Effective_Paused_IsNoneWhateverIsAdded()
    {
        Assert.Equal(HardwareGroups.None, HardwareGroupsPolicy.Effective(HardwareAccessLevel.Paused, Array.Empty<SensorKind>()));
        Assert.Equal(HardwareGroups.None,
            HardwareGroupsPolicy.Effective(HardwareAccessLevel.Paused, Enum.GetValues<SensorKind>()));
    }
}
