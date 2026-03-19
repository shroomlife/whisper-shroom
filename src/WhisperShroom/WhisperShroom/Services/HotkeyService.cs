using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using WhisperShroom.Helpers;

namespace WhisperShroom.Services;

public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 1;
    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_QUIT = 0x0012;
    private Thread? _thread;
    private uint _threadId;
    private bool _registered;
    private Action? _callback;

    public bool IsRegistered => _registered;

    public void Register(string hotkeyStr, Action callback)
    {
        Unregister();

        _callback = callback;
        var (modifiers, vkCode) = HotkeyParser.Parse(hotkeyStr);

        _thread = new Thread(() => ListenLoop(modifiers, vkCode))
        {
            IsBackground = true,
            Name = "HotkeyListener"
        };
        _thread.Start();
    }

    private void ListenLoop(uint modifiers, uint vkCode)
    {
        _threadId = PInvoke.GetCurrentThreadId();

        var modFlags = (HOT_KEY_MODIFIERS)modifiers;
        if (!PInvoke.RegisterHotKey(default, HotkeyId, modFlags, vkCode))
        {
            _registered = false;
            return;
        }

        _registered = true;

        while (PInvoke.GetMessage(out var msg, default, 0, 0))
        {
            if (msg.message == WM_HOTKEY && msg.wParam == (nuint)HotkeyId)
            {
                _callback?.Invoke();
            }
        }

        PInvoke.UnregisterHotKey(default, HotkeyId);
        _registered = false;
    }

    public void Unregister()
    {
        if (_threadId != 0)
        {
            PInvoke.PostThreadMessage(_threadId, WM_QUIT, 0, 0);
            _threadId = 0;
        }

        if (_thread is not null)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
            _thread = null;
        }

        _callback = null;
    }

    public void Dispose()
    {
        Unregister();
    }
}
