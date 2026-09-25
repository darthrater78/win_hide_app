namespace ShareHider.Core;

/// <summary>
/// Describes a window that only exists while a screen share is live, such as a
/// meeting app's "you are sharing" toolbar. Windows has no API that reports an
/// active share, so detection matches these windows instead.
/// </summary>
public sealed class ShareSignature
{
    public string Name { get; set; } = "";

    /// <summary>Lower-case exe names the window may belong to.</summary>
    public List<string> ProcessNames { get; set; } = [];

    /// <summary>Case-insensitive substring of the window title, or null to ignore the title.</summary>
    public string? TitleContains { get; set; }

    /// <summary>Exact window class name, or null to ignore the class.</summary>
    public string? ClassName { get; set; }

    /// <summary>A signature needs a process and at least one window property, or it would match everything.</summary>
    public bool IsValid =>
        ProcessNames.Count > 0
        && ProcessNames.All(p => ExeName.TryNormalize(p, out var n) && n == p)
        && (TitleContains?.Length ?? 0) <= 256
        && (ClassName?.Length ?? 0) <= 256
        && (!string.IsNullOrWhiteSpace(TitleContains) || !string.IsNullOrWhiteSpace(ClassName));

    public bool Matches(WindowInfo window) =>
        ProcessNames.Contains(window.ProcessName)
        && (string.IsNullOrEmpty(TitleContains)
            || window.Title.Contains(TitleContains, StringComparison.OrdinalIgnoreCase))
        && (string.IsNullOrEmpty(ClassName)
            || string.Equals(window.ClassName, ClassName, StringComparison.Ordinal));

    private static readonly string[] Chromium = ["chrome.exe", "msedge.exe", "brave.exe", "vivaldi.exe", "opera.exe"];

    /// <summary>
    /// Signatures that ship with the app. These are best-effort: the meeting apps can
    /// rename their windows in any update. Users can add their own in settings.json,
    /// using the diagnostics window list to find the right title or class.
    /// </summary>
    public static IReadOnlyList<ShareSignature> BuiltIn { get; } =
    [
        // Chromium's "<site> is sharing your screen / a window / this tab" bar (Meet, Teams web, etc.).
        new() { Name = "Browser: sharing screen", ProcessNames = [.. Chromium], TitleContains = " is sharing your screen" },
        new() { Name = "Browser: sharing window", ProcessNames = [.. Chromium], TitleContains = " is sharing a window" },
        new() { Name = "Browser: sharing tab", ProcessNames = [.. Chromium], TitleContains = " is sharing this tab" },
        new() { Name = "Microsoft Teams", ProcessNames = ["ms-teams.exe", "teams.exe"], TitleContains = "Sharing control bar" },
        new() { Name = "Zoom", ProcessNames = ["zoom.exe"], TitleContains = "zoom share" },
    ];
}
