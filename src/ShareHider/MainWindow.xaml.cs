using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace ShareHider;

/// <summary>The main window. Closing it only hides it: ShareHider keeps running in the tray.</summary>
internal sealed partial class MainWindow : Window
{
    private readonly Controller _controller;
    private bool _exiting;

    public MainWindow(Controller controller)
    {
        _controller = controller;
        DataContext = controller;
        InitializeComponent();
        VersionRun.Text = $"ShareHider {AppInfo.Version}";
        HotkeyBox.Text = controller.HotkeyText;
        ShowSelectedGroup();
        controller.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Controller.SelectedGroup))
            {
                ShowSelectedGroup();
            }
        };
    }

    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        // Windows refuses focus to a process the user isn't interacting with (for example a
        // start the user clicked away from); still bring the window to the top so it is seen.
        if (!Activate())
        {
            Topmost = true;
            Topmost = false;
        }
    }

    /// <summary>Lets the next Close actually close, for app shutdown.</summary>
    public void PrepareExit() => _exiting = true;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private static AppButton? AppOf(object sender) => (sender as FrameworkElement)?.DataContext as AppButton;

    private void AppButton_Click(object sender, RoutedEventArgs e)
    {
        if (AppOf(sender) is { } app)
        {
            _controller.ToggleApp(app.ExeName);
        }
    }

    /// <summary>Builds the mockup button's menu: hide or show now, then one check item per group.</summary>
    private void AppMenu_Opening(object sender, ContextMenuEventArgs e)
    {
        if (AppOf(sender) is not { } app || (sender as FrameworkElement)?.ContextMenu is not { } menu)
        {
            return;
        }

        menu.Items.Clear();
        menu.Items.Add(new MenuItem { Header = app.DisplayName, IsEnabled = false, FontWeight = FontWeights.SemiBold });
        menu.Items.Add(new Separator());
        var toggle = new MenuItem { Header = "Hide or show now" };
        toggle.Click += (_, _) => _controller.ToggleApp(app.ExeName);
        menu.Items.Add(toggle);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "In groups", IsEnabled = false });
        foreach (var group in _controller.Groups)
        {
            var member = group.Apps.Contains(app.ExeName);
            // Header is a TextBlock so an underscore in a group name isn't read as an access key.
            var item = new MenuItem { Header = new TextBlock { Text = group.DisplayName }, IsChecked = member };
            item.Click += (_, _) => _controller.SetInGroup(group, app.ExeName, !member);
            menu.Items.Add(item);
        }
    }

    private void ShowSelectedGroup()
    {
        GroupNameBox.Text = _controller.SelectedGroup.Name;
        GroupHotkeyBox.Text = _controller.SelectedGroup.Hotkey ?? "";
        GroupNameError.Visibility = Visibility.Collapsed;
        AddError.Visibility = Visibility.Collapsed;
    }

    private void NewGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.NewGroup())
        {
            GroupNameBox.Focus();
            GroupNameBox.SelectAll();
        }
    }

    private void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        var group = _controller.SelectedGroup;
        var answer = MessageBox.Show(this, $"Delete the group {group.Name}? Its apps stay as they are now.",
            "ShareHider", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.OK)
        {
            _controller.DeleteGroup(group);
        }
    }

    private void RenameGroup_Click(object sender, RoutedEventArgs e)
    {
        var error = _controller.RenameGroup(_controller.SelectedGroup, GroupNameBox.Text);
        GroupNameError.Text = error;
        GroupNameError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        if (error is null)
        {
            GroupNameBox.Text = _controller.SelectedGroup.Name;
        }
    }

    private void GroupNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            RenameGroup_Click(sender, e);
        }
    }

    private void GroupHotkey_Click(object sender, RoutedEventArgs e)
    {
        var group = _controller.SelectedGroup;
        var error = _controller.SetGroupHotkey(group, GroupHotkeyBox.Text);
        if (error is null)
        {
            GroupHotkeyBox.Text = group.Hotkey ?? ""; // normalized, e.g. "ctrl+alt+1" -> "Ctrl+Alt+1"
        }
        else
        {
            group.HotkeyError = error;
        }
    }

    private void GroupHotkeyBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            GroupHotkey_Click(sender, e);
        }
    }

    private void SetActive_Click(object sender, RoutedEventArgs e) => _controller.SetActiveGroup(_controller.SelectedGroup);

    private void ToggleGroup_Click(object sender, RoutedEventArgs e) => _controller.ToggleGroup(_controller.SelectedGroup);

    private void RemoveFromGroup_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string exe)
        {
            _controller.SetInGroup(_controller.SelectedGroup, exe, false);
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var added = _controller.AddToGroup(_controller.SelectedGroup, AddBox.Text);
        AddError.Visibility = added ? Visibility.Collapsed : Visibility.Visible;
        if (added)
        {
            AddBox.Clear();
        }
    }

    private void AddBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Add_Click(sender, e);
        }
    }

    private void ApplyHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.ApplyHotkey(HotkeyBox.Text) is null)
        {
            HotkeyBox.Text = _controller.HotkeyText; // normalized, e.g. "ctrl+alt+h" -> "Ctrl+Alt+H"
        }
    }

    private void HotkeyBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyHotkey_Click(sender, e);
        }
    }

    private void ShareHiding_Click(object sender, RoutedEventArgs e) => _controller.ToggleShareHiding();

    private void Resume_Click(object sender, RoutedEventArgs e) => _controller.ResumeAutomatic();

    private void GitHub_Click(object sender, RoutedEventArgs e) => Controller.Open(AppInfo.RepositoryUrl);

    private void ReleaseNotes_Click(object sender, RoutedEventArgs e) => Controller.Open(AppInfo.ReleaseNotesUrl);

    private void LogFolder_Click(object sender, RoutedEventArgs e) => Controller.OpenLogFolder();

    private void WindowList_Click(object sender, RoutedEventArgs e) => Controller.WriteWindowList();
}

/// <summary>Visible when the bound bool is false.</summary>
internal sealed class FalseToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is false ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True when the bound count is above one: the last group can't be deleted.</summary>
internal sealed class MoreThanOneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is > 1;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Collapsed when the bound value is null, visible otherwise.</summary>
internal sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the bound count is zero: the mockup's "no apps" message.</summary>
internal sealed class ZeroToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
