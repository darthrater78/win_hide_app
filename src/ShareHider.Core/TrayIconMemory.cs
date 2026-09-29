using System.Text.Json;

namespace ShareHider.Core;

/// <summary>
/// The tray icons ShareHider moved into the overflow, with each one's original setting, kept
/// on disk so a restart after a crash can still put them back.
/// </summary>
/// <remarks>
/// Windows 11 keeps each tray icon's "show on taskbar" switch (Settings, Personalization,
/// Taskbar, Other system tray icons) in <c>HKCU\Control Panel\NotifyIconSettings\&lt;id&gt;</c>:
/// the icon's exe path in <c>ExecutablePath</c>, and <c>IsPromoted</c> 1 for shown or 0 for
/// the overflow. Explorer applies a change straight away. An entry's original value is null
/// when <c>IsPromoted</c> wasn't set, and restoring then removes it again.
/// </remarks>
public sealed class TrayIconMemory
{
    private const long MaxFileBytes = 64 * 1024;

    private readonly string _path;

    private TrayIconMemory(string path, Dictionary<string, int?> moved)
    {
        _path = path;
        Moved = moved;
    }

    /// <summary>Entry id to its original <c>IsPromoted</c> value (null: not set).</summary>
    public Dictionary<string, int?> Moved { get; }

    /// <summary>Loads what an earlier run moved, ignoring a missing, oversized or malformed file and invalid ids.</summary>
    public static TrayIconMemory Load(string path)
    {
        var moved = new Dictionary<string, int?>();
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length <= MaxFileBytes)
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, int?>>(File.ReadAllText(path)) ?? [];
                foreach (var (id, value) in loaded)
                {
                    if (IsValidId(id) && value is null or 0 or 1)
                    {
                        moved[id] = value;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Nothing trustworthy to restore; the icons stay as they are.
        }

        return new TrayIconMemory(path, moved);
    }

    /// <summary>Writes the file atomically, or deletes it when nothing is moved.</summary>
    public void Save()
    {
        if (Moved.Count == 0)
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }

            return;
        }

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Moved));
        File.Move(temp, _path, overwrite: true);
    }

    /// <summary>Entry ids are the decimal numbers Windows names the keys with; anything else is never touched.</summary>
    public static bool IsValidId(string id) => id.Length is > 0 and <= 20 && id.All(char.IsAsciiDigit);

    /// <summary>Whether an entry's exe path is this exe, by file name (apps like Slack move folders on every update).</summary>
    public static bool ExeMatches(string? executablePath, string exeName)
    {
        if (string.IsNullOrEmpty(executablePath))
        {
            return false;
        }

        var slash = executablePath.LastIndexOfAny(['\\', '/']);
        return string.Equals(executablePath[(slash + 1)..], exeName, StringComparison.OrdinalIgnoreCase);
    }
}
