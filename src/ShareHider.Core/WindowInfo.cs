namespace ShareHider.Core;

/// <summary>A visible top-level window, as seen by one enumeration pass.</summary>
/// <param name="ProcessName">Lower-case exe file name, e.g. "chrome.exe". Empty when unreadable.</param>
/// <param name="ExePath">Full path of the exe, for its icon and display name. Empty when unreadable.</param>
public sealed record WindowInfo(nint Handle, string ProcessName, string ClassName, string Title, string ExePath = "");
