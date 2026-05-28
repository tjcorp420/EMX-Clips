using System.Runtime.InteropServices;

namespace EmxClips;

public sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private readonly int _hotkeyId;
    private bool _registered;

    public event EventHandler? HotkeyPressed;

    public HotkeyWindow(int hotkeyId)
    {
        _hotkeyId = hotkeyId;
        CreateHandle(new CreateParams());
    }

    public bool Register(Keys key, HotkeyModifiers modifiers)
    {
        Unregister();

        var nativeModifiers = (uint)(modifiers | HotkeyModifiers.NoRepeat);
        _registered = RegisterHotKey(Handle, _hotkeyId, nativeModifiers, (uint)key);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        UnregisterHotKey(Handle, _hotkeyId);
        _registered = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam.ToInt32() == _hotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
