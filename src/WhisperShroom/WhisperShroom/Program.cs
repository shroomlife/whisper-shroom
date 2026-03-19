using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace WhisperShroom;

public static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // Single-instance: register or find existing
        var key = AppInstance.FindOrRegisterForKey("WhisperShroom");

        if (!key.IsCurrent)
        {
            // Another instance is running - redirect activation and exit
            var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            await key.RedirectActivationToAsync(activationArgs);
            return 0;
        }

        // We are the main instance
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });

        return 0;
    }
}
