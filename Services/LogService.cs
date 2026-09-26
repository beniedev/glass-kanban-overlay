namespace DesktopOverlayBoard.Services;

public static class LogService
{
    private static ResolvedAppPaths? _paths;

    public static void Initialize(ResolvedAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
    }

    public static void Error(Exception ex, string message) => Write("ERROR", $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            var paths = _paths;
            if (paths is null) return;
            Directory.CreateDirectory(paths.LogDirectory);
            var path = Path.Combine(paths.LogDirectory, $"{DateTime.Now:yyyy-MM-dd}.log");
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss} [{level}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never break the desktop widget.
        }
    }
}
