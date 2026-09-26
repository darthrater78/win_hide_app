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

    private void ToggleMenu_Click(object sender, RoutedEventArgs e) => AppButton_Click(sender, e);

    private void AutoHideMenu_Click(object sender, RoutedEventArgs e)
    {
        if (AppOf(sender) is { } app)
        {
            _controller.SetAutoHide(app.ExeName, !app.IsAutoHide);
        }
    }

    private void RemoveAutoHide_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string exe)
        {
            _controller.SetAutoHide(exe, false);
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var added = _controller.AddAutoHide(AddBox.Text);
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

/// <summary>Visible when the bound count is zero: the mockup's "no apps" message.</summary>
internal sealed class ZeroToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
