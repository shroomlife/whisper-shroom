using System.Diagnostics;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using WhisperShroom.Helpers;

namespace WhisperShroom.Services;

public sealed class NotificationService : IDisposable
{
    private readonly AppNotificationManager _manager;
    private bool _registered;

    public NotificationService()
    {
        _manager = AppNotificationManager.Default;
        _manager.NotificationInvoked += OnNotificationInvoked;
    }

    public void Register()
    {
        try
        {
            _manager.Register();
            _registered = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NotificationService] Register failed: {ex.Message}");
            _registered = false;
        }
    }

    public void ShowTranscriptionResult(string text)
    {
        if (!_registered) return;

        try
        {
            var preview = text.Length > 200 ? text[..200] + "..." : text;
            var argText = text.Length > 1500 ? text[..1500] : text;

            var notification = new AppNotificationBuilder()
                .AddText("Transcription Complete")
                .AddText(preview)
                .AddArgument("action", "copyText")
                .AddArgument("text", argText)
                .BuildNotification();

            _manager.Show(notification);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NotificationService] Show failed: {ex.Message}");
        }
    }

    private void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        if (args.Arguments.TryGetValue("action", out var action) && action == "copyText"
            && args.Arguments.TryGetValue("text", out var text))
        {
            App.MainAppWindow.DispatcherQueue.TryEnqueue(() =>
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
                ClipboardHelper.CopyToClipboard(text, hWnd);
            });
        }
    }

    public void Dispose()
    {
        if (_registered)
        {
            try { _manager.Unregister(); }
            catch { /* shutting down */ }
        }
    }
}
