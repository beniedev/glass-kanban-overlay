using DesktopOverlayBoard.Models;
using DesktopOverlayBoard.Services;

namespace DesktopOverlayBoard.Application;

public sealed record SettingsCommitResult(
    bool Saved, Exception? SaveError = null, StartupApplyResult? Startup = null,
    Exception? WindowError = null, Exception? RefreshError = null, bool RefreshDeferred = false);

public sealed class SettingsWorkflow(Action<AppConfig> save, Func<bool, StartupApplyResult> applyStartup)
{
    public async Task<SettingsCommitResult> CommitAsync(
        AppConfig draft, Action<AppConfig> publish, Func<Task> refresh)
    {
        try { save(draft); }
        catch (Exception error) { return new(false, SaveError: error); }

        // All retained windows receive the saved reference before any asynchronous refresh.
        try { publish(draft); }
        catch (Exception error) { return new(true, WindowError: error); }

        StartupApplyResult startup;
        try { startup = applyStartup(draft.Startup.StartWithWindows); }
        catch (Exception error) { startup = new(false, error.Message); }

        try { await refresh(); }
        catch (WindowRefreshDeferredException pending)
        {
            return new(true, Startup: startup, RefreshError: pending.RefreshError, RefreshDeferred: true);
        }
        catch (Exception error) { return new(true, Startup: startup, RefreshError: error); }
        return new(true, Startup: startup);
    }
}
