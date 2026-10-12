using Torrid.Core.Models;

namespace Torrid.Tests;

public class SensorIdentityTests
{
    [Fact]
    public void TryParse_Cpu_ReturnsCpuIdentity()
    {
        var identity = SensorIdentity.TryParse("cpu");

        Assert.NotNull(identity);
        Assert.Equal(SensorKind.CpuPackage, identity!.Kind);
        Assert.Equal("CPU", identity.DefaultLabel);
        Assert.Null(identity.GpuIndex);
    }

    [Theory]
    [InlineData("gpu0-core", SensorKind.GpuCore, "GPU0 Core", 0)]
    [InlineData("gpu1-hotspot", SensorKind.GpuHotSpot, "GPU1 Hot", 1)]
    [InlineData("gpu12-vram", SensorKind.GpuVram, "GPU12 VRAM", 12)]
    public void TryParse_GpuIds_ReturnExpectedIdentity(string id, SensorKind kind, string label, int index)
    {
        var identity = SensorIdentity.TryParse(id);

        Assert.NotNull(identity);
        Assert.Equal(id, identity!.Id);
        Assert.Equal(kind, identity.Kind);
        Assert.Equal(label, identity.DefaultLabel);
        Assert.Equal(index, identity.GpuIndex);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gpu-core")]
    [InlineData("gpu0-fan")]
    [InlineData("gpux-core")]
    [InlineData("GPU0-CORE")]
    [InlineData("gpu0-core\n")]
    [InlineData("gpu99999999999999999999-core")]
    [InlineData("gpu01-core")]
    [InlineData("gpu00-vram")]
    [InlineData("something-else")]
    public void TryParse_InvalidIds_ReturnNull(string? id)
    {
        Assert.Null(SensorIdentity.TryParse(id));
    }

    [Fact]
    public void FromReading_CopiesIdentityFields()
    {
        var reading = new SensorReading("gpu1-vram", "GPU1 VRAM", SensorKind.GpuVram, 1, 70.0);

        var identity = SensorIdentity.FromReading(reading);

        Assert.Equal(new SensorIdentity("gpu1-vram", "GPU1 VRAM", SensorKind.GpuVram, 1), identity);
    }
}
