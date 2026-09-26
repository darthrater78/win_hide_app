using System.Runtime.InteropServices;
using System.Windows.Threading;
using ShareHider.Core;

namespace ShareHider;

/// <summary>
/// Removes and restores other apps' taskbar buttons. Must be used from the STA UI thread.
/// </summary>
/// <remarks>
/// Explorer re-adds a button whenever it re-registers the window (Explorer restart, some
/// title or state changes), so <see cref="Hide"/> is meant to be called repeatedly while
/// hiding is on. Calling DeleteTab on an already-removed button is harmless.
///
/// Re-adding a button puts it at the end of the taskbar. To put it back where it was,
/// the taskbar's order is read (UI Automation) before hiding, and after a restore the
/// buttons that belong after it are cycled to the end in order (<see cref="TaskbarOrder"/>).
/// </remarks>
internal sealed class TaskbarController : IDisposable
{
    // Explorer applies taskbar changes asynchronously, so moves sent back to back can land
    // out of order (CI showed it). Moves go out one per tick, after the re-adds settle.
    private static readonly TimeSpan MoveInterval = TimeSpan.FromMilliseconds(120);

    private readonly Native.ITaskbarList _taskbar;
    private readonly Queue<nint> _pendingMoves = [];
    private readonly DispatcherTimer _moveTimer = new() { Interval = MoveInterval };

    /// <summary>Windows currently hidden: their process, whether we minimized them, and their taskbar app id if known.</summary>
    private readonly Dictionary<nint, (string Process, bool MinimizedByUs, string? AppId)> _hidden = [];

    /// <summary>The taskbar's app ids, left to right, including hidden apps at their old places.</summary>
    private List<string> _order = [];

    public TaskbarController()
    {
        _moveTimer.Tick += (_, _) => MoveNext();
        _taskbar = (Native.ITaskbarList)new Native.TaskbarList();
        _taskbar.HrInit();
    }

    public int HiddenCount => _hidden.Count;

    /// <summary>Hides the taskbar buttons of the given windows. New windows are minimized first when asked.</summary>
    public void Hide(IEnumerable<WindowInfo> windows, bool minimize)
    {
        var list = windows.ToList();
        var appIds = RememberOrder(list.Where(w => !_hidden.ContainsKey(w.Handle)).ToList());
        foreach (var window in list)
        {
            var hwnd = window.Handle;
            var minimizedByUs = _hidden.TryGetValue(hwnd, out var entry) && entry.MinimizedByUs;
            var appId = entry.AppId ?? appIds.GetValueOrDefault(hwnd);
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

            _hidden[hwnd] = (window.ProcessName, minimizedByUs, appId);
        }

        // Forget windows that have closed, so a recycled handle is never "restored".
        foreach (var hwnd in _hidden.Keys.Where(h => !Native.IsWindow(h)).ToList())
        {
            _hidden.Remove(hwnd);
        }
    }

    /// <summary>
    /// Puts every hidden button back and un-minimizes the windows we minimized. With
    /// <paramref name="inPlace"/> false the buttons just go on the end, which needs no
    /// UI Automation: the path for logoff and unexpected errors, where Explorer may not answer.
    /// </summary>
    public void RestoreAll(bool inPlace)
    {
        RestoreWhere(_ => true, inPlace);
        if (inPlace)
        {
            // Exiting: the timer won't run again, so make the queued moves now, same pacing.
            _moveTimer.Stop();
            while (_pendingMoves.Count > 0)
            {
                Thread.Sleep(MoveInterval);
                MoveNext();
            }
        }
    }

    /// <summary>Restores only the windows of apps no longer in <paramref name="stillHidden"/>, e.g. after a settings change mid-share.</summary>
    public void RestoreAppsNotIn(IReadOnlySet<string> stillHidden) => RestoreWhere(p => !stillHidden.Contains(p), inPlace: true);

    private void RestoreWhere(Func<string, bool> shouldRestore, bool inPlace)
    {
        var restore = _hidden.Where(e => shouldRestore(e.Value.Process)).ToList();
        if (restore.Count == 0)
        {
            return;
        }

        // A new restore replans from the taskbar as it is now.
        _pendingMoves.Clear();
        _moveTimer.Stop();

        var observed = inPlace && _order.Count > 0 ? TaskbarButtons.AppIds() : [];

        // Re-add in remembered order, so apps restored together are already right among themselves.
        var ordered = restore.OrderBy(e => e.Value.AppId is { } id && _order.IndexOf(id) is >= 0 and var i ? i : int.MaxValue).ToList();
        foreach (var (hwnd, (_, minimizedByUs, _)) in ordered)
        {
            _hidden.Remove(hwnd);
            if (Native.IsWindow(hwnd))
            {
                Restore(hwnd, minimizedByUs);
            }
        }

        Log.Write($"restored {restore.Count} window(s)");
        if (observed.Count > 0)
        {
            var restoredIds = ordered.Select(e => e.Value.AppId).OfType<string>().Distinct().ToList();
            MoveBackIntoPlace(observed, restoredIds);
        }
    }

    /// <summary>
    /// Reads the taskbar's order before new windows are hidden, keeping the places of apps
    /// hidden earlier, and returns the app id of each new window.
    /// </summary>
    private Dictionary<nint, string> RememberOrder(List<WindowInfo> newWindows)
    {
        if (newWindows.Count == 0)
        {
            return [];
        }

        var observed = TaskbarButtons.AppIds();
        if (observed.Count == 0)
        {
            return [];
        }

        _order = TaskbarOrder.Merge(_order, observed, HiddenAppIds());
        return AppIds.MatchButtons(newWindows, observed);
    }

    /// <summary>
    /// Restored buttons were added at the end. Cycles the buttons that belong after them,
    /// in order, so everything is back where it was. Buttons with no window we can reach
    /// (pinned apps that aren't running, apps whose id we can't read) stay put.
    /// </summary>
    private void MoveBackIntoPlace(List<string> observed, List<string> restoredIds)
    {
        _order = TaskbarOrder.Merge(_order, observed, HiddenAppIds().Concat(restoredIds).ToHashSet());
        var current = observed.Concat(restoredIds).Distinct().ToList();
        // Our own window too: its button may sit after a restored one.
        var shown = WindowEnumerator.VisibleWindows(includeOwn: true)
            .Where(w => !_hidden.ContainsKey(w.Handle) && WindowEnumerator.HasTaskbarButton(w))
            .ToList();
        var windowsById = AppIds.MatchButtons(shown, current)
            .GroupBy(pair => pair.Value, pair => pair.Key)
            .ToDictionary(g => g.Key, g => g.ToList());

        var plan = TaskbarOrder.Plan(
            _order.Where(windowsById.ContainsKey).ToList(),
            current.Where(windowsById.ContainsKey).ToList());
        foreach (var hwnd in plan.SelectMany(id => windowsById[id]))
        {
            _pendingMoves.Enqueue(hwnd);
        }

        if (_pendingMoves.Count > 0)
        {
            _moveTimer.Start(); // first move one interval from now, after the re-adds
        }

        if (plan.Count > 0)
        {
            Log.Write($"moved {plan.Count} app(s) back into place on the taskbar");
        }

        var unmovable = current.Where(id => !windowsById.ContainsKey(id)).ToList();
        if (unmovable.Count > 0)
        {
            Log.Write($"taskbar buttons with no window to move: {string.Join(", ", unmovable)}");
        }
    }

    private void MoveNext()
    {
        if (!_pendingMoves.TryDequeue(out var hwnd))
        {
            _moveTimer.Stop();
            return;
        }

        if (Native.IsWindow(hwnd) && !_hidden.ContainsKey(hwnd))
        {
            MoveToEnd(hwnd);
        }
    }

    /// <summary>Takes a window's button off the taskbar and adds it back, which puts it at the end.</summary>
    private void MoveToEnd(nint hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId)
        {
            // The taskbar ignores DeleteTab/AddTab on our own window (CI showed it staying put),
            // so hide and re-show it instead, keeping focus if it had it.
            var focused = Native.GetForegroundWindow() == hwnd;
            Native.ShowWindow(hwnd, Native.SW_HIDE);
            Native.ShowWindow(hwnd, focused ? Native.SW_SHOW : Native.SW_SHOWNA);
            return;
        }

        try
        {
            _taskbar.DeleteTab(hwnd);
            _taskbar.AddTab(hwnd);
        }
        catch (COMException ex)
        {
            Log.Write($"moving the button of 0x{hwnd:X} failed: 0x{ex.HResult:X8}");
        }
    }

    private HashSet<string> HiddenAppIds() => _hidden.Values.Select(v => v.AppId).OfType<string>().ToHashSet();

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
        _moveTimer.Stop();
        _pendingMoves.Clear();
        RestoreAll(inPlace: false);
        Marshal.ReleaseComObject(_taskbar);
    }
}
