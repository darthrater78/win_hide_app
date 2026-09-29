using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ShareHider;

/// <summary>One app in the taskbar mockup: all of its taskbar windows grouped as one button.</summary>
internal sealed class AppButton(string exeName) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string ExeName { get; } = exeName;

    public string DisplayName { get; private set => Set(ref field, value); } = exeName;

    public ImageSource? Icon { get; private set => Set(ref field, value); }

    /// <summary>First letter, shown when the exe has no readable icon.</summary>
    public string Initial => DisplayName.Length > 0 ? DisplayName[..1].ToUpperInvariant() : "?";

    public int WindowCount { get; private set => Set(ref field, value); }

    /// <summary>The button is currently removed from the real taskbar.</summary>
    public bool IsHidden { get; private set => Set(ref field, value); }

    /// <summary>The app is on the list hidden automatically while sharing.</summary>
    public bool IsAutoHide { get; private set => Set(ref field, value); }

    /// <summary>The app is pinned to the real taskbar, so hiding it leaves its pinned icon.</summary>
    public bool IsPinned { get; private set => Set(ref field, value); }

    public string ToolTip
    {
        get
        {
            var windows = WindowCount == 1 ? "1 window" : $"{WindowCount} windows";
            var state = IsHidden ? "Hidden from the taskbar" : "Shown on the taskbar";
            var auto = IsAutoHide ? "\nHidden automatically while sharing" : "";
            var pinned = IsPinned ? "\nPinned: hiding leaves its icon on the taskbar. Unpin it to hide it completely." : "";
            return $"{DisplayName} ({windows})\n{state}{auto}{pinned}\n\nClick to {(IsHidden ? "show" : "hide")} now · Right-click for options";
        }
    }

    public void Update(string displayName, ImageSource? icon, int windowCount, bool hidden, bool autoHide, bool pinned)
    {
        DisplayName = displayName;
        Icon = icon;
        WindowCount = windowCount;
        IsHidden = hidden;
        IsAutoHide = autoHide;
        IsPinned = pinned;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name == nameof(DisplayName))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Initial)));
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToolTip)));
    }
}
