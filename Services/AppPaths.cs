namespace DesktopOverlayBoard.Services;

/// <summary>Paths captured at composition time, independent of later environment or CWD changes.</summary>
public sealed record ResolvedAppPaths
{
    internal ResolvedAppPaths(string rootDirectory)
    {
        RootDirectory = rootDirectory;
        DataDirectory = Path.Combine(rootDirectory, "Data");
        LogDirectory = Path.Combine(rootDirectory, "Log");
        ConfigPath = Path.Combine(DataDirectory, "config.json");
    }

    public string RootDirectory { get; }
    public string DataDirectory { get; }
    public string LogDirectory { get; }
    public string ConfigPath { get; }
}

public static class AppPaths
{
    public static ResolvedAppPaths FromRoot(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        if (!Path.IsPathFullyQualified(rootDirectory))
        {
            throw new ArgumentException("An explicit absolute application root is required.", nameof(rootDirectory));
        }
        return new ResolvedAppPaths(Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory)));
    }

    public static ResolvedAppPaths ResolveDefault() => Resolve(
        Environment.GetEnvironmentVariable("GLASS_KANBAN_OVERLAY_HOME"),
        Directory.GetCurrentDirectory(),
        AppContext.BaseDirectory);

    public static ResolvedAppPaths Resolve(string? configuredHome, string currentDirectory, string baseDirectory)
    {
        var current = FromRoot(currentDirectory);
        if (!string.IsNullOrWhiteSpace(configuredHome))
        {
            try
            {
                var home = Path.GetFullPath(configuredHome, current.RootDirectory);
                if (Directory.Exists(home)) return FromRoot(home);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Directory.Exists previously rejected invalid home values and fell back to CWD/base.
            }
        }

        if (File.Exists(Path.Combine(current.RootDirectory, "DesktopOverlayBoard.csproj")) ||
            Directory.Exists(current.DataDirectory))
        {
            return current;
        }
        return FromRoot(baseDirectory);
    }
}
