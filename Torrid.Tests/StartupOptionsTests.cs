using Torrid.App;

namespace Torrid.Tests;

public class StartupOptionsTests
{
    [Fact]
    public void NoArguments_UsesDefaults()
    {
        var options = StartupOptions.Parse(Array.Empty<string>());

        Assert.False(options.ShowWindow);
        Assert.False(options.Demo);
        Assert.False(options.DemoPaused);
        Assert.Null(options.ConfigPath);
    }

    [Fact]
    public void Flags_AreParsedCaseInsensitively()
    {
        var options = StartupOptions.Parse(new[] { "--SHOW", "--demo", "--config", @"C:\temp\c.json" });

        Assert.True(options.ShowWindow);
        Assert.True(options.Demo);
        Assert.Equal(@"C:\temp\c.json", options.ConfigPath);
    }

    [Fact]
    public void DemoPaused_ImpliesDemo()
    {
        var options = StartupOptions.Parse(new[] { "--demo-paused" });

        Assert.True(options.Demo);
        Assert.True(options.DemoPaused);
    }

    [Fact]
    public void ConfigFlagWithoutValue_IsIgnored()
    {
        Assert.Null(StartupOptions.Parse(new[] { "--config" }).ConfigPath);
    }
}
