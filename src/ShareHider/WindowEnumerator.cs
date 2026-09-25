using System.Text;
using ShareHider.Core;

namespace ShareHider;

/// <summary>Lists visible top-level windows along with their owning process names.</summary>
internal static class WindowEnumerator
{
    private static readonly uint OwnProcessId = (uint)Environment.ProcessId;

    /// <summary>One pass over all visible top-level windows. Process names are cached for the pass only, so a reused PID can't return a stale name.</summary>
    public static List<WindowInfo> VisibleWindows()
    {
        var windows = new List<WindowInfo>();
        var processNames = new Dictionary<uint, string>();
        var text = new StringBuilder(512);

        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd))
            {
                return true;
            }

            Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == OwnProcessId)
            {
                return true;
            }

            if (!processNames.TryGetValue(pid, out var processName))
            {
                processName = ProcessName(pid);
                processNames[pid] = processName;
            }

            text.Clear();
            Native.GetClassNameW(hwnd, text, text.Capacity);
            var className = text.ToString();

            text.Clear();
            Native.GetWindowTextW(hwnd, text, text.Capacity);
            windows.Add(new WindowInfo(hwnd, processName, className, text.ToString()));
            return true;
        }, 0);

        return windows;
    }

    /// <summary>
    /// True when Windows would give this window a taskbar button: an unowned window
    /// that isn't a tool window, or any window that asks for one with WS_EX_APPWINDOW.
    /// </summary>
    public static bool HasTaskbarButton(nint hwnd)
    {
        var exStyle = (long)Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
        if ((exStyle & Native.WS_EX_APPWINDOW) != 0)
        {
            return true;
        }

        return (exStyle & Native.WS_EX_TOOLWINDOW) == 0 && Native.GetWindow(hwnd, Native.GW_OWNER) == 0;
    }

    /// <summary>Lower-case exe file name, or "" when the process can't be queried (e.g. protected processes).</summary>
    private static string ProcessName(uint pid)
    {
        var handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0)
        {
            return "";
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            return Native.QueryFullProcessImageNameW(handle, 0, buffer, ref size)
                ? Path.GetFileName(buffer.ToString()).ToLowerInvariant()
                : "";
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }
}
