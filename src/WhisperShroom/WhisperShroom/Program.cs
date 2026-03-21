using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace WhisperShroom;

public static partial class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        // Global crash handlers so startup failures are never silent
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            ShowFatalError($"Unhandled exception: {ex?.Message}\n\n{ex?.StackTrace}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            ShowFatalError($"Unobserved task exception: {e.Exception.Message}\n\n{e.Exception.StackTrace}");
        };

        try
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
        catch (Exception ex)
        {
            ShowFatalError($"Startup failed: {ex.Message}\n\n{ex.StackTrace}");
            return 1;
        }
    }

    private static void ShowFatalError(string message)
    {
        // Log to file in AppData for post-mortem analysis
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WhisperShroom");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "crash.log");
            File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* last resort - can't even write log */ }

        // Show a native Win32 message box (works even if WinUI hasn't initialized)
        NativeMessageBox(message);
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf16)]
    private static partial int NativeMessageBox(nint hWnd, string text, string caption, uint type);

    private static void NativeMessageBox(string message)
    {
        const uint MB_OK = 0x00000000;
        const uint MB_ICONERROR = 0x00000010;
        NativeMessageBox(0, message, "WhisperShroom - Fatal Error", MB_OK | MB_ICONERROR);
    }
}
