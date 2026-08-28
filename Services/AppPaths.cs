namespace DesktopOverlayBoard.Services;

public static class AppPaths
{
    public static string RootDirectory
    {
        get
        {
            var configuredHome = Environment.GetEnvironmentVariable("GLASS_KANBAN_OVERLAY_HOME");
            if (!string.IsNullOrWhiteSpace(configuredHome) && Directory.Exists(configuredHome))
            {
                return configuredHome;
            }

            var currentDirectory = Directory.GetCurrentDirectory();
            if (File.Exists(Path.Combine(currentDirectory, "DesktopOverlayBoard.csproj")) ||
                Directory.Exists(Path.Combine(currentDirectory, "Data")))
            {
                return currentDirectory;
            }

            return AppContext.BaseDirectory;
        }
    }

    public static string DataDirectory => Path.Combine(RootDirectory, "Data");
    public static string LogDirectory => Path.Combine(RootDirectory, "Log");
    public static string ConfigPath => Path.Combine(DataDirectory, "config.json");
}
