using System.Runtime.InteropServices;
using ShareHider.Core;

namespace ShareHider;

/// <summary>
/// Removes and restores other apps' taskbar buttons. Must be used from the STA UI thread.
/// </summary>
/// <remarks>
/// Explorer re-adds a button whenever it re-registers the window (Explorer restart, some
/// title or state changes), so <see cref="Hide"/> is meant to be called repeatedly while
/// hiding is on. Calling DeleteTab on an already-removed button is harmless.
/// </remarks>
internal sealed class TaskbarController : IDisposable
{
    private readonly Native.ITaskbarList _taskbar;

    /// <summary>Windows currently hidden: their process, and whether we minimized them.</summary>
    private readonly Dictionary<nint, (string Process, bool MinimizedByUs)> _hidden = [];

    public TaskbarController()
    {
        _taskbar = (Native.ITaskbarList)new Native.TaskbarList();
        _taskbar.HrInit();
    }

    public int HiddenCount => _hidden.Count;

    /// <summary>Hides the taskbar buttons of the given windows. New windows are minimized first when asked.</summary>
    public void Hide(IEnumerable<WindowInfo> windows, bool minimize)
    {
        foreach (var window in windows)
        {
            var hwnd = window.Handle;
            var minimizedByUs = _hidden.TryGetValue(hwnd, out var entry) && entry.MinimizedByUs;
            if (!_hidden.ContainsKey(hwnd))
            {
                // Minimize only the first time we see a window, so one the user
                // deliberately brings back mid-share stays where they put it.
                if (minimize && !Native.IsIconic(hwnd))
                {
                    minimizedByUs = Native.ShowWindowAsync(hwnd, Native.SW_MINIMIZE);
                }

                Log.Write($"hiding {window.ProcessName} window 0x{hwnd:X}{(minimizedByUs ? " (minimized)" : "")}");
            }

            try
            {
                _taskbar.DeleteTab(hwnd);
            }
            catch (COMException ex)
            {
                Log.Write($"DeleteTab failed for 0x{hwnd:X}: 0x{ex.HResult:X8}");
            }

            _hidden[hwnd] = (window.ProcessName, minimizedByUs);
        }

        // Forget windows that have closed, so a recycled handle is never "restored".
        foreach (var hwnd in _hidden.Keys.Where(h => !Native.IsWindow(h)).ToList())
        {
            _hidden.Remove(hwnd);
        }
    }

    /// <summary>Puts every hidden button back and un-minimizes the windows we minimized.</summary>
    public void RestoreAll() => RestoreWhere(_ => true);

    /// <summary>Restores only the windows of apps no longer in <paramref name="stillHidden"/>, e.g. after a settings change mid-share.</summary>
    public void RestoreAppsNotIn(IReadOnlySet<string> stillHidden) => RestoreWhere(p => !stillHidden.Contains(p));

    private void RestoreWhere(Func<string, bool> shouldRestore)
    {
        var restore = _hidden.Where(e => shouldRestore(e.Value.Process)).ToList();
        foreach (var (hwnd, (_, minimizedByUs)) in restore)
        {
            _hidden.Remove(hwnd);
            if (Native.IsWindow(hwnd))
            {
                Restore(hwnd, minimizedByUs);
            }
        }

        if (restore.Count > 0)
        {
            Log.Write($"restored {restore.Count} window(s)");
        }
    }

    private void Restore(nint hwnd, bool minimizedByUs)
    {
        try
        {
            _taskbar.AddTab(hwnd);
        }
        catch (COMException ex)
        {
            Log.Write($"AddTab failed for 0x{hwnd:X}: 0x{ex.HResult:X8}");
        }

        if (minimizedByUs && Native.IsIconic(hwnd))
        {
            Native.ShowWindowAsync(hwnd, Native.SW_SHOWNOACTIVATE);
        }
    }

    public void Dispose()
    {
        RestoreAll();
        Marshal.ReleaseComObject(_taskbar);
    }
}
