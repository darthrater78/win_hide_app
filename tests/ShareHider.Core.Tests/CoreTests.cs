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
        Assert.Empty(settings.HiddenApps);
        Assert.True(settings.AutoDetect);
        Assert.Equal("Ctrl+Alt+H", settings.Hotkey);
    }

    [Fact]
    public void Round_trips()
    {
        var path = PathFor("settings.json");
        new AppSettings { HiddenApps = ["Outlook.exe"], MinimizeWindows = false, Hotkey = "win+f9" }.Save(path);

        var loaded = AppSettings.Load(path, out var error);
        Assert.Null(error);
        Assert.Equal(["outlook.exe"], loaded.HiddenApps);
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
        Assert.Empty(settings.HiddenApps);
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
            }
            """);

        var settings = AppSettings.Load(path, out var error);
        Assert.Null(error);
        Assert.Equal(["good.exe"], settings.HiddenApps);
        Assert.Equal("Ctrl+Alt+H", settings.Hotkey);
        var signature = Assert.Single(settings.CustomSignatures);
        Assert.Equal(["slack.exe"], signature.ProcessNames);
    }

    [Fact]
    public void Hidden_app_list_is_capped()
    {
        var settings = new AppSettings
        {
            HiddenApps = Enumerable.Range(0, AppSettings.MaxHiddenApps + 10).Select(i => $"app{i}.exe").ToList(),
        }.Normalized();
        Assert.Equal(AppSettings.MaxHiddenApps, settings.HiddenApps.Count);
    }
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
    public void Auto_hide_list_can_be_edited_and_overrides_cleared()
    {
        var selection = new HideSelection([]);
        selection.SetAutoHide("slack.exe", true);
        Assert.True(selection.IsAutoHide("slack.exe"));
        selection.Toggle("zoom.exe", shareHiding: false);
        Assert.True(selection.HasAnyOverride);

        selection.ClearOverrides();
        selection.SetAutoHide("slack.exe", false);
        Assert.False(selection.HasAnyOverride);
        Assert.Empty(selection.HiddenApps(shareHiding: true));
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
