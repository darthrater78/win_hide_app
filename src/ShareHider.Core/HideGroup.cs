namespace ShareHider.Core;

/// <summary>A named set of apps that are hidden together, by hand, by its own hotkey, or while sharing.</summary>
public sealed class HideGroup
{
    public const int MaxNameLength = 40;

    /// <summary>The name of the group settings start with, and the one an old auto-hide list moves into.</summary>
    public const string DefaultName = "While sharing";

    public string Name { get; set; } = "";

    /// <summary>Exe names, e.g. "outlook.exe".</summary>
    public List<string> Apps { get; set; } = [];

    /// <summary>Optional hotkey that hides or shows this group, e.g. "Ctrl+Alt+1". Null for none.</summary>
    public string? Hotkey { get; set; }

    /// <summary>Trims a group name and checks it is 1 to <see cref="MaxNameLength"/> printable characters.</summary>
    public static bool TryNormalizeName(string? input, out string name)
    {
        name = input?.Trim() ?? "";
        return name.Length is > 0 and <= MaxNameLength && !name.Any(char.IsControl);
    }
}
