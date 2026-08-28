using Microsoft.Win32;

namespace DesktopOverlayBoard.Services;

public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "GlassKanbanOverlay";
    private const string LegacyAppName = "DesktopOverlayBoard";
    private const string StartupArg = "--startup";

    public static void ApplyStartWithWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null)
        {
            return;
        }

        if (!enabled)
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
            DeleteLegacyRunValueIfOwned(key);
            return;
        }

        var exe = ResolveStartupExecutable();

        DeleteLegacyRunValueIfOwned(key);
        key.SetValue(AppName, $"\"{exe}\" {StartupArg}");
    }

    public static bool IsStartWithWindowsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return HasRunValue(key, AppName) || HasRunValue(key, LegacyAppName);
    }

    private static bool HasRunValue(RegistryKey? key, string name)
    {
        return key?.GetValue(name) is string value && !string.IsNullOrWhiteSpace(value);
    }

    private static void DeleteLegacyRunValueIfOwned(RegistryKey key)
    {
        if (key.GetValue(LegacyAppName) is not string value || !LooksLikeThisAppStartupValue(value))
        {
            return;
        }

        key.DeleteValue(LegacyAppName, throwOnMissingValue: false);
    }

    private static bool LooksLikeThisAppStartupValue(string value)
    {
        return value.Contains("GlassKanbanOverlay", StringComparison.OrdinalIgnoreCase)
            || value.Contains("glass-kanban-overlay", StringComparison.OrdinalIgnoreCase)
            || value.Contains(AppPaths.RootDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveStartupExecutable()
    {
        var portableExe = Path.Combine(AppPaths.RootDirectory, "dist", "GlassKanbanOverlay-win-x64-portable", "GlassKanbanOverlay.exe");
        if (File.Exists(portableExe))
        {
            return portableExe;
        }

        var flatDistExe = Path.Combine(AppPaths.RootDirectory, "dist", "GlassKanbanOverlay.exe");
        if (File.Exists(flatDistExe))
        {
            return flatDistExe;
        }

        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath)
            && !string.Equals(Path.GetFileName(Environment.ProcessPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return Environment.ProcessPath;
        }

        return Path.Combine(AppContext.BaseDirectory, "GlassKanbanOverlay.exe");
    }
}
