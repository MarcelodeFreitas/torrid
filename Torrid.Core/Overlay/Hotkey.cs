namespace Torrid.Core.Overlay;

using System.Globalization;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}

// Modifier values equal the Win32 MOD_* flags so they can be passed to RegisterHotKey as-is.
public readonly record struct Hotkey(HotkeyModifiers Modifiers, int VirtualKey)
{
    public static Hotkey Default { get; } = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4F);

    public bool IsValid => Modifiers != HotkeyModifiers.None && IsSupportedKey(VirtualKey);

    public static bool IsSupportedKey(int vk) =>
        vk is (>= 0x30 and <= 0x39) or (>= 0x41 and <= 0x5A) or (>= 0x70 and <= 0x87);

    public string Format()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(VirtualKey is >= 0x70 and <= 0x87 ? "F" + (VirtualKey - 0x6F) : ((char)VirtualKey).ToString());
        return string.Join('+', parts);
    }

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var tokens = text.Split('+').Select(token => token.Trim()).ToArray();
        var modifiers = HotkeyModifiers.None;
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            var modifier = ModifierOf(tokens[i]);
            if (modifier is null) return false;
            modifiers |= modifier.Value;
        }

        var key = KeyOf(tokens[^1]);
        if (key is null) return false;

        var candidate = new Hotkey(modifiers, key.Value);
        if (!candidate.IsValid) return false;
        hotkey = candidate;
        return true;
    }

    private static HotkeyModifiers? ModifierOf(string token) => token.ToLowerInvariant() switch
    {
        "ctrl" or "control" => HotkeyModifiers.Control,
        "alt" => HotkeyModifiers.Alt,
        "shift" => HotkeyModifiers.Shift,
        "win" => HotkeyModifiers.Win,
        _ => null,
    };

    private static int? KeyOf(string token)
    {
        var upper = token.ToUpperInvariant();
        int vk;
        if (upper.Length == 1) vk = upper[0];
        else if (upper.Length > 1 && upper[0] == 'F' && int.TryParse(upper.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var f)) vk = 0x6F + f;
        else return null;
        return IsSupportedKey(vk) ? vk : null;
    }
}
