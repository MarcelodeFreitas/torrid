using Torrid.Core.Overlay;

namespace Torrid.Tests;

public class HotkeyTests
{
    [Fact]
    public void Default_FormatsAsCtrlAltO()
    {
        Assert.Equal("Ctrl+Alt+O", Hotkey.Default.Format());
    }

    [Fact]
    public void Format_OrdersModifiersCtrlAltShiftWin()
    {
        var hotkey = new Hotkey(HotkeyModifiers.Win | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Control, 0x78);

        Assert.Equal("Ctrl+Alt+Shift+Win+F9", hotkey.Format());
    }

    [Theory]
    [InlineData("Ctrl+Alt+O")]
    [InlineData("Shift+F12")]
    [InlineData("Ctrl+5")]
    [InlineData("Win+Alt+Z")]
    public void TryParse_RoundTripsFormat(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var parsed));
        Assert.True(Hotkey.TryParse(parsed.Format(), out var again));
        Assert.Equal(parsed, again);
    }

    [Fact]
    public void TryParse_IsCaseAndSpaceInsensitive()
    {
        Assert.True(Hotkey.TryParse(" ctrl + ALT + o ", out var parsed));

        Assert.Equal(Hotkey.Default, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("O")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+OO")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+O+Alt")]
    [InlineData("Foo+O")]
    [InlineData("Ctrl+;")]
    public void TryParse_Rejects(string? text)
    {
        Assert.False(Hotkey.TryParse(text, out _));
    }

    [Fact]
    public void IsValid_RequiresAModifierAndASupportedKey()
    {
        Assert.True(new Hotkey(HotkeyModifiers.Control, 0x4F).IsValid);
        Assert.True(new Hotkey(HotkeyModifiers.Control, 0x87).IsValid);
        Assert.False(new Hotkey(HotkeyModifiers.None, 0x4F).IsValid);
        Assert.False(new Hotkey(HotkeyModifiers.Control, 0x20).IsValid);
        Assert.False(new Hotkey(HotkeyModifiers.Control, 0x88).IsValid);
    }
}
