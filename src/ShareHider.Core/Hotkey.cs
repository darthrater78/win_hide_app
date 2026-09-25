namespace ShareHider.Core;

/// <summary>Matches the Win32 RegisterHotKey MOD_* values.</summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>A global hotkey such as "Ctrl+Alt+H".</summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, uint VirtualKey)
{
    public static readonly Hotkey Default = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'H');

    /// <summary>
    /// Parses "Ctrl+Alt+H", "Win+Shift+F9" and similar strings. Keys are A-Z, 0-9 or F1-F24.
    /// At least one of Ctrl, Alt or Win is required so the hotkey can't swallow normal typing.
    /// </summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 64)
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = HotkeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var modifier = ParseModifier(parts[i]);
            if (modifier == HotkeyModifiers.None || modifiers.HasFlag(modifier))
            {
                return false;
            }

            modifiers |= modifier;
        }

        if ((modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Win)) == 0
            || !TryParseKey(parts[^1], out var vk))
        {
            return false;
        }

        hotkey = new Hotkey(modifiers, vk);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(VirtualKey is >= 0x70 and <= 0x87 ? $"F{VirtualKey - 0x6F}" : ((char)VirtualKey).ToString());
        return string.Join('+', parts);
    }

    private static HotkeyModifiers ParseModifier(string part) => part.ToLowerInvariant() switch
    {
        "ctrl" or "control" => HotkeyModifiers.Control,
        "alt" => HotkeyModifiers.Alt,
        "shift" => HotkeyModifiers.Shift,
        "win" or "windows" => HotkeyModifiers.Win,
        _ => HotkeyModifiers.None,
    };

    private static bool TryParseKey(string part, out uint vk)
    {
        vk = 0;
        var key = part.ToUpperInvariant();
        if (key.Length == 1 && (char.IsAsciiLetterUpper(key[0]) || char.IsAsciiDigit(key[0])))
        {
            vk = key[0]; // VK codes for A-Z and 0-9 equal their ASCII values.
            return true;
        }

        if (key.Length is 2 or 3 && key[0] == 'F'
            && int.TryParse(key.AsSpan(1), System.Globalization.NumberStyles.None, null, out var n)
            && n is >= 1 and <= 24)
        {
            vk = (uint)(0x6F + n); // VK_F1 = 0x70
            return true;
        }

        return false;
    }
}
