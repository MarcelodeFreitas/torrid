namespace Torrid.Core.Overlay;

public static class OverlayAppearance
{
    public const int MinTextSize = 10;
    public const int MaxTextSize = 48;
    public const int DefaultTextSize = 18;
    public const string DefaultColor = "#FFFFFF";

    public static IReadOnlyList<(string Name, string Hex)> Presets { get; } =
    [
        ("White", "#FFFFFF"),
        ("Green", "#4ED36B"),
        ("Cyan", "#4FD8EB"),
        ("Yellow", "#FFC93C"),
        ("Orange", "#FF9F43"),
        ("Red", "#FF5C4D"),
    ];

    public static int ClampTextSize(int size) => Math.Clamp(size, MinTextSize, MaxTextSize);

    public static bool IsValidColor(string? hex) =>
        hex is { Length: 7 } && hex[0] == '#' && hex[1..].All(Uri.IsHexDigit);
}
