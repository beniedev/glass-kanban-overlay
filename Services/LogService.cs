namespace DesktopOverlayBoard.Services;

public static class LogService
{
    public static void Error(Exception ex, string message) => Write("ERROR", $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            var path = Path.Combine(AppPaths.LogDirectory, $"{DateTime.Now:yyyy-MM-dd}.log");
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss} [{level}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never break the desktop widget.
        }
    }
}
