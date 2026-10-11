namespace Torrid.App;

using Torrid.Core.Config;

public static class ColorPalette
{
    public static readonly (byte R, byte G, byte B) Blue = (0x5A, 0xA9, 0xFF);
    public static readonly (byte R, byte G, byte B) Green = (0x4E, 0xD3, 0x6B);
    public static readonly (byte R, byte G, byte B) Yellow = (0xFF, 0xC9, 0x3C);
    public static readonly (byte R, byte G, byte B) Red = (0xFF, 0x5C, 0x4D);
    public static readonly (byte R, byte G, byte B) Unknown = (0x8A, 0x94, 0xA6);

    public static (byte R, byte G, byte B) ForZone(ColorZone zone) => zone switch
    {
        ColorZone.Blue => Blue,
        ColorZone.Green => Green,
        ColorZone.Yellow => Yellow,
        ColorZone.Red => Red,
        _ => Unknown,
    };
}
