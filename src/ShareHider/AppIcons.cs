using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShareHider;

/// <summary>Icons and friendly names for apps, looked up once per exe path.</summary>
internal static class AppIcons
{
    private const int MaxEntries = 256;
    private const int IconSize = 48;
    private static readonly Dictionary<string, (ImageSource? Icon, string Name)> Cache = [];

    /// <summary>The exe's icon (null when it has none we can read) and its product name.</summary>
    public static (ImageSource? Icon, string Name) For(string exePath, string exeName)
    {
        var key = exePath.Length > 0 ? exePath : exeName;
        if (Cache.TryGetValue(key, out var entry))
        {
            return entry;
        }

        if (Cache.Count >= MaxEntries)
        {
            Cache.Clear();
        }

        entry = (LoadIcon(exePath), DisplayName(exePath, exeName));
        Cache[key] = entry;
        return entry;
    }

    private static ImageSource? LoadIcon(string exePath)
    {
        if (exePath.Length == 0)
        {
            return null;
        }

        try
        {
            using var icon = System.Drawing.Icon.ExtractIcon(exePath, 0, IconSize)
                ?? System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon is null)
            {
                return null;
            }

            var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or Win32Exception)
        {
            return null;
        }
    }

    /// <summary>The exe's FileDescription ("Google Chrome"), falling back to its file name ("chrome").</summary>
    private static string DisplayName(string exePath, string exeName)
    {
        var fallback = Path.GetFileNameWithoutExtension(exeName);
        if (exePath.Length == 0)
        {
            return fallback;
        }

        try
        {
            var description = FileVersionInfo.GetVersionInfo(exePath).FileDescription?.Trim();
            return string.IsNullOrEmpty(description) ? fallback : description;
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            return fallback;
        }
    }
}
