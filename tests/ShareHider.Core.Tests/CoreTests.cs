using ShareHider.Core;

namespace ShareHider.Core.Tests;

public class ExeNameTests
{
    [Theory]
    [InlineData("chrome.exe", "chrome.exe")]
    [InlineData("  Outlook.EXE ", "outlook.exe")]
    [InlineData("ms-teams.exe", "ms-teams.exe")]
    public void Accepts_and_normalizes(string input, string expected)
    {
        Assert.True(ExeName.TryNormalize(input, out var name));
        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".exe")]
    [InlineData("chrome")]
    [InlineData("chrome.exe.lnk")]
    [InlineData(@"C:\Program Files\app.exe")]
    [InlineData("../app.exe")]
    [InlineData("*.exe")]
    [InlineData("a\u0001b.exe")]
    public void Rejects_invalid(string? input) => Assert.False(ExeName.TryNormalize(input, out _));

    [Fact]
    public void Rejects_overlong() => Assert.False(ExeName.TryNormalize(new string('a', 300) + ".exe", out _));
}

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+H", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x48u, "Ctrl+Alt+H")]
    [InlineData("alt + shift + 9", HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x39u, "Alt+Shift+9")]
    [InlineData("Win+F12", HotkeyModifiers.Win, 0x7Bu, "Win+F12")]
    [InlineData("Control+F1", HotkeyModifiers.Control, 0x70u, "Ctrl+F1")]
    public void Parses_and_round_trips(string text, HotkeyModifiers modifiers, uint vk, string formatted)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(modifiers, hotkey.Modifiers);
        Assert.Equal(vk, hotkey.VirtualKey);
        Assert.Equal(formatted, hotkey.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("H")]           // no modifier would swallow typing
    [InlineData("Shift+H")]     // Shift alone still swallows capitals
    [InlineData("Ctrl+Ctrl+H")]
    [InlineData("Ctrl+Hyper+H")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+Space")]
    [InlineData("Ctrl+")]
    public void Rejects_invalid(string? text) => Assert.False(Hotkey.TryParse(text, out _));
}

public class ShareSignatureTests
{
    private static WindowInfo Window(string process, string title, string className = "Chrome_WidgetWin_1") =>
        new(1, process, className, title);

    [Fact]
    public void Builtins_are_valid() => Assert.All(ShareSignature.BuiltIn, s => Assert.True(s.IsValid, s.Name));

    [Fact]
    public void Browser_share_bar_matches()
    {
        var window = Window("chrome.exe", "meet.google.com is sharing your screen.");
        Assert.Contains(ShareSignature.BuiltIn, s => s.Matches(window));
    }

    [Fact]
    public void Same_title_in_other_process_does_not_match()
    {
        var window = Window("notepad.exe", "meet.google.com is sharing your screen.");
        Assert.DoesNotContain(ShareSignature.BuiltIn, s => s.Matches(window));
    }

    [Fact]
    public void Ordinary_browser_window_does_not_match()
    {
        var window = Window("chrome.exe", "Inbox - Google Chrome");
        Assert.DoesNotContain(ShareSignature.BuiltIn, s => s.Matches(window));
    }

    [Fact]
    public void Builtin_meetings_are_valid() => Assert.All(ShareSignature.BuiltInMeetings, s => Assert.True(s.IsValid, s.Name));

    // Class names from a real Zoom share (window list saved during the share).
    [Fact]
    public void Zoom_share_toolbar_matches()
    {
        var window = Window("zoom.exe", "Screen sharing meeting controls", "ZPFloatToolbarClass");
        Assert.Contains(ShareSignature.BuiltIn, s => s.Matches(window));
    }

    [Theory]
    [InlineData("Zoom Workplace", "ZPPTMainFrmWndClassEx")]
    [InlineData("Zoom Meeting", "ZPContentViewWndClass")]
    [InlineData("Annotation - Zoom", "ZoomAnnoWindowWndClass")]
    public void Other_zoom_windows_are_not_a_share(string title, string className) =>
        Assert.DoesNotContain(ShareSignature.BuiltIn, s => s.Matches(Window("zoom.exe", title, className)));

    [Fact]
    public void Zoom_meeting_window_is_a_meeting()
    {
        Assert.Contains(ShareSignature.BuiltInMeetings, s => s.Matches(Window("zoom.exe", "Zoom Meeting", "ZPContentViewWndClass")));
        Assert.DoesNotContain(ShareSignature.BuiltInMeetings, s => s.Matches(Window("zoom.exe", "Zoom Workplace", "ZPPTMainFrmWndClassEx")));
    }

    [Fact]
    public void Class_name_is_exact()
    {
        var signature = new ShareSignature { ProcessNames = ["slack.exe"], ClassName = "HuddleBar" };
        Assert.True(signature.Matches(Window("slack.exe", "", "HuddleBar")));
        Assert.False(signature.Matches(Window("slack.exe", "", "huddlebar")));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(" ", null)]
    [InlineData(null, "  ")]
    public void Signature_without_window_property_is_invalid(string? title, string? className)
    {
        var signature = new ShareSignature { ProcessNames = ["slack.exe"], TitleContains = title, ClassName = className };
        Assert.False(signature.IsValid);
    }
}

public class HideStateTests
{
    [Fact]
    public void Detects_immediately_and_clears_after_threshold()
    {
        var state = new HideState(clearAfterPolls: 3);
        Assert.True(state.ReportPoll(true));
        Assert.True(state.ShouldHide);

        Assert.False(state.ReportPoll(false));
        Assert.False(state.ReportPoll(false));
        Assert.True(state.ShouldHide);
        Assert.True(state.ReportPoll(false));
        Assert.False(state.ShouldHide);
    }

    [Fact]
    public void Share_window_reappearing_resets_the_countdown()
    {
        var state = new HideState(clearAfterPolls: 2);
        state.ReportPoll(true);
        state.ReportPoll(false);
        state.ReportPoll(true);
        state.ReportPoll(false);
        Assert.True(state.ShouldHide);
    }

    [Fact]
    public void Manual_toggle_overrides_until_detection_changes()
    {
        var state = new HideState(clearAfterPolls: 1);
        state.ReportPoll(true);
        state.Toggle();
        Assert.False(state.ShouldHide);
        state.ReportPoll(true);
        Assert.False(state.ShouldHide); // still sharing: override holds

        state.ReportPoll(false);        // sharing ended: override cleared
        Assert.Null(state.Override);
        Assert.False(state.ShouldHide);
    }

    [Fact]
    public void Manual_hide_works_without_detection()
    {
        var state = new HideState(clearAfterPolls: 1) { AutoDetect = false };
        state.Toggle();
        Assert.True(state.ShouldHide);
        state.ClearOverride();
        Assert.False(state.ShouldHide);
    }

    [Fact]
    public void Disabling_detection_forgets_a_detected_share()
    {
        var state = new HideState(clearAfterPolls: 3);
        state.ReportPoll(true);
        state.AutoDetect = false;
        state.AutoDetect = true;
        Assert.False(state.SharingDetected);
        Assert.False(state.ShouldHide);
    }

    [Fact]
    public void Rejects_zero_threshold() => Assert.Throws<ArgumentOutOfRangeException>(() => new HideState(0));
}

public class AppSettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sharehider-tests").FullName;

    private string PathFor(string name) => Path.Combine(_dir, name);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var settings = AppSettings.Load(PathFor("none.json"), out var error);
        Assert.Null(error);
        var group = Assert.Single(settings.Groups);
        Assert.Equal(HideGroup.DefaultName, group.Name);
        Assert.Empty(group.Apps);
        Assert.Equal(HideGroup.DefaultName, settings.ActiveGroup);
        Assert.True(settings.AutoDetect);
        Assert.Equal("Ctrl+Alt+H", settings.Hotkey);
    }

    [Fact]
    public void Round_trips()
    {
        var path = PathFor("settings.json");
        new AppSettings
        {
            Groups =
            [
                new HideGroup { Name = "Work", Apps = ["Outlook.exe"], Hotkey = "ctrl+alt+1" },
                new HideGroup { Name = "Music", Apps = ["spotify.exe"] },
            ],
            ActiveGroup = "music",
            MinimizeWindows = false,
            Hotkey = "win+f9",
        }.Save(path);

        var loaded = AppSettings.Load(path, out var error);
        Assert.Null(error);
        Assert.Equal(["Work", "Music"], loaded.Groups.Select(g => g.Name));
        Assert.Equal(["outlook.exe"], loaded.Groups[0].Apps);
        Assert.Equal("Ctrl+Alt+1", loaded.Groups[0].Hotkey);
        Assert.Null(loaded.Groups[1].Hotkey);
        Assert.Equal("Music", loaded.ActiveGroup);
        Assert.False(loaded.MinimizeWindows);
        Assert.Equal("Win+F9", loaded.Hotkey);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Malformed_json_gives_defaults_and_an_error()
    {
        var path = PathFor("bad.json");
        File.WriteAllText(path, "{ \"HiddenApps\": [ ");
        var settings = AppSettings.Load(path, out var error);
        Assert.NotNull(error);
        Assert.Empty(Assert.Single(settings.Groups).Apps);
    }

    [Fact]
    public void Oversized_file_is_not_parsed()
    {
        var path = PathFor("big.json");
        File.WriteAllText(path, new string(' ', (int)AppSettings.MaxFileBytes + 1));
        AppSettings.Load(path, out var error);
        Assert.NotNull(error);
    }

    [Fact]
    public void Invalid_entries_are_dropped()
    {
        var path = PathFor("mixed.json");
        File.WriteAllText(path, """
            {
              // comments and trailing commas are allowed
              "HiddenApps": ["good.exe", "GOOD.EXE", "C:\\evil\\path.exe", "noext", null],
              "Hotkey": "H",
              "CustomSignatures": [
                { "Name": "ok", "ProcessNames": ["Slack.exe"], "TitleContains": "huddle" },
                { "Name": "matches everything", "ProcessNames": ["slack.exe"] },
                { "Name": "no process", "ProcessNames": [], "TitleContains": "x" },
              ],
              "CustomMeetingSignatures": [
                { "Name": "huddle", "ProcessNames": ["slack.exe"], "ClassName": "HuddleWindow" },
                { "Name": "matches everything", "ProcessNames": ["slack.exe"] },
              ],
            }
            """);

        var settings = AppSettings.Load(path, out var error);
        Assert.Null(error);
        Assert.Equal(["good.exe"], Assert.Single(settings.Groups).Apps); // the old list became the first group
        Assert.Equal("Ctrl+Alt+H", settings.Hotkey);
        var signature = Assert.Single(settings.CustomSignatures);
        Assert.Equal(["slack.exe"], signature.ProcessNames);
        Assert.Equal("huddle", Assert.Single(settings.CustomMeetingSignatures).Name);
    }

    [Fact]
    public void Hidden_app_list_is_capped()
    {
        var settings = new AppSettings
        {
            HiddenApps = Enumerable.Range(0, AppSettings.MaxHiddenApps + 10).Select(i => $"app{i}.exe").ToList(),
        }.Normalized();
        Assert.Equal(AppSettings.MaxHiddenApps, settings.Groups[0].Apps.Count);
    }

    [Fact]
    public void Old_auto_hide_list_moves_into_a_group_and_is_not_written_back()
    {
        var path = PathFor("old.json");
        File.WriteAllText(path, """{ "HiddenApps": ["outlook.exe"] }""");
        AppSettings.Load(path, out _).Save(path);

        var text = File.ReadAllText(path);
        Assert.DoesNotContain("HiddenApps", text);
        var loaded = AppSettings.Load(path, out _);
        Assert.Equal(["outlook.exe"], Assert.Single(loaded.Groups).Apps);
        Assert.Equal(HideGroup.DefaultName, loaded.ActiveGroup);
    }

    [Fact]
    public void Invalid_groups_and_taken_hotkeys_are_dropped()
    {
        var settings = new AppSettings
        {
            Hotkey = "Ctrl+Alt+H",
            Groups =
            [
                new HideGroup { Name = "  Work  ", Hotkey = "ctrl+alt+h" },  // taken by the main hotkey
                new HideGroup { Name = "WORK" },                             // duplicate name
                new HideGroup { Name = "" },
                new HideGroup { Name = new string('x', HideGroup.MaxNameLength + 1) },
                new HideGroup { Name = "Music", Hotkey = "Ctrl+Alt+M" },
                new HideGroup { Name = "Chat", Hotkey = "ctrl+alt+m" },      // taken by Music
            ],
            ActiveGroup = "missing",
        }.Normalized();

        Assert.Equal(["Work", "Music", "Chat"], settings.Groups.Select(g => g.Name));
        Assert.Equal([null, "Ctrl+Alt+M", null], settings.Groups.Select(g => g.Hotkey));
        Assert.Equal("Work", settings.ActiveGroup); // unknown active group falls back to the first
    }

    [Fact]
    public void Group_count_is_capped()
    {
        var settings = new AppSettings
        {
            Groups = Enumerable.Range(0, AppSettings.MaxGroups + 5).Select(i => new HideGroup { Name = $"g{i}" }).ToList(),
        }.Normalized();
        Assert.Equal(AppSettings.MaxGroups, settings.Groups.Count);
    }

    [Theory]
    [InlineData("Work", true)]
    [InlineData("  Work  ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("tab\there", false)]
    public void Group_names_are_validated(string input, bool valid) =>
        Assert.Equal(valid, HideGroup.TryNormalizeName(input, out _));
}

public class HideSelectionTests
{
    [Fact]
    public void Auto_hide_apps_hide_only_while_sharing()
    {
        var selection = new HideSelection(["outlook.exe"]);
        Assert.False(selection.IsHidden("outlook.exe", shareHiding: false));
        Assert.True(selection.IsHidden("outlook.exe", shareHiding: true));
        Assert.False(selection.IsHidden("spotify.exe", shareHiding: true));
        Assert.Equal(["outlook.exe"], selection.HiddenApps(shareHiding: true));
        Assert.Empty(selection.HiddenApps(shareHiding: false));
    }

    [Fact]
    public void Click_hides_any_app_on_demand_and_again_shows_it()
    {
        var selection = new HideSelection([]);
        selection.Toggle("spotify.exe", shareHiding: false);
        Assert.True(selection.IsHidden("spotify.exe", shareHiding: false));
        Assert.True(selection.IsHidden("spotify.exe", shareHiding: true));

        selection.Toggle("spotify.exe", shareHiding: false);
        Assert.False(selection.IsHidden("spotify.exe", shareHiding: false));
        Assert.False(selection.HasOverride("spotify.exe"));
    }

    [Fact]
    public void Click_during_share_shows_an_auto_hidden_app_until_the_share_ends()
    {
        var selection = new HideSelection(["outlook.exe"]);
        selection.Toggle("outlook.exe", shareHiding: true);
        Assert.False(selection.IsHidden("outlook.exe", shareHiding: true));
        Assert.DoesNotContain("outlook.exe", selection.HiddenApps(shareHiding: true));

        selection.ShareEnded();
        Assert.False(selection.HasOverride("outlook.exe"));
        Assert.True(selection.IsHidden("outlook.exe", shareHiding: true)); // next share hides it again
    }

    [Fact]
    public void Hide_overrides_survive_the_end_of_a_share()
    {
        var selection = new HideSelection([]);
        selection.Toggle("spotify.exe", shareHiding: true);
        selection.ShareEnded();
        Assert.True(selection.IsHidden("spotify.exe", shareHiding: false));
    }

    [Fact]
    public void Override_equal_to_default_is_dropped()
    {
        var selection = new HideSelection(["outlook.exe"]);
        selection.Toggle("outlook.exe", shareHiding: false); // hide now (default: shown)
        Assert.True(selection.HasOverride("outlook.exe"));
        Assert.True(selection.IsHidden("outlook.exe", shareHiding: true));

        selection.Toggle("outlook.exe", shareHiding: true); // show; default while sharing is hidden
        Assert.False(selection.IsHidden("outlook.exe", shareHiding: true));
        selection.Toggle("outlook.exe", shareHiding: true); // back to hidden == default
        Assert.False(selection.HasOverride("outlook.exe"));
    }

    [Fact]
    public void Auto_hide_list_can_be_replaced_and_overrides_cleared()
    {
        var selection = new HideSelection([]);
        selection.ReplaceAutoHide(["slack.exe"]);
        Assert.True(selection.IsAutoHide("slack.exe"));
        selection.Toggle("zoom.exe", shareHiding: false);
        Assert.True(selection.HasAnyOverride);

        selection.ClearOverrides();
        selection.ReplaceAutoHide([]);
        Assert.False(selection.HasAnyOverride);
        Assert.Empty(selection.HiddenApps(shareHiding: true));
    }

    [Fact]
    public void Group_toggle_hides_all_then_shows_all()
    {
        var selection = new HideSelection([]);
        string[] group = ["outlook.exe", "slack.exe"];
        selection.Toggle("outlook.exe", shareHiding: false); // one already hidden by hand

        selection.ToggleGroup(group, shareHiding: false);
        Assert.True(selection.AllHidden(group, shareHiding: false));

        selection.ToggleGroup(group, shareHiding: false);
        Assert.Empty(selection.HiddenApps(shareHiding: false));
        Assert.False(selection.HasAnyOverride);
    }

    [Fact]
    public void Group_toggle_during_share_shows_the_active_group_and_empty_group_is_never_all_hidden()
    {
        var selection = new HideSelection(["outlook.exe"]);
        selection.ToggleGroup(["outlook.exe"], shareHiding: true);
        Assert.False(selection.IsHidden("outlook.exe", shareHiding: true));
        Assert.False(selection.AllHidden([], shareHiding: true));
    }
}

public class AppOrderTests
{
    [Fact]
    public void Existing_apps_keep_their_place_and_new_ones_append()
    {
        var order = new AppOrder();
        Assert.Equal(["b.exe", "c.exe"], order.Update(["c.exe", "b.exe"]));
        Assert.Equal(["b.exe", "c.exe", "a.exe"], order.Update(["a.exe", "c.exe", "b.exe"]));
    }

    [Fact]
    public void Closed_apps_are_dropped_and_come_back_at_the_end()
    {
        var order = new AppOrder();
        order.Update(["a.exe", "b.exe", "c.exe"]);
        Assert.Equal(["a.exe", "c.exe"], order.Update(["a.exe", "c.exe"]));
        Assert.Equal(["a.exe", "c.exe", "b.exe"], order.Update(["b.exe", "a.exe", "c.exe"]));
    }
}

public class TaskbarOrderTests
{
    private static readonly HashSet<string> None = [];

    [Fact]
    public void Merge_keeps_hidden_apps_after_the_app_they_followed()
    {
        var known = new[] { "edge", "outlook", "notepad", "slack" };
        var merged = TaskbarOrder.Merge(known, ["edge", "notepad", "slack", "zoom"], new HashSet<string> { "outlook" });
        Assert.Equal(["edge", "outlook", "notepad", "slack", "zoom"], merged);
    }

    [Fact]
    public void Merge_puts_a_hidden_first_app_first_and_forgets_closed_apps()
    {
        var merged = TaskbarOrder.Merge(["outlook", "edge", "gone"], ["edge"], new HashSet<string> { "outlook" });
        Assert.Equal(["outlook", "edge"], merged);
    }

    [Fact]
    public void Merge_with_nothing_known_is_the_observed_order() =>
        Assert.Equal(["a", "b"], TaskbarOrder.Merge([], ["a", "b", "a"], None));

    [Fact]
    public void Plan_is_empty_when_the_order_is_right() =>
        Assert.Empty(TaskbarOrder.Plan(["a", "b", "c"], ["a", "b", "c"]));

    [Fact]
    public void Plan_cycles_the_apps_after_a_restored_one()
    {
        // outlook was second; re-adding it put it last.
        var plan = TaskbarOrder.Plan(["edge", "outlook", "notepad", "slack"], ["edge", "notepad", "slack", "outlook"]);
        Assert.Equal(["notepad", "slack"], plan);
    }

    [Fact]
    public void Plan_restores_several_apps_at_once()
    {
        var plan = TaskbarOrder.Plan(["a", "b", "c", "d", "e"], ["a", "c", "e", "b", "d"]);
        Assert.Equal(["c", "d", "e"], plan);
        Assert.Equal(["a", "b", "c", "d", "e"], Apply(["a", "c", "e", "b", "d"], plan));
    }

    [Fact]
    public void Plan_leaves_new_apps_after_the_known_ones()
    {
        var current = new[] { "a", "new", "c", "b" };
        var plan = TaskbarOrder.Plan(["a", "b", "c"], current);
        Assert.Equal(["a", "b", "c", "new"], Apply(current, plan));
    }

    [Theory]
    [InlineData("abcdef", "fedcba")]
    [InlineData("abcdef", "bacdfe")]
    [InlineData("abcdef", "abcdef")]
    [InlineData("abcdef", "cabdef")]
    public void Plan_always_reaches_the_desired_order(string desired, string current)
    {
        var want = desired.Select(c => c.ToString()).ToList();
        var have = current.Select(c => c.ToString()).ToList();
        Assert.Equal(want, Apply(have, TaskbarOrder.Plan(want, have)));
    }

    /// <summary>What the taskbar does: each cycled app goes to the end.</summary>
    private static List<string> Apply(IEnumerable<string> current, IEnumerable<string> plan)
    {
        var result = current.ToList();
        foreach (var id in plan)
        {
            result.Remove(id);
            result.Add(id);
        }

        return result;
    }
}

