using System.Text.Json;

namespace ShareHider.Core;

/// <summary>User settings, stored as JSON in %APPDATA%\ShareHider\settings.json.</summary>
public sealed class AppSettings
{
    public const int MaxHiddenApps = 200;
    public const int MaxCustomSignatures = 50;
    public const long MaxFileBytes = 256 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 16,
    };

    /// <summary>Exe names whose windows are hidden from the taskbar while sharing.</summary>
    public List<string> HiddenApps { get; set; } = [];

    public bool AutoDetect { get; set; } = true;

    /// <summary>Also minimize the hidden apps' windows, and restore them afterwards.</summary>
    public bool MinimizeWindows { get; set; } = true;

    public string Hotkey { get; set; } = Core.Hotkey.Default.ToString();

    /// <summary>Extra share-detection signatures, checked alongside <see cref="ShareSignature.BuiltIn"/>.</summary>
    public List<ShareSignature> CustomSignatures { get; set; } = [];

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
                return new AppSettings();
            }

            if (info.Length > MaxFileBytes)
            {
                error = $"settings file is larger than {MaxFileBytes / 1024} KB; using defaults";
                return new AppSettings();
            }

            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions);
            if (loaded is null)
            {
                error = "settings file is empty; using defaults";
                return new AppSettings();
            }

            return loaded.Normalized();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = $"settings file could not be read ({ex.GetType().Name}); using defaults";
            return new AppSettings();
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
        var apps = new List<string>();
        foreach (var app in HiddenApps ?? [])
        {
            if (ExeName.TryNormalize(app, out var name) && !apps.Contains(name) && apps.Count < MaxHiddenApps)
            {
                apps.Add(name);
            }
        }

        var signatures = (CustomSignatures ?? [])
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

        return new AppSettings
        {
            HiddenApps = apps,
            AutoDetect = AutoDetect,
            MinimizeWindows = MinimizeWindows,
            Hotkey = Core.Hotkey.TryParse(Hotkey, out var hotkey) ? hotkey.ToString() : Core.Hotkey.Default.ToString(),
            CustomSignatures = signatures,
        };
    }
}
