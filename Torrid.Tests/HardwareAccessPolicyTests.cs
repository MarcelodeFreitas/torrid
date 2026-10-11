using Torrid.Core.Sensors;

namespace Torrid.Tests;

public class HardwareAccessPolicyTests
{
    private static readonly AntiCheatMatch Vanguard = new("Riot Vanguard", "vgc");

    [Theory]
    [InlineData(AntiCheatBehavior.Pause)]
    [InlineData(AntiCheatBehavior.GpuOnly)]
    [InlineData(AntiCheatBehavior.Continue)]
    public void NoAntiCheat_AlwaysFullAccess(AntiCheatBehavior behavior)
    {
        Assert.Equal(HardwareAccessLevel.Full, HardwareAccessPolicy.Decide(behavior, null));
    }

    [Theory]
    [InlineData(AntiCheatBehavior.Pause, HardwareAccessLevel.Paused)]
    [InlineData(AntiCheatBehavior.GpuOnly, HardwareAccessLevel.GpuOnly)]
    [InlineData(AntiCheatBehavior.Continue, HardwareAccessLevel.Full)]
    public void AntiCheatRunning_FollowsTheConfiguredBehavior(AntiCheatBehavior behavior, HardwareAccessLevel expected)
    {
        Assert.Equal(expected, HardwareAccessPolicy.Decide(behavior, Vanguard));
    }

    [Fact]
    public void Evaluate_CombinesDetectionAndPolicy()
    {
        var status = HardwareAccessPolicy.Evaluate(AntiCheatBehavior.Pause, new[] { "explorer", "vgc" }, null);

        Assert.Equal(HardwareAccessLevel.Paused, status.Level);
        Assert.Equal("Riot Vanguard", status.AntiCheat!.Product);
    }

    [Fact]
    public void Evaluate_WithNothingRunning_IsFullAccessWithNoMatch()
    {
        var status = HardwareAccessPolicy.Evaluate(AntiCheatBehavior.Pause, new[] { "explorer" }, null);

        Assert.Equal(HardwareStatus.FullAccess, status);
    }
}
