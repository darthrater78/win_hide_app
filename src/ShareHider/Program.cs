namespace ShareHider;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // One instance per user session: two copies would fight over the same taskbar buttons.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\ShareHider.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        using var app = new TrayApp();

        // Never leave other apps' buttons hidden: restore on logoff/shutdown too.
        Microsoft.Win32.SystemEvents.SessionEnding += (_, _) => app.ExitThread();

        Application.Run(app);
    }
}
