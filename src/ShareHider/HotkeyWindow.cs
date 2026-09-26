using System.Windows.Forms;
using ShareHider.Core;

namespace ShareHider;

/// <summary>A message-only window that receives global hotkeys, each registered under its own id.</summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private static readonly nint HwndMessage = -3;
    private readonly HashSet<int> _registered = [];

    public HotkeyWindow() => CreateHandle(new CreateParams { Parent = HwndMessage });

    /// <summary>Raised with the id the pressed hotkey was registered under.</summary>
    public event EventHandler<int>? Pressed;

    /// <summary>Registers a hotkey under <paramref name="id"/>, replacing any previous one. False when another app already owns it.</summary>
    public bool Register(int id, Hotkey hotkey)
    {
        Unregister(id);
        if (!Native.RegisterHotKey(Handle, id, (uint)hotkey.Modifiers | Native.MOD_NOREPEAT, hotkey.VirtualKey))
        {
            return false;
        }

        _registered.Add(id);
        return true;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id))
        {
            Native.UnregisterHotKey(Handle, id);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && _registered.Contains((int)m.WParam))
        {
            Pressed?.Invoke(this, (int)m.WParam);
            return;
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (var id in _registered.ToList())
        {
            Unregister(id);
        }

        DestroyHandle();
    }
}
