using System.Diagnostics;
using ShareHider.Core;

namespace ShareHider;

/// <summary>
/// The tray icon and the app's main loop. Every tick polls for a share window and,
/// while hiding, re-hides the chosen apps' taskbar buttons. Window events trigger
/// an immediate re-hide so a newly opened window doesn't sit on the taskbar for a second.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private const int PollMs = 1000;
    private const int SharingEndsAfterPolls = 3;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShareHider", "settings.json");

    private readonly HideState _state = new(SharingEndsAfterPolls);
    private readonly TaskbarController _taskbar = new();
    private readonly HotkeyWindow _hotkey = new();
    private readonly NotifyIcon _icon = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = PollMs };
    private readonly SynchronizationContext _ui;
    private readonly List<nint> _eventHooks = [];

    // Kept in a field: the native hook holds only a function pointer, so the
    // delegate must not be garbage-collected while a hook is installed.
    private readonly Native.WinEventProc _winEventProc;

    private readonly ToolStripMenuItem _statusItem = new() { Enabled = false };
    private readonly ToolStripMenuItem _hideNowItem = new("&Hide apps now");
    private readonly ToolStripMenuItem _autoDetectItem = new("&Auto-detect screen sharing");
    private readonly ToolStripMenuItem _resumeAutoItem = new("&Resume automatic detection");

    private AppSettings _settings;
    private HashSet<string> _hiddenApps = [];
    private bool _rehidePending;
    private bool _hiding;
    private bool _exited;

    public TrayApp()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _winEventProc = OnWinEvent;

        _settings = AppSettings.Load(SettingsPath, out var loadError);
        if (loadError is not null)
        {
            Log.Write(loadError);
        }

        _state.AutoDetect = _settings.AutoDetect;
        _hiddenApps = [.. _settings.HiddenApps];

        _icon.ContextMenuStrip = BuildMenu();
        _icon.DoubleClick += (_, _) => ToggleManual();
        _hotkey.Pressed += (_, _) => ToggleManual();
        _timer.Tick += (_, _) => Tick();

        RegisterHotkey(showErrors: true);
        UpdateUi();
        _icon.Visible = true;
        _timer.Start();
        Log.Write($"started {AppInfo.Version}; hiding {_settings.HiddenApps.Count} app(s)");
    }

    private ContextMenuStrip BuildMenu()
    {
        _hideNowItem.Click += (_, _) => ToggleManual();
        _autoDetectItem.Click += (_, _) => SetAutoDetect(!_settings.AutoDetect);
        _resumeAutoItem.Click += (_, _) => { _state.ClearOverride(); Refresh(); };

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(
        [
            _statusItem,
            new ToolStripSeparator(),
            _hideNowItem,
            _autoDetectItem,
            _resumeAutoItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("&Settings…", null, (_, _) => ShowSettings()),
            new ToolStripMenuItem("Write &window list (diagnostics)", null, (_, _) => WriteWindowList()),
            new ToolStripMenuItem("Open log &folder", null, (_, _) => OpenLogFolder()),
            new ToolStripSeparator(),
            new ToolStripMenuItem($"ShareHider {AppInfo.Version}") { Enabled = false },
            new ToolStripMenuItem("E&xit", null, (_, _) => ExitThread()),
        ]);
        return menu;
    }

    private void Tick()
    {
        if (_settings.AutoDetect || _hiding)
        {
            var windows = WindowEnumerator.VisibleWindows();
            if (_settings.AutoDetect)
            {
                var share = FindShareWindow(windows);
                if (_state.ReportPoll(share is not null))
                {
                    Log.Write(share is not null ? $"sharing detected ({share})" : "sharing ended");
                }
            }

            Apply(windows);
        }

        UpdateUi();
    }

    /// <summary>Name of the first matching share signature, or null.</summary>
    private string? FindShareWindow(List<WindowInfo> windows)
    {
        foreach (var signature in ShareSignature.BuiltIn.Concat(_settings.CustomSignatures))
        {
            if (windows.Any(signature.Matches))
            {
                return signature.Name;
            }
        }

        return null;
    }

    /// <summary>Brings the taskbar in line with <see cref="HideState.ShouldHide"/>.</summary>
    private void Apply(List<WindowInfo>? windows = null)
    {
        if (_state.ShouldHide)
        {
            if (!_hiding)
            {
                _hiding = true;
                InstallEventHooks();
            }

            windows ??= WindowEnumerator.VisibleWindows();
            _taskbar.Hide(
                windows.Where(w => _hiddenApps.Contains(w.ProcessName) && WindowEnumerator.HasTaskbarButton(w.Handle)),
                _settings.MinimizeWindows);
        }
        else if (_hiding)
        {
            _hiding = false;
            RemoveEventHooks();
            _taskbar.RestoreAll();
        }
    }

    private void Refresh()
    {
        Apply();
        UpdateUi();
    }

    private void ToggleManual()
    {
        _state.Toggle();
        Log.Write($"manual toggle: {(_state.ShouldHide ? "hide" : "show")}");
        Refresh();
    }

    private void SetAutoDetect(bool enabled)
    {
        _settings.AutoDetect = enabled;
        _state.AutoDetect = enabled;
        SaveSettings();
        Refresh();
    }

    private void UpdateUi()
    {
        var status = _state.ShouldHide
            ? $"Hiding {_taskbar.HiddenCount} window(s)"
            : _settings.AutoDetect ? "Watching for screen sharing" : "Idle (auto-detect off)";
        if (_state.Override is not null)
        {
            status += " — manual";
        }
        else if (_state.SharingDetected)
        {
            status += " — sharing detected";
        }

        _statusItem.Text = status;
        _hideNowItem.Checked = _state.ShouldHide;
        _autoDetectItem.Checked = _settings.AutoDetect;
        _resumeAutoItem.Visible = _state.Override is not null;
        _icon.Icon = _state.ShouldHide ? SystemIcons.Shield : SystemIcons.Application;

        // NotifyIcon.Text is limited to 127 characters.
        var tooltip = $"ShareHider — {status}";
        _icon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;
    }

    private void RegisterHotkey(bool showErrors)
    {
        if (!Hotkey.TryParse(_settings.Hotkey, out var hotkey))
        {
            hotkey = Hotkey.Default;
        }

        if (!_hotkey.Register(hotkey))
        {
            Log.Write($"hotkey {hotkey} is already in use by another app");
            if (showErrors)
            {
                // Not a balloon: this only happens at startup or in Settings, never mid-share.
                MessageBox.Show(
                    $"The hotkey {hotkey} is already used by another app. Pick a different one in Settings.",
                    "ShareHider", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    private void ShowSettings()
    {
        using var form = new SettingsForm(_settings);
        if (form.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        _settings = form.Result;
        _hiddenApps = [.. _settings.HiddenApps];
        _state.AutoDetect = _settings.AutoDetect;
        SaveSettings();
        RegisterHotkey(showErrors: true);
        Log.Write($"settings saved; hiding {_settings.HiddenApps.Count} app(s)");

        // Give back only the apps taken off the list; the rest stay hidden mid-share.
        _taskbar.RestoreAppsNotIn(_hiddenApps);
        Refresh();
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Save(SettingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"could not save settings: {ex.Message}");
            MessageBox.Show($"Settings could not be saved: {ex.Message}", "ShareHider",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Writes every visible window to window-list.txt, so users can write a signature for an app
    /// we don't detect. Titles can be private (document names, email subjects), so they go to a
    /// separate file that is overwritten each time, never into the rolling log.
    /// </summary>
    private static void WriteWindowList()
    {
        var lines = WindowEnumerator.VisibleWindows()
            .Select(w => $"{w.ProcessName} | {w.ClassName} | {w.Title}")
            .Prepend("process | class | title");
        try
        {
            Directory.CreateDirectory(Log.Directory);
            File.WriteAllLines(Log.WindowListPath, lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"could not write window list: {ex.Message}");
        }

        OpenLogFolder();
    }

    private static void OpenLogFolder()
    {
        Directory.CreateDirectory(Log.Directory);
        // UseShellExecute on a directory path opens it in Explorer; the path is our own, not user input.
        Process.Start(new ProcessStartInfo(Log.Directory) { UseShellExecute = true })?.Dispose();
    }

    private void InstallEventHooks()
    {
        foreach (var (min, max) in new[]
                 {
                     (Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND),
                     (Native.EVENT_SYSTEM_MINIMIZESTART, Native.EVENT_SYSTEM_MINIMIZEEND),
                     (Native.EVENT_OBJECT_SHOW, Native.EVENT_OBJECT_SHOW),
                 })
        {
            var hook = Native.SetWinEventHook(min, max, 0, _winEventProc, 0, 0,
                Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
            if (hook != 0)
            {
                _eventHooks.Add(hook);
            }
        }
    }

    private void RemoveEventHooks()
    {
        foreach (var hook in _eventHooks)
        {
            Native.UnhookWinEvent(hook);
        }

        _eventHooks.Clear();
    }

    /// <summary>Runs on the UI thread (out-of-context hooks are delivered through its message loop). Coalesces bursts into one re-hide.</summary>
    private void OnWinEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != Native.OBJID_WINDOW || idChild != Native.CHILDID_SELF || _rehidePending)
        {
            return;
        }

        _rehidePending = true;
        _ui.Post(_ =>
        {
            _rehidePending = false;
            if (_hiding)
            {
                Apply();
            }
        }, null);
    }

    protected override void ExitThreadCore()
    {
        if (_exited)
        {
            return;
        }

        _exited = true;
        _timer.Stop();
        RemoveEventHooks();
        _taskbar.Dispose(); // restores every hidden button and window
        _hotkey.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        Log.Write("exited");
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }
}
