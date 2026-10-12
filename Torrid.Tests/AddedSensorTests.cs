using Torrid.Core.Config;
using Torrid.Core.Models;

namespace Torrid.Tests;

public class AddedSensorTests
{
    private const string VrmId = "hw:/lpc/nct6798d/0/temperature/4";

    private static AppConfig ConfigWithVrm()
    {
        var config = new AppConfig();
        config.AddedSensors[VrmId] = new AddedSensor { Name = "VRM", HardwareName = "ASUS ROG", Kind = SensorKind.Motherboard };
        return config;
    }

    [Theory]
    [InlineData("cpu", SensorKind.CpuPackage)]
    [InlineData("gpu0-core", SensorKind.GpuCore)]
    [InlineData("gpu2-vram", SensorKind.GpuVram)]
    public void Identity_BuiltInId_UsesTryParse(string id, SensorKind kind)
    {
        var identity = SensorDisplay.Identity(new AppConfig(), id);

        Assert.NotNull(identity);
        Assert.Equal(kind, identity!.Kind);
        Assert.Equal(SensorIdentity.TryParse(id), identity);
    }

    [Fact]
    public void Identity_AddedHwId_UsesTheStoredNameAndKind()
    {
        var identity = SensorDisplay.Identity(ConfigWithVrm(), VrmId);

        Assert.Equal(new SensorIdentity(VrmId, "VRM", SensorKind.Motherboard, null), identity);
    }

    [Fact]
    public void Identity_HwIdNotInAddedSensors_IsNull()
    {
        Assert.Null(SensorDisplay.Identity(ConfigWithVrm(), "hw:/nvme/0/temperature/0"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("hw:")]
    [InlineData("hw:/a b")]
    [InlineData("HW:/lpc/nct6798d/0/temperature/4")]
    public void Identity_GarbageIds_AreNull(string? id)
    {
        Assert.Null(SensorDisplay.Identity(ConfigWithVrm(), id!));
    }

    [Fact]
    public void Identity_AddedEntryUnderAnInvalidKey_IsNotAccepted()
    {
        var config = new AppConfig();
        config.AddedSensors["hw:/a b"] = new AddedSensor { Name = "Bad", Kind = SensorKind.Storage };

        Assert.Null(SensorDisplay.Identity(config, "hw:/a b"));
    }
}
