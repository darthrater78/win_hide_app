using Microsoft.Win32;

namespace ShareHider;

/// <summary>"Start with Windows", via the per-user Run key (no admin rights needed).</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ShareHider";

    // Quoted, so a path with spaces can't be split into a different program plus arguments.
    private static string Command => $"\"{Environment.ProcessPath}\"";

    /// <summary>True only when the Run entry points at this exe, not an older copy elsewhere.</summary>
    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value
                && string.Equals(value, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool enabled)
    {
        if (enabled && string.IsNullOrEmpty(Environment.ProcessPath))
        {
            throw new IOException("the path of the running exe is unknown");
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, Command);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
