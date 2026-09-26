using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShareHider.Core;

/// <summary>User settings, stored as JSON in %APPDATA%\ShareHider\settings.json.</summary>
public sealed class AppSettings
{
    public const int MaxHiddenApps = 200;
    public const int MaxGroups = 20;
    public const int MaxCustomSignatures = 50;
    public const long MaxFileBytes = 256 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 16,
    };

    /// <summary>
    /// The auto-hide list from before groups existed. Read only to move it into the
    /// first group; never written back.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? HiddenApps { get; set; }

    /// <summary>Saved groups of apps. Normalized settings always have at least one.</summary>
    public List<HideGroup> Groups { get; set; } = [];

    /// <summary>Name of the group hidden automatically while sharing.</summary>
    public string? ActiveGroup { get; set; }

    public bool AutoDetect { get; set; } = true;

    /// <summary>Also minimize the hidden apps' windows, and restore them afterwards.</summary>
    public bool MinimizeWindows { get; set; } = true;

    public string Hotkey { get; set; } = Core.Hotkey.Default.ToString();

    /// <summary>Extra share-detection signatures, checked alongside <see cref="ShareSignature.BuiltIn"/>.</summary>
    public List<ShareSignature> CustomSignatures { get; set; } = [];

    /// <summary>Extra meeting-window signatures, checked alongside <see cref="ShareSignature.BuiltInMeetings"/>.</summary>
    public List<ShareSignature> CustomMeetingSignatures { get; set; } = [];

    /// <summary>
    /// Loads settings, falling back to defaults when the file is missing, oversized or unreadable.
    /// Invalid entries are dropped rather than trusted.
    /// </summary>
    /// <param name="error">Why the file could not be used, or null when it loaded (or did not exist).</param>
    public static AppSettings Load(string path, out string? error)
    {
        error = null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return new AppSettings().Normalized();
            }

            if (info.Length > MaxFileBytes)
            {
                error = $"settings file is larger than {MaxFileBytes / 1024} KB; using defaults";
                return new AppSettings().Normalized();
            }

            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions);
            if (loaded is null)
            {
                error = "settings file is empty; using defaults";
                return new AppSettings().Normalized();
            }

            return loaded.Normalized();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = $"settings file could not be read ({ex.GetType().Name}); using defaults";
            return new AppSettings().Normalized();
        }
    }

    /// <summary>Writes the settings atomically: a temp file first, then a rename over the old one.</summary>
    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Normalized(), JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Returns a copy with invalid, duplicate and excess entries removed.</summary>
    public AppSettings Normalized()
    {
        var mainHotkey = Core.Hotkey.TryParse(Hotkey, out var hotkey) ? hotkey : Core.Hotkey.Default;
        var groups = ValidGroups(mainHotkey);
        var active = groups.FirstOrDefault(g => string.Equals(g.Name, ActiveGroup, StringComparison.OrdinalIgnoreCase))
            ?? groups[0];

        return new AppSettings
        {
            Groups = groups,
            ActiveGroup = active.Name,
            AutoDetect = AutoDetect,
            MinimizeWindows = MinimizeWindows,
            Hotkey = mainHotkey.ToString(),
            CustomSignatures = ValidSignatures(CustomSignatures),
            CustomMeetingSignatures = ValidSignatures(CustomMeetingSignatures),
        };
    }

    /// <summary>
    /// Valid, uniquely named groups with clean app lists. A hotkey that doesn't parse or
    /// is already taken (by the main hotkey or an earlier group) is dropped. An old
    /// auto-hide list becomes the first group when there are no groups yet.
    /// </summary>
    private List<HideGroup> ValidGroups(Hotkey mainHotkey)
    {
        var source = Groups is { Count: > 0 } ? Groups : [new HideGroup { Name = HideGroup.DefaultName, Apps = HiddenApps ?? [] }];
        var taken = new HashSet<Hotkey> { mainHotkey };
        var groups = new List<HideGroup>();
        foreach (var group in source)
        {
            if (group is null
                || !HideGroup.TryNormalizeName(group.Name, out var name)
                || groups.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            string? groupHotkey = null;
            if (Core.Hotkey.TryParse(group.Hotkey, out var parsed) && taken.Add(parsed))
            {
                groupHotkey = parsed.ToString();
            }

            groups.Add(new HideGroup { Name = name, Apps = ValidApps(group.Apps), Hotkey = groupHotkey });
            if (groups.Count == MaxGroups)
            {
                break;
            }
        }

        return groups.Count > 0 ? groups : [new HideGroup { Name = HideGroup.DefaultName }];
    }

    private static List<string> ValidApps(List<string>? input)
    {
        var apps = new List<string>();
        foreach (var app in input ?? [])
        {
            if (ExeName.TryNormalize(app, out var name) && !apps.Contains(name) && apps.Count < MaxHiddenApps)
            {
                apps.Add(name);
            }
        }

        return apps;
    }

    private static List<ShareSignature> ValidSignatures(List<ShareSignature>? signatures) =>
        (signatures ?? [])
            .Where(s => s is not null)
            .Select(s => new ShareSignature
            {
                Name = s.Name ?? "",
                ProcessNames = (s.ProcessNames ?? [])
                    .Select(p => ExeName.TryNormalize(p, out var n) ? n : null)
                    .OfType<string>()
                    .Distinct()
                    .ToList(),
                TitleContains = s.TitleContains,
                ClassName = s.ClassName,
            })
            .Where(s => s.IsValid)
            .Take(MaxCustomSignatures)
            .ToList();
}
