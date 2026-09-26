using System.Diagnostics;
using System.Windows;

namespace ShareHider;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // One instance per user session: two copies would fight over the same taskbar buttons.
        // A second launch just asks the running one to show its window.
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ShareHider.Show");
        using var mutex = new Mutex(initiallyOwned: true, @"Local\ShareHider.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            // This launch came from the user, so it may bring a window to the front; the running
            // copy may not. Pass the right on, or its window opens behind others as a taskbar button.
            using var self = Process.GetCurrentProcess();
            foreach (var other in Process.GetProcessesByName(self.ProcessName))
            {
                if (other.Id != Environment.ProcessId && other.SessionId == self.SessionId)
                {
                    Native.AllowSetForegroundWindow((uint)other.Id);
                }

                other.Dispose();
            }

            showSignal.Set();
            return;
        }

        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
            ThemeMode = ThemeMode.System, // Windows 11 Fluent look, following the light/dark setting
        };

        using var controller = new Controller();
        var window = new MainWindow(controller);
        void Exit()
        {
            window.PrepareExit();
            controller.RestoreInPlace();
            app.Shutdown();
        }

        using var tray = new TrayIcon(controller, window.ShowAndActivate, Exit);
        var showWait = ThreadPool.RegisterWaitForSingleObject(
            showSignal, (_, _) => app.Dispatcher.BeginInvoke(window.ShowAndActivate), null, Timeout.Infinite, executeOnlyOnce: false);

        // Never leave other apps' buttons hidden: restore on logoff and shutdown too.
        app.SessionEnding += (_, _) => controller.Dispose();

        // An unexpected error must not strand hidden buttons: restore everything, say so, and exit.
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Write($"unexpected error: {e.Exception}");
            controller.Dispose();
            e.Handled = true;
            MessageBox.Show(
                $"ShareHider hit an unexpected error and has restored all hidden apps.\n\n{e.Exception.Message}\n\nDetails are in {Log.FilePath}.",
                "ShareHider", MessageBoxButton.OK, MessageBoxImage.Error);
            Exit();
        };

        // Every start opens the window, including at sign-in; closing it leaves ShareHider in the tray.
        window.ShowAndActivate();

        app.Run();
        showWait.Unregister(null);
        // controller.Dispose (via using) restores every hidden button and window.
    }
}
