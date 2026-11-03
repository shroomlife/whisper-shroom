using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using WhisperShroom.Helpers;

namespace WhisperShroom.Services;

/// <summary>
/// Registers one global hotkey on a dedicated thread with its own Win32 message loop.
/// All public members are called from the UI thread; the listener thread only reads
/// values handed to it through its closure, so no field is shared across threads.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 1;
    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_QUIT = 0x0012;
    private const uint WM_USER = 0x0400;

    private Thread? _thread;
    private uint _threadId;

    public bool IsRegistered { get; private set; }

    /// <summary>
    /// Replaces any previous registration. Returns false if Windows rejected the hotkey,
    /// typically because another application already owns the combination.
    /// </summary>
    public bool Register(string hotkeyStr, Action callback)
    {
        Unregister();

        var (modifiers, vkCode) = HotkeyParser.Parse(hotkeyStr);

        // PostThreadMessage requires the target thread to own a message queue. Following the
        // documented pattern, the listener forces queue creation with PeekMessage and signals
        // readiness before Register returns, so Unregister can always reach it.
        using var ready = new ManualResetEventSlim(false);
        uint threadId = 0;
        var registered = false;

        var thread = new Thread(() =>
        {
            threadId = PInvoke.GetCurrentThreadId();
            PInvoke.PeekMessage(out _, default, WM_USER, WM_USER, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
            registered = PInvoke.RegisterHotKey(default, HotkeyId, (HOT_KEY_MODIFIERS)modifiers, vkCode);
            ready.Set();

            if (!registered)
                return;

            ListenLoop(callback);
        })
        {
            IsBackground = true,
            Name = "HotkeyListener"
        };

        thread.Start();
        ready.Wait();

        if (!registered)
        {
            thread.Join();
            IsRegistered = false;
            return false;
        }

        _thread = thread;
        _threadId = threadId;
        IsRegistered = true;
        return true;
    }

    private static void ListenLoop(Action callback)
    {
        while (PInvoke.GetMessage(out var msg, default, 0, 0))
        {
            if (msg.message == WM_HOTKEY && msg.wParam == (nuint)HotkeyId)
                callback();
        }

        PInvoke.UnregisterHotKey(default, HotkeyId);
    }

    public void Unregister()
    {
        if (_thread is null)
            return;

        PInvoke.PostThreadMessage(_threadId, WM_QUIT, 0, 0);
        _thread.Join();

        _thread = null;
        _threadId = 0;
        IsRegistered = false;
    }

    public void Dispose()
    {
        Unregister();
    }
}
