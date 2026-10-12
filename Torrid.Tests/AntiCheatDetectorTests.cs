using Torrid.Core.Sensors;

namespace Torrid.Tests;

public class AntiCheatDetectorTests
{
    [Theory]
    [InlineData("vgc", "Riot Vanguard")]
    [InlineData("VALORANT-Win64-Shipping", "Riot Vanguard")]
    [InlineData("EasyAntiCheat_EOS", "Easy Anti-Cheat")]
    [InlineData("BEService", "BattlEye")]
    [InlineData("FACEIT", "FACEIT Anti-Cheat")]
    [InlineData("SGuard64", "Tencent ACE")]
    [InlineData("PnkBstrA", "PunkBuster")]
    [InlineData("GameMon64", "nProtect GameGuard")]
    public void Detect_RecognisesKnownAntiCheatProcesses(string process, string product)
    {
        var match = AntiCheatDetector.Detect(new[] { "explorer", process, "chrome" });

        Assert.NotNull(match);
        Assert.Equal(product, match!.Product);
        Assert.Equal(process, match.ProcessName);
    }

    [Fact]
    public void Detect_SkipsNullNamesInRunningAndExtraLists()
    {
        var running = new string?[] { null, "explorer", null, "mygame" };
        var extra = new string?[] { null, "  ", "mygame" };

        var match = AntiCheatDetector.Detect(running!, extra!);

        Assert.Equal("Custom", match!.Product);
        Assert.Equal("mygame", match.DisplayName);
        Assert.Null(AntiCheatDetector.Detect(new string?[] { null }!, new string?[] { null }!));
    }

    [Fact]
    public void DisplayName_IsProcessForCustom_ProductOtherwise()
    {
        Assert.Equal("mygame", new AntiCheatMatch("Custom", "mygame").DisplayName);
        Assert.Equal("Riot Vanguard", new AntiCheatMatch("Riot Vanguard", "vgc").DisplayName);
    }

    [Fact]
    public void Detect_IsCaseInsensitive_AndIgnoresExeSuffix()
    {
        Assert.NotNull(AntiCheatDetector.Detect(new[] { "VGC.EXE" }));
        Assert.NotNull(AntiCheatDetector.Detect(new[] { "easyanticheat" }));
    }

    [Fact]
    public void Detect_ReturnsNullWhenNothingMatches()
    {
        Assert.Null(AntiCheatDetector.Detect(new[] { "explorer", "chrome", "vgtray" }));
    }

    [Fact]
    public void Detect_VanguardTrayAloneIsNotEnough()
    {
        // vgtray runs whenever Vanguard is installed; only the vgc service means Vanguard is actively up.
        Assert.Null(AntiCheatDetector.Detect(new[] { "vgtray" }));
    }

    [Fact]
    public void Detect_HonoursUserSuppliedProcesses_NormalisingNames()
    {
        var match = AntiCheatDetector.Detect(new[] { "explorer", "MyGame" }, new[] { " mygame.exe ", "" });

        Assert.NotNull(match);
        Assert.Equal("Custom", match!.Product);
        Assert.Equal("MyGame", match.ProcessName);
    }

    [Fact]
    public void Detect_IgnoresBlankUserEntries()
    {
        Assert.Null(AntiCheatDetector.Detect(new[] { "explorer" }, new[] { "", "   " }));
    }

    [Fact]
    public void KnownProducts_ListsEveryBuiltInProduct()
    {
        var products = AntiCheatDetector.KnownProducts;

        Assert.Contains("Riot Vanguard", products);
        Assert.Contains("Easy Anti-Cheat", products);
        Assert.Contains("BattlEye", products);
    }
}
