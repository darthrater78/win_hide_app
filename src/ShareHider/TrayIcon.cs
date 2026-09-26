using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace ShareHider;

/// <summary>
/// The notification-area icon. WPF has none of its own, so this uses WinForms'
/// NotifyIcon, which runs fine on WPF's message loop. It never shows balloon tips:
/// one would pop up on the very screen being shared.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Controller _controller;
    private readonly NotifyIcon _icon = new();
    private readonly Icon _idleIcon = LoadIcon("ShareHider.ico");
    private readonly Icon _activeIcon = LoadIcon("ShareHiderActive.ico");
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private readonly ToolStripMenuItem _shareHiding = new("&Hide the active group");
    private readonly ToolStripMenuItem _groups = new("&Groups");
    private readonly ToolStripMenuItem _autoDetect = new("Auto-&detect screen sharing");
    private readonly ToolStripMenuItem _resume = new("&Resume automatic hiding");

    public TrayIcon(Controller controller, Action showWindow, Action exit)
    {
        _controller = controller;
        _shareHiding.Click += (_, _) => controller.ToggleShareHiding();
        _autoDetect.Click += (_, _) => controller.AutoDetect = !controller.AutoDetect;
        _resume.Click += (_, _) => controller.ResumeAutomatic();
        _groups.DropDownItems.Add(new ToolStripMenuItem()); // placeholder, so the submenu arrow shows
        _groups.DropDownOpening += (_, _) => FillGroupsMenu();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(
        [
            new ToolStripMenuItem("&Open ShareHider", null, (_, _) => showWindow()) { Font = new Font(menu.Font, FontStyle.Bold) },
            _status,
            new ToolStripSeparator(),
            _shareHiding,
            _groups,
            _autoDetect,
            _resume,
            new ToolStripSeparator(),
            new ToolStripMenuItem("E&xit", null, (_, _) => exit()),
        ]);

        _icon.ContextMenuStrip = menu;
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                showWindow();
            }
        };

        controller.PropertyChanged += OnControllerChanged;
        UpdateMenu();
        _icon.Visible = true;
    }

    private static Icon LoadIcon(string name)
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream($"ShareHider.Assets.{name}")
            ?? throw new InvalidOperationException($"missing embedded icon {name}");
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    /// <summary>One entry per group, rebuilt each time the submenu opens so it is never stale.</summary>
    private void FillGroupsMenu()
    {
        _groups.DropDownItems.Clear();
        foreach (var group in _controller.Groups)
        {
            // "&&" so an ampersand in a group name isn't read as an access key.
            var text = $"{(group.IsHidden ? "Show" : "Hide")} {group.DisplayName.Replace("&", "&&")}";
            var item = new ToolStripMenuItem(text, null, (_, _) => _controller.ToggleGroup(group))
            {
                Checked = group.IsHidden,
                ShortcutKeyDisplayString = group.Hotkey,
            };
            _groups.DropDownItems.Add(item);
        }
    }

    private void OnControllerChanged(object? sender, PropertyChangedEventArgs e) => UpdateMenu();

    private void UpdateMenu()
    {
        _status.Text = _controller.StatusTitle;
        _shareHiding.Checked = _controller.IsShareHiding;
        _autoDetect.Checked = _controller.AutoDetect;
        _resume.Visible = _controller.HasOverrides;

        // Only touch the shell when something changed: the controller raises many events per second.
        var icon = _controller.IsHidingAny ? _activeIcon : _idleIcon;
        if (_icon.Icon != icon)
        {
            _icon.Icon = icon;
        }

        // NotifyIcon.Text is limited to 127 characters.
        var tooltip = _controller.MeetingName is { } meeting
            ? $"ShareHider: in a {meeting} meeting. If you share and apps aren't hidden, press {_controller.HotkeyText}."
            : $"ShareHider: {_controller.StatusTitle}";
        tooltip = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        if (_icon.Text != tooltip)
        {
            _icon.Text = tooltip;
        }
    }

    public void Dispose()
    {
        _controller.PropertyChanged -= OnControllerChanged;
        _icon.Visible = false;
        _icon.Dispose();
        _idleIcon.Dispose();
        _activeIcon.Dispose();
    }
}
