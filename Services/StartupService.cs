using Microsoft.Win32;

namespace DesktopOverlayBoard.Services;

public sealed record StartupApplyResult(bool Success, string? Error = null);
public sealed record StartupReadResult(bool Available, bool Enabled, string? Error = null);

internal interface IStartupRunKey : IDisposable
{
    object? GetValue(string name);
    void SetValue(string name, string value);
    void DeleteValue(string name);
}

internal interface IStartupRunKeyAccess
{
    IStartupRunKey? Open(bool writable);
}

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "GlassKanbanOverlay";
    private const string LegacyAppName = "DesktopOverlayBoard";
    private const string StartupArg = "--startup";
    private readonly ResolvedAppPaths _paths;
    private readonly IStartupRunKeyAccess _runKeys;
    private readonly string? _processPath;
    private readonly string _baseDirectory;

    public StartupService(ResolvedAppPaths paths)
        : this(paths, new RegistryRunKeyAccess(), Environment.ProcessPath, AppContext.BaseDirectory) { }

    internal StartupService(ResolvedAppPaths paths, IStartupRunKeyAccess runKeys,
        string? processPath, string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(runKeys);
        _paths = paths;
        _runKeys = runKeys;
        _processPath = processPath;
        _baseDirectory = baseDirectory;
    }

    public StartupApplyResult ApplyStartWithWindows(bool enabled)
    {
        try
        {
            using var key = _runKeys.Open(writable: true);
            if (key is null) return new(false, LocalizationService.Text("Error.StartupUnavailable"));

            if (!enabled)
            {
                key.DeleteValue(AppName);
                DeleteLegacyRunValueIfOwned(key);
            }
            else
            {
                var exe = ResolveStartupExecutable();
                DeleteLegacyRunValueIfOwned(key);
                key.SetValue(AppName, $"\"{exe}\" {StartupArg}");
            }
            return new(true);
        }
        catch (Exception error)
        {
            LogService.Error(error, "Startup setting apply failed.");
            return new(false, error.Message);
        }
    }

    public StartupReadResult ReadStartWithWindows()
    {
        try
        {
            using var key = _runKeys.Open(writable: false);
            if (key is null) return new(false, false, LocalizationService.Text("Error.StartupUnavailable"));
            return new(true, HasRunValue(key, AppName) || HasRunValue(key, LegacyAppName));
        }
        catch (Exception error)
        {
            LogService.Error(error, "Startup setting read failed.");
            return new(false, false, error.Message);
        }
    }

    private static bool HasRunValue(IStartupRunKey key, string name) =>
        key.GetValue(name) is string value && !string.IsNullOrWhiteSpace(value);

    private void DeleteLegacyRunValueIfOwned(IStartupRunKey key)
    {
        if (key.GetValue(LegacyAppName) is string value &&
            (value.Contains("GlassKanbanOverlay", StringComparison.OrdinalIgnoreCase) ||
             value.Contains("glass-kanban-overlay", StringComparison.OrdinalIgnoreCase) ||
             value.Contains(_paths.RootDirectory, StringComparison.OrdinalIgnoreCase)))
        {
            key.DeleteValue(LegacyAppName);
        }
    }

    internal string ResolveStartupExecutable()
    {
        var portableExe = Path.Combine(_paths.RootDirectory, "dist", "GlassKanbanOverlay-win-x64-portable", "GlassKanbanOverlay.exe");
        if (File.Exists(portableExe)) return portableExe;
        var flatDistExe = Path.Combine(_paths.RootDirectory, "dist", "GlassKanbanOverlay.exe");
        if (File.Exists(flatDistExe)) return flatDistExe;
        if (!string.IsNullOrWhiteSpace(_processPath) &&
            !string.Equals(Path.GetFileName(_processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return _processPath;
        }
        return Path.Combine(_baseDirectory, "GlassKanbanOverlay.exe");
    }

    private sealed class RegistryRunKeyAccess : IStartupRunKeyAccess
    {
        public IStartupRunKey? Open(bool writable)
        {
            var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable);
            return key is null ? null : new RegistryRunKey(key);
        }
    }

    private sealed class RegistryRunKey(RegistryKey key) : IStartupRunKey
    {
        public object? GetValue(string name) => key.GetValue(name);
        public void SetValue(string name, string value) => key.SetValue(name, value);
        public void DeleteValue(string name) => key.DeleteValue(name, throwOnMissingValue: false);
        public void Dispose() => key.Dispose();
    }
}
