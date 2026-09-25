namespace ShareHider;

/// <summary>A small local log in %LOCALAPPDATA%\ShareHider, rotated at 1 MB. Logging never throws.</summary>
internal static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly Lock Gate = new();

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShareHider");

    public static string FilePath { get; } = Path.Combine(Directory, "log.txt");

    /// <summary>Diagnostics output; see TrayApp.WriteWindowList.</summary>
    public static string WindowListPath { get; } = Path.Combine(Directory, "window-list.txt");

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Move(FilePath, Path.Combine(Directory, "log.old.txt"), overwrite: true);
                }

                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Nowhere left to report it; the app keeps working without a log.
            }
        }
    }
}
