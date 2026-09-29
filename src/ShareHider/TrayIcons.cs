using System.Security;
using Microsoft.Win32;
using ShareHider.Core;

namespace ShareHider;

/// <summary>
/// Moves hidden apps' tray icons into the notification area's overflow (the ^ flyout) and
/// back, through the same per-icon switch as Settings (<see cref="TrayIconMemory"/>).
/// An icon moved there can still be seen by opening the flyout.
/// </summary>
internal sealed class TrayIcons
{
    private const string RootKey = @"Control Panel\NotifyIconSettings";
    private const string PromotedValue = "IsPromoted";

    private readonly TrayIconMemory _memory;

    public TrayIcons(string statePath)
    {
        _memory = TrayIconMemory.Load(statePath);
        if (_memory.Moved.Count > 0)
        {
            // An earlier run ended without putting them back (killed, or a crash).
            Log.Write($"putting back {_memory.Moved.Count} tray icon(s) left in the overflow by an earlier run");
            RestoreAll();
        }
    }

    /// <summary>Moves the tray icons of <paramref name="hiddenExes"/> into the overflow, and puts back those of every other app.</summary>
    public void Apply(IReadOnlySet<string> hiddenExes)
    {
        if (hiddenExes.Count == 0 && _memory.Moved.Count == 0)
        {
            return;
        }

        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(RootKey, writable: true);
            if (root is null)
            {
                return; // Not Windows 11, or no tray icons recorded yet.
            }

            var wanted = new HashSet<string>();
            if (hiddenExes.Count > 0)
            {
                foreach (var id in root.GetSubKeyNames().Where(TrayIconMemory.IsValidId))
                {
                    using var entry = root.OpenSubKey(id, writable: true);
                    var exe = hiddenExes.FirstOrDefault(e => TrayIconMemory.ExeMatches(entry?.GetValue("ExecutablePath") as string, e));
                    if (entry is null || exe is null)
                    {
                        continue;
                    }

                    wanted.Add(id);
                    MoveToOverflow(entry, id, exe);
                }
            }

            foreach (var id in _memory.Moved.Keys.Where(id => !wanted.Contains(id)).ToList())
            {
                PutBack(root, id);
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Write($"could not change tray icon settings: {ex.Message}");
        }
    }

    /// <summary>Puts back every icon this or an earlier run moved.</summary>
    public void RestoreAll() => Apply(new HashSet<string>());

    private void MoveToOverflow(RegistryKey entry, string id, string exe)
    {
        if (_memory.Moved.ContainsKey(id))
        {
            return;
        }

        var original = entry.GetValue(PromotedValue) as int?;
        if (original == 0)
        {
            return; // Already in the overflow: nothing to do, nothing to put back.
        }

        // Remember before changing, so a crash in between can't lose the original.
        _memory.Moved[id] = original;
        _memory.Save();
        entry.SetValue(PromotedValue, 0, RegistryValueKind.DWord);
        Log.Write($"moved {exe}'s tray icon into the overflow");
    }

    private void PutBack(RegistryKey root, string id)
    {
        using (var entry = root.OpenSubKey(id, writable: true))
        {
            // Gone (the app was uninstalled, or Windows pruned it): nothing to put back.
            if (entry is not null)
            {
                if (_memory.Moved[id] is { } original)
                {
                    entry.SetValue(PromotedValue, original, RegistryValueKind.DWord);
                }
                else
                {
                    entry.DeleteValue(PromotedValue, throwOnMissingValue: false);
                }

                Log.Write($"put back tray icon {id}");
            }
        }

        _memory.Moved.Remove(id);
        _memory.Save();
    }
}
