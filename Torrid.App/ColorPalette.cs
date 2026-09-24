namespace Torrid.App;

using Torrid.Core.Config;

public static class ColorPalette
{
    public static readonly (byte R, byte G, byte B) Blue = (0x42, 0x9C, 0xF5);
    public static readonly (byte R, byte G, byte B) Green = (0x4C, 0xAF, 0x50);
    public static readonly (byte R, byte G, byte B) Yellow = (0xFF, 0xC1, 0x07);
    public static readonly (byte R, byte G, byte B) Red = (0xF4, 0x43, 0x36);
    public static readonly (byte R, byte G, byte B) Unknown = (0x80, 0x80, 0x80);

    public static (byte R, byte G, byte B) ForZone(ColorZone zone) => zone switch
    {
        ColorZone.Blue => Blue,
        ColorZone.Green => Green,
        ColorZone.Yellow => Yellow,
        ColorZone.Red => Red,
        _ => Unknown,
    };
}
