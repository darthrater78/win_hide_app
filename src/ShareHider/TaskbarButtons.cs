using System.Windows.Automation;

namespace ShareHider;

/// <summary>Reads the real taskbar's buttons, in on-screen order, through UI Automation.</summary>
internal static class TaskbarButtons
{
    public sealed record Button(string Name, string AutomationId, string ClassName);

    private const string AppIdPrefix = "Appid: ";

    /// <summary>
    /// The app ids of the taskbar's app buttons (pinned and running), left to right. Empty
    /// when this Windows version's taskbar doesn't expose them, which turns off keeping
    /// restored buttons in place.
    /// </summary>
    public static List<string> AppIds() =>
        Read()
            .Where(b => b.AutomationId.StartsWith(AppIdPrefix, StringComparison.Ordinal))
            .Select(b => b.AutomationId[AppIdPrefix.Length..])
            .ToList();

    /// <summary>Every button on the primary taskbar (Start, pinned and running apps, tray), or empty when unreadable.</summary>
    public static List<Button> Read()
    {
        try
        {
            var taskbar = AutomationElement.RootElement.FindFirst(
                TreeScope.Children, new PropertyCondition(AutomationElement.ClassNameProperty, "Shell_TrayWnd"));
            if (taskbar is null)
            {
                return [];
            }

            var buttons = taskbar.FindAll(
                TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            return buttons.Cast<AutomationElement>()
                .Select(b => new Button(b.Current.Name, b.Current.AutomationId, b.Current.ClassName))
                .ToList();
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
        {
            Log.Write($"could not read the taskbar: {ex.Message}");
            return [];
        }
    }
}
