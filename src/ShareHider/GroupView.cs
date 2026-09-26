using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ShareHider;

/// <summary>One saved group, as the window and tray menu show it.</summary>
internal sealed class GroupView(string name) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; set => Set(ref field, value); } = name;

    /// <summary>Exe names, kept sorted.</summary>
    public ObservableCollection<string> Apps { get; } = [];

    /// <summary>Normalized hotkey text such as "Ctrl+Alt+1", or null for none.</summary>
    public string? Hotkey { get; set => Set(ref field, value); }

    /// <summary>Why the hotkey isn't working, or null.</summary>
    public string? HotkeyError { get; set => Set(ref field, value); }

    /// <summary>This group is hidden automatically while sharing.</summary>
    public bool IsActive { get; set => Set(ref field, value); }

    /// <summary>Every app in the group is hidden right now.</summary>
    public bool IsHidden { get; set => Set(ref field, value); }

    public string DisplayName => IsActive ? $"{Name} (while sharing)" : Name;

    public string ToggleText => IsHidden ? "Show this group now" : "Hide this group now";

    public void AddApp(string exe)
    {
        var index = 0;
        while (index < Apps.Count && string.CompareOrdinal(Apps[index], exe) < 0)
        {
            index++;
        }

        if (index == Apps.Count || Apps[index] != exe)
        {
            Apps.Insert(index, exe);
        }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name is nameof(Name) or nameof(IsActive))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        }

        if (name == nameof(IsHidden))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToggleText)));
        }
    }
}
