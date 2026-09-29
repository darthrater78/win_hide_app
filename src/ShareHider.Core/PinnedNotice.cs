namespace ShareHider.Core;

/// <summary>
/// The warning shown when an app chosen for hiding is pinned to the taskbar. Hiding removes
/// an app's window buttons, but a pin is a shortcut, not a window, so its icon stays.
/// </summary>
public static class PinnedNotice
{
    /// <summary>The warning for these app names, or null when there are none.</summary>
    public static string? Text(IReadOnlyList<string> names)
    {
        var distinct = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count == 0)
        {
            return null;
        }

        var list = distinct.Count == 1
            ? distinct[0]
            : $"{string.Join(", ", distinct[..^1])} and {distinct[^1]}";
        var verb = distinct.Count == 1 ? "is" : "are";
        return $"{list} {verb} pinned to your taskbar. Hiding a pinned app only hides that it's running: " +
            "its icon stays, just without the dash under it. To keep an app off the taskbar completely, " +
            "unpin it (right-click its taskbar icon, then Unpin from taskbar).";
    }
}
