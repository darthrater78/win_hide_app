using ShareHider.Core;

namespace ShareHider;

/// <summary>A message-only window that receives one global hotkey.</summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int HotkeyId = 1;
    private static readonly nint HwndMessage = -3;
    private bool _registered;

    public HotkeyWindow() => CreateHandle(new CreateParams { Parent = HwndMessage });

    public event EventHandler? Pressed;

    /// <summary>Registers the hotkey, replacing any previous one. False when another app already owns it.</summary>
    public bool Register(Hotkey hotkey)
    {
        Unregister();
        _registered = Native.RegisterHotKey(
            Handle, HotkeyId, (uint)hotkey.Modifiers | Native.MOD_NOREPEAT, hotkey.VirtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            Native.UnregisterHotKey(Handle, HotkeyId);
            _registered = false;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && m.WParam == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }
}
