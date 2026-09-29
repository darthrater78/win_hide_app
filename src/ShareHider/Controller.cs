using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using ShareHider.Core;

namespace ShareHider;

/// <summary>
/// The app's engine and the main window's view model. Once a second it polls for a
/// share window, works out which apps should be hidden, applies that to the real
/// taskbar, and refreshes the mockup. While anything is hidden, window events trigger
/// an immediate re-hide so a new window never sits on the taskbar for a second.
/// </summary>
internal sealed class Controller : INotifyPropertyChanged, IDisposable
{
    private const int SharingEndsAfterPolls = 3;
    private const int MainHotkeyId = 1;
    private const int FirstGroupHotkeyId = 100;
    private static readonly TimeSpan PinnedReadInterval = TimeSpan.FromSeconds(5);

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShareHider", "settings.json");

    private readonly HideState _share = new(SharingEndsAfterPolls);
    private readonly TrayIcons _tray = new(Path.Combine(Log.Directory, "tray-icons.json"));
    private readonly HideSelection _selection;
    private readonly AppOrder _order = new();
    private readonly TaskbarController _taskbar = new();
    private readonly HotkeyWindow _hotkey = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly List<nint> _eventHooks = [];
    private readonly Dictionary<int, GroupView> _groupHotkeys = [];

    // Kept in a field: the native hook holds only a function pointer, so the
    // delegate must not be garbage-collected while a hook is installed.
    private readonly Native.WinEventProc _winEventProc;

    private AppSettings _settings;
    private bool _wasShareHiding;
    private string? _shareName;
    private string? _meetingName;
    private HashSet<string> _pinnedExes = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _pinnedReadFor = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _pinnedReadAt;
    private bool _refreshPending;
    private bool _disposed;

    public Controller()
    {
        _winEventProc = OnWinEvent;
        _settings = AppSettings.Load(SettingsPath, out var loadError);
        if (loadError is not null)
        {
            Log.Write(loadError);
        }

        foreach (var group in _settings.Groups)
        {
            var view = new GroupView(group.Name) { Hotkey = group.Hotkey };
            group.Apps.ForEach(view.AddApp);
            Groups.Add(view);
        }

        ActiveGroup = Groups.First(g => g.Name == _settings.ActiveGroup);
        ActiveGroup.IsActive = true;
        SelectedGroup = ActiveGroup;
        _selection = new HideSelection(ActiveGroup.Apps);
        _share.AutoDetect = _settings.AutoDetect;
        _hotkey.Pressed += OnHotkey;
        _timer.Tick += (_, _) => Refresh(pollForShare: true);
        HotkeyError = RegisterHotkey();
        RegisterGroupHotkeys();
        Refresh(pollForShare: true);
        _timer.Start();
        Log.Write($"started {AppInfo.Version}; {Groups.Count} group(s), {ActiveGroup.Apps.Count} app(s) in the active group");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Running apps, in taskbar-mockup order.</summary>
    public ObservableCollection<AppButton> Apps { get; } = [];

    /// <summary>Saved groups, in the user's order.</summary>
    public ObservableCollection<GroupView> Groups { get; } = [];

    /// <summary>The group hidden automatically while sharing.</summary>
    public GroupView ActiveGroup { get; private set; }

    /// <summary>The group being edited in the window. Never null: the picker briefly offers null while its list changes.</summary>
    public GroupView SelectedGroup
    {
        get;
        set
        {
            if (value is not null)
            {
                Set(ref field, value);
            }
        }
    }

    public string StatusTitle { get; private set => Set(ref field, value); } = "";

    public string StatusDetail { get; private set => Set(ref field, value); } = "";

    public bool IsHidingAny { get; private set => Set(ref field, value); }

    /// <summary>The auto-hide list is being applied (sharing detected, or the hotkey).</summary>
    public bool IsShareHiding { get; private set => Set(ref field, value); }

    /// <summary>Something overrides automatic behavior: the hotkey, or clicks in the mockup.</summary>
    public bool HasOverrides { get; private set => Set(ref field, value); }

    public string HotkeyText => _settings.Hotkey;

    public string? HotkeyError { get; private set => Set(ref field, value); }

    /// <summary>The meeting app in use while no share is detected, or null. Drives the "press the hotkey" reminder.</summary>
    public string? MeetingName { get; private set => Set(ref field, value); }

    public string? MeetingWarning { get; private set => Set(ref field, value); }

    /// <summary>Says which chosen apps are pinned (their icon stays when hidden), or null.</summary>
    public string? PinnedWarning { get; private set => Set(ref field, value); }

    public bool AutoDetect
    {
        get => _settings.AutoDetect;
        set
        {
            _settings.AutoDetect = value;
            _share.AutoDetect = value;
            SaveAndRefresh();
        }
    }

    public bool MinimizeWindows
    {
        get => _settings.MinimizeWindows;
        set
        {
            _settings.MinimizeWindows = value;
            SaveAndRefresh();
        }
    }

    public bool StartWithWindows
    {
        get => StartupRegistration.IsEnabled;
        set
        {
            try
            {
                StartupRegistration.Set(value);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                Log.Write($"could not change start-with-Windows: {ex.Message}");
            }

            OnPropertyChanged();
        }
    }

    /// <summary>The mockup's click: hide or show one app's taskbar button right now.</summary>
    public void ToggleApp(string exe)
    {
        _selection.Toggle(exe, _share.ShouldHide);
        Log.Write($"{exe}: {(_selection.IsHidden(exe, _share.ShouldHide) ? "hidden" : "shown")} on demand");
        Refresh(pollForShare: false);
    }

    public void SetInGroup(GroupView group, string exe, bool member)
    {
        if (member)
        {
            group.AddApp(exe);
        }
        else
        {
            group.Apps.Remove(exe);
        }

        GroupsChanged();
    }

    /// <summary>Adds a typed exe name to a group. Returns false when it isn't a valid exe name or the group is full.</summary>
    public bool AddToGroup(GroupView group, string input)
    {
        if (!ExeName.TryNormalize(input, out var exe) || group.Apps.Count >= AppSettings.MaxHiddenApps)
        {
            return false;
        }

        SetInGroup(group, exe, true);
        return true;
    }

    /// <summary>Adds an empty group with an unused name and selects it. False when the group limit is reached.</summary>
    public bool NewGroup()
    {
        if (Groups.Count >= AppSettings.MaxGroups)
        {
            return false;
        }

        var number = Groups.Count + 1;
        while (FindGroup($"Group {number}") is not null)
        {
            number++;
        }

        var group = new GroupView($"Group {number}");
        Groups.Add(group);
        SelectedGroup = group;
        GroupsChanged();
        return true;
    }

    /// <summary>Renames a group. Returns an error message, or null.</summary>
    public string? RenameGroup(GroupView group, string input)
    {
        if (!HideGroup.TryNormalizeName(input, out var name))
        {
            return $"Use 1 to {HideGroup.MaxNameLength} characters.";
        }

        if (FindGroup(name) is { } other && other != group)
        {
            return $"There is already a group called {name}.";
        }

        group.Name = name;
        GroupsChanged();
        return null;
    }

    /// <summary>Deletes a group. The last group can't be deleted; deleting the active one makes the first group active.</summary>
    public void DeleteGroup(GroupView group)
    {
        if (Groups.Count <= 1)
        {
            return;
        }

        var remaining = Groups.First(g => g != group);
        if (group == ActiveGroup)
        {
            ActiveGroup = remaining;
            remaining.IsActive = true;
            Log.Write($"active group is now {remaining.Name}");
        }

        // Move the selection off the group first, so the picker never shows a deleted group.
        SelectedGroup = ActiveGroup;
        Groups.Remove(group);
        GroupsChanged();
    }

    public void SetActiveGroup(GroupView group)
    {
        ActiveGroup.IsActive = false;
        ActiveGroup = group;
        group.IsActive = true;
        Log.Write($"active group is now {group.Name}");
        GroupsChanged();
    }

    /// <summary>Sets or clears (empty text) a group's hotkey. Returns an error message, or null.</summary>
    public string? SetGroupHotkey(GroupView group, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            group.Hotkey = null;
        }
        else if (!Hotkey.TryParse(text, out var hotkey))
        {
            return "Use Ctrl, Alt or Win plus a letter, digit or F1–F24, e.g. Ctrl+Alt+1.";
        }
        else if (HotkeyOwner(hotkey) is { } owner && owner != group.Name)
        {
            return $"{hotkey} is already the hotkey for {owner}.";
        }
        else
        {
            group.Hotkey = hotkey.ToString();
        }

        GroupsChanged();
        return group.HotkeyError;
    }

    /// <summary>A group's manual trigger (window, tray menu, its hotkey): hide all its apps, or show them if all are hidden.</summary>
    public void ToggleGroup(GroupView group)
    {
        _selection.ToggleGroup(group.Apps, _share.ShouldHide);
        Log.Write($"group {group.Name}: {(_selection.AllHidden(group.Apps, _share.ShouldHide) ? "hidden" : "shown")} on demand");
        Refresh(pollForShare: false);
    }

    /// <summary>The main hotkey: hide or show the active group now, whatever detection says.</summary>
    public void ToggleShareHiding()
    {
        _share.Toggle();
        Log.Write($"hotkey: active group {(_share.ShouldHide ? "hidden" : "shown")}");
        Refresh(pollForShare: false);
    }

    /// <summary>Drops the hotkey override and every click override: back to fully automatic.</summary>
    public void ResumeAutomatic()
    {
        _share.ClearOverride();
        _selection.ClearOverrides();
        Refresh(pollForShare: false);
    }

    /// <summary>Validates, registers and saves a new hotkey. Returns an error message, or null.</summary>
    public string? ApplyHotkey(string text)
    {
        if (!Hotkey.TryParse(text, out var hotkey))
        {
            return "Use Ctrl, Alt or Win plus a letter, digit or F1–F24, e.g. Ctrl+Alt+H.";
        }

        if (Groups.FirstOrDefault(g => g.Hotkey == hotkey.ToString()) is { } group)
        {
            return $"{hotkey} is already the hotkey for {group.Name}.";
        }

        _settings.Hotkey = hotkey.ToString();
        HotkeyError = RegisterHotkey();
        Save();
        OnPropertyChanged(nameof(HotkeyText));
        return HotkeyError;
    }

    /// <summary>
    /// Saves every visible window and the taskbar's buttons to window-list.txt, so users
    /// can write a detection signature for a meeting app we don't recognize. Titles can be private (document
    /// names, email subjects), so they go to a separate file that is overwritten each
    /// time, never into the rolling log.
    /// </summary>
    public static void WriteWindowList()
    {
        var lines = WindowEnumerator.VisibleWindows()
            .Select(w => $"{w.ProcessName} | {w.ClassName} | {w.Title}")
            .Prepend("process | class | title")
            .Append("")
            .Append("taskbar button | automation id | class")
            .Concat(TaskbarButtons.Read().Select(b => $"{b.Name} | {b.AutomationId} | {b.ClassName}"));
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

    public static void OpenLogFolder()
    {
        Directory.CreateDirectory(Log.Directory);
        Open(Log.Directory);
    }

    /// <summary>Opens one of our own fixed locations (log folder, https links). Never called with user input.</summary>
    public static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception ex)
        {
            Log.Write($"could not open {target}: {ex.Message}");
        }
    }

    private void Refresh(bool pollForShare)
    {
        var windows = WindowEnumerator.VisibleWindows();
        if (pollForShare && _settings.AutoDetect)
        {
            var share = FirstMatch(ShareSignature.BuiltIn.Concat(_settings.CustomSignatures), windows);
            var changed = _share.ReportPoll(share is not null);

            // Keep the last name through the few polls it takes to decide the share has ended.
            _shareName = share ?? (_share.SharingDetected ? _shareName : null);
            if (changed)
            {
                Log.Write(_share.SharingDetected ? $"sharing detected ({_shareName})" : "sharing ended");
            }
        }

        if (pollForShare)
        {
            var meeting = FirstMatch(ShareSignature.BuiltInMeetings.Concat(_settings.CustomMeetingSignatures), windows);
            if (meeting != _meetingName)
            {
                Log.Write(meeting is null ? "meeting window closed" : $"meeting window seen ({meeting})");
                _meetingName = meeting;
            }
        }

        var shareHiding = _share.ShouldHide;
        if (_wasShareHiding && !shareHiding)
        {
            _selection.ShareEnded();
        }

        _wasShareHiding = shareHiding;
        var hidden = _selection.HiddenApps(shareHiding);
        ApplyToTaskbar(windows, hidden);
        UpdateApps(windows, hidden);
        foreach (var group in Groups)
        {
            group.IsHidden = _selection.AllHidden(group.Apps, shareHiding);
        }

        UpdateStatus(hidden.Count);
    }

    private void ApplyToTaskbar(List<WindowInfo> windows, HashSet<string> hidden)
    {
        _taskbar.RestoreAppsNotIn(hidden);
        _taskbar.Hide(
            windows.Where(w => hidden.Contains(w.ProcessName) && WindowEnumerator.HasTaskbarButton(w)),
            _settings.MinimizeWindows);
        _tray.Apply(hidden);

        if (hidden.Count > 0 && _eventHooks.Count == 0)
        {
            InstallEventHooks();
        }
        else if (hidden.Count == 0 && _eventHooks.Count > 0)
        {
            RemoveEventHooks();
        }
    }

    /// <summary>Brings the mockup's buttons in line with the running apps, reusing existing buttons.</summary>
    private void UpdateApps(List<WindowInfo> windows, HashSet<string> hidden)
    {
        var groups = windows.Where(WindowEnumerator.IsShownOnTaskbar)
            .GroupBy(w => w.ProcessName)
            .ToDictionary(g => g.Key, g => g.ToList());
        var order = _order.Update(groups.Keys);
        var existing = Apps.ToDictionary(a => a.ExeName);
        RefreshPinned(groups);

        for (var i = 0; i < order.Count; i++)
        {
            var exe = order[i];
            if (!existing.TryGetValue(exe, out var button))
            {
                button = new AppButton(exe);
                Apps.Insert(i, button);
            }
            else if (Apps.IndexOf(button) != i)
            {
                Apps.Move(Apps.IndexOf(button), i);
            }

            var group = groups[exe];
            var (icon, name) = AppIcons.For(group[0].ExePath, exe);
            button.Update(name, icon, group.Count, hidden.Contains(exe), _selection.IsAutoHide(exe), _pinnedExes.Contains(exe));
        }

        while (Apps.Count > order.Count)
        {
            Apps.RemoveAt(Apps.Count - 1);
        }

        // Chosen = hidden now, or in any group.
        PinnedWarning = PinnedNotice.Text(Apps
            .Where(a => a.IsPinned && (a.IsHidden || Groups.Any(g => g.Apps.Contains(a.ExeName))))
            .Select(a => a.DisplayName)
            .ToList());
    }

    /// <summary>
    /// Works out which running apps are pinned. That reads the taskbar through UI Automation,
    /// so it runs when the running apps change, and otherwise every few seconds to catch a pin
    /// made or removed by hand, not on every poll.
    /// </summary>
    private void RefreshPinned(Dictionary<string, List<WindowInfo>> groups)
    {
        if (groups.Keys.ToHashSet().SetEquals(_pinnedReadFor) && DateTime.UtcNow - _pinnedReadAt < PinnedReadInterval)
        {
            return;
        }

        _pinnedReadFor = new HashSet<string>(groups.Keys, StringComparer.OrdinalIgnoreCase);
        _pinnedReadAt = DateTime.UtcNow;
        var pinnedIds = TaskbarButtons.AppButtons().Where(b => b.Pinned).Select(b => b.AppId).ToList();
        var windows = groups.Values.SelectMany(w => w).ToList();
        var exeByHandle = windows.ToDictionary(w => w.Handle, w => w.ProcessName);
        _pinnedExes = AppIds.MatchButtons(windows, pinnedIds).Keys
            .Select(hwnd => exeByHandle[hwnd])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void UpdateStatus(int hiddenApps)
    {
        IsShareHiding = _share.ShouldHide;
        IsHidingAny = hiddenApps > 0;
        HasOverrides = _share.Override is not null || _selection.HasAnyOverride;

        StatusTitle = hiddenApps switch
        {
            0 => _settings.AutoDetect ? "Watching for screen sharing" : "Automatic detection is off",
            1 => "Hiding 1 app",
            _ => $"Hiding {hiddenApps} apps",
        };

        StatusDetail = (_share.Override, _share.SharingDetected) switch
        {
            (true, _) => $"The hotkey hid the {ActiveGroup.Name} group.",
            (false, true) => $"Sharing detected, but the hotkey showed the {ActiveGroup.Name} group.",
            (null, true) => $"Sharing detected ({_shareName ?? "share window"}). The {ActiveGroup.Name} group is hidden.",
            _ => "Click an app below to hide or show it now.",
        };

        // Detection can miss a share (a meeting app renames its windows in an update), so
        // during a meeting remind the user how to hide by hand. Nothing to remind about when
        // the list is already applied or empty.
        MeetingName = _meetingName is not null && !_share.SharingDetected && !_share.ShouldHide && _selection.AutoHide.Count > 0
            ? _meetingName
            : null;
        MeetingWarning = MeetingName is null
            ? null
            : $"You're in a {MeetingName} meeting. If you share your screen and your apps aren't hidden, press {HotkeyText} or click Hide now.";
    }

    /// <summary>Name of the first signature that matches any window, or null.</summary>
    private static string? FirstMatch(IEnumerable<ShareSignature> signatures, List<WindowInfo> windows) =>
        signatures.FirstOrDefault(signature => windows.Any(signature.Matches))?.Name;

    private GroupView? FindGroup(string name) =>
        Groups.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Who already uses a hotkey: "the main hotkey", a group's name, or null.</summary>
    private string? HotkeyOwner(Hotkey hotkey)
    {
        var text = hotkey.ToString();
        if (text == _settings.Hotkey)
        {
            return "hiding the active group";
        }

        return Groups.FirstOrDefault(g => g.Hotkey == text)?.Name;
    }

    /// <summary>After any group edit: re-sync the auto-hide list and hotkeys, save, and refresh.</summary>
    private void GroupsChanged()
    {
        _selection.ReplaceAutoHide(ActiveGroup.Apps);
        RegisterGroupHotkeys();
        SaveAndRefresh();
    }

    private void OnHotkey(object? sender, int id)
    {
        if (id == MainHotkeyId)
        {
            ToggleShareHiding();
        }
        else if (_groupHotkeys.TryGetValue(id, out var group))
        {
            ToggleGroup(group);
        }
    }

    /// <summary>Re-registers every group's hotkey, recording which ones another app already owns.</summary>
    private void RegisterGroupHotkeys()
    {
        foreach (var id in _groupHotkeys.Keys)
        {
            _hotkey.Unregister(id);
        }

        _groupHotkeys.Clear();
        for (var i = 0; i < Groups.Count; i++)
        {
            var group = Groups[i];
            group.HotkeyError = null;
            if (!Hotkey.TryParse(group.Hotkey, out var hotkey))
            {
                continue;
            }

            var id = FirstGroupHotkeyId + i;
            if (_hotkey.Register(id, hotkey))
            {
                _groupHotkeys[id] = group;
            }
            else
            {
                Log.Write($"hotkey {hotkey} for group {group.Name} is already in use by another app");
                group.HotkeyError = $"{hotkey} is already used by another app. Pick a different hotkey.";
            }
        }
    }

    private void SaveAndRefresh()
    {
        Save();
        OnPropertyChanged(nameof(AutoDetect));
        OnPropertyChanged(nameof(MinimizeWindows));
        Refresh(pollForShare: false);
    }

    private void Save()
    {
        _settings.Groups = Groups
            .Select(g => new HideGroup { Name = g.Name, Apps = [.. g.Apps], Hotkey = g.Hotkey })
            .ToList();
        _settings.ActiveGroup = ActiveGroup.Name;
        try
        {
            _settings.Save(SettingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"could not save settings: {ex.Message}");
        }
    }

    /// <summary>Registers the configured hotkey. Returns an error message, or null.</summary>
    private string? RegisterHotkey()
    {
        var hotkey = Hotkey.TryParse(_settings.Hotkey, out var parsed) ? parsed : Hotkey.Default;
        if (_hotkey.Register(MainHotkeyId, hotkey))
        {
            return null;
        }

        Log.Write($"hotkey {hotkey} is already in use by another app");
        return $"{hotkey} is already used by another app. Pick a different hotkey.";
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

    /// <summary>Delivered on the UI thread (out-of-context hooks go through its message loop). Coalesces bursts into one refresh.</summary>
    private void OnWinEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != Native.OBJID_WINDOW || idChild != Native.CHILDID_SELF || _refreshPending)
        {
            return;
        }

        _refreshPending = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            _refreshPending = false;
            if (!_disposed)
            {
                Refresh(pollForShare: false);
            }
        });
    }

    /// <summary>For a normal exit: restores every hidden button in its original place before <see cref="Dispose"/>.</summary>
    public void RestoreInPlace() => _taskbar.RestoreAll(inPlace: true);

    /// <summary>Restores every hidden button and window. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        RemoveEventHooks();
        _taskbar.Dispose();
        _tray.RestoreAll();
        _hotkey.Dispose();
        Log.Write("exited");
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            OnPropertyChanged(name);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
