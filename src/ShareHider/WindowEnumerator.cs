using System.Text;
using ShareHider.Core;

namespace ShareHider;

/// <summary>Lists visible top-level windows and decides which ones the taskbar shows.</summary>
internal static class WindowEnumerator
{
    private static readonly uint OwnProcessId = (uint)Environment.ProcessId;

    // The desktop and the taskbars themselves are visible, unowned windows with no button.
    private static readonly HashSet<string> ShellClasses =
        ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    private const string FrameHostExe = "applicationframehost.exe";

    /// <summary>
    /// One pass over all visible top-level windows. Process details are cached for the
    /// pass only, so a reused PID can never return a stale name. ShareHider's own windows
    /// are left out unless <paramref name="includeOwn"/>: it must never hide itself.
    /// </summary>
    public static List<WindowInfo> VisibleWindows(bool includeOwn = false)
    {
        var windows = new List<WindowInfo>();
        var processes = new Dictionary<uint, (string Name, string Path)>();
        var text = new StringBuilder(512);

        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd))
            {
                return true;
            }

            Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == OwnProcessId && !includeOwn)
            {
                return true;
            }

            var process = Lookup(processes, pid);
            text.Clear();
            Native.GetClassNameW(hwnd, text, text.Capacity);
            var className = text.ToString();

            // Store apps run inside a frame-host window; the app itself owns a child window.
            if (process.Name == FrameHostExe && FrameChildProcess(hwnd, pid) is { } appPid)
            {
                process = Lookup(processes, appPid);
            }

            text.Clear();
            Native.GetWindowTextW(hwnd, text, text.Capacity);
            windows.Add(new WindowInfo(hwnd, process.Name, className, text.ToString(), process.Path));
            return true;
        }, 0);

        return windows;
    }

    /// <summary>
    /// True when Windows gives this window a taskbar button: an unowned window that isn't a
    /// tool or no-activate window, or any window that asks for one with WS_EX_APPWINDOW.
    /// Hidden windows still count, since removing the button doesn't change their styles.
    /// </summary>
    public static bool HasTaskbarButton(WindowInfo window)
    {
        if (window.ProcessName.Length == 0 || ShellClasses.Contains(window.ClassName))
        {
            return false;
        }

        var exStyle = (long)Native.GetWindowLongPtr(window.Handle, Native.GWL_EXSTYLE);
        if ((exStyle & Native.WS_EX_APPWINDOW) != 0)
        {
            return true;
        }

        return (exStyle & (Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE)) == 0
            && Native.GetWindow(window.Handle, Native.GW_OWNER) == 0;
    }

    /// <summary>
    /// Windows to show in the taskbar mockup: those with a button, a title, and not cloaked.
    /// Cloaked windows are suspended Store apps and windows on other virtual desktops,
    /// neither of which has a button on this taskbar.
    /// </summary>
    public static bool IsShownOnTaskbar(WindowInfo window) =>
        window.Title.Length > 0 && HasTaskbarButton(window) && !IsCloaked(window.Handle);

    private static bool IsCloaked(nint hwnd) =>
        Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0
        && cloaked != 0;

    private static (string Name, string Path) Lookup(Dictionary<uint, (string, string)> cache, uint pid)
    {
        if (!cache.TryGetValue(pid, out var process))
        {
            var path = ProcessPath(pid);
            process = (path.Length > 0 ? Path.GetFileName(path).ToLowerInvariant() : "", path);
            cache[pid] = process;
        }

        return process;
    }

    /// <summary>The process owning a frame host's app window, or null for an empty frame.</summary>
    private static uint? FrameChildProcess(nint frame, uint framePid)
    {
        uint? found = null;
        Native.EnumChildWindows(frame, (child, _) =>
        {
            Native.GetWindowThreadProcessId(child, out var pid);
            if (pid != framePid)
            {
                found = pid;
                return false;
            }

            return true;
        }, 0);
        return found;
    }

    /// <summary>Full exe path, or "" when the process can't be queried (e.g. protected processes).</summary>
    private static string ProcessPath(uint pid)
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
            return Native.QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString() : "";
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }
}
