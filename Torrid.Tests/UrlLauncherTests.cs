using Torrid.App.Drivers;

namespace Torrid.Tests;

public class UrlLauncherTests
{
    [Theory]
    [InlineData("https://github.com/namazso/PawnIO.Setup/releases/latest")]
    [InlineData("HTTPS://example.com/")]
    public void IsOpenable_AcceptsAbsoluteHttpsUrls(string url) => Assert.True(UrlLauncher.IsOpenable(url));

    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("calc.exe")]
    [InlineData("ms-settings:")]
    [InlineData("/relative/path")]
    [InlineData("")]
    [InlineData(null)]
    public void IsOpenable_RejectsEverythingElse(string? url) => Assert.False(UrlLauncher.IsOpenable(url));
}
