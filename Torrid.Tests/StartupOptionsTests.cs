using Torrid.App;
using Torrid.App.ViewModels;

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

    [Theory]
    [InlineData("Sensors", PageId.Sensors)]
    [InlineData("thresholds", PageId.Thresholds)]
    [InlineData("SETTINGS", PageId.Settings)]
    [InlineData("home", PageId.Home)]
    public void Page_IsParsedCaseInsensitively(string value, PageId expected)
    {
        Assert.Equal(expected, StartupOptions.Parse(new[] { "--show", "--page", value }).InitialPage);
    }

    [Theory]
    [InlineData("Nowhere")]
    [InlineData("2")]
    [InlineData("")]
    public void InvalidPage_IsIgnored(string value)
    {
        var options = StartupOptions.Parse(new[] { "--page", value, "--demo" });

        Assert.Null(options.InitialPage);
        Assert.True(options.Demo);
    }

    [Fact]
    public void PageFlagWithoutValue_IsIgnored()
    {
        Assert.Null(StartupOptions.Parse(new[] { "--page" }).InitialPage);
        Assert.Null(StartupOptions.Parse(Array.Empty<string>()).InitialPage);
    }

    [Fact]
    public void TestToast_IsParsedCaseInsensitively_AndOffByDefault()
    {
        Assert.False(StartupOptions.Parse(Array.Empty<string>()).TestToast);

        var options = StartupOptions.Parse(new[] { "--Test-Toast", "--config", @"C:\temp\c.json" });

        Assert.True(options.TestToast);
        Assert.Equal(@"C:\temp\c.json", options.ConfigPath);
        Assert.False(options.Demo);
    }

    [Fact]
    public void ConfigFlagWithoutValue_IsIgnored()
    {
        Assert.Null(StartupOptions.Parse(new[] { "--config" }).ConfigPath);
    }
}
