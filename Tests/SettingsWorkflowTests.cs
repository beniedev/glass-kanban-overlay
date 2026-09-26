using DesktopOverlayBoard.Application;
using DesktopOverlayBoard.Models;
using DesktopOverlayBoard.Services;
using static DesktopOverlayBoard.Tests.TestAssert;

namespace DesktopOverlayBoard.Tests;

internal static class SettingsWorkflowTests
{
    public static async Task RunAsync(string root)
    {
        await TestPublishBeforeAwaitAsync(root);
        await TestSaveFailureAsync(root);
        await TestUnavailableStartupAsync(root);
        await TestThrownStartupAsync(root);
        await TestRefreshFailureAsync(root);
        await TestWindowFailureAsync(root);
        await TestCreatedFileSurvivesConfigFailureAsync(root);
        TestLocalizedFailures();
    }

    private static ConfigService Service(string root, string name) => new(AppPaths.FromRoot(Path.Combine(root, name)));

    private static async Task TestPublishBeforeAwaitAsync(string root)
    {
        var service = Service(root, "publish-order");
        var active = new AppConfig { UiLanguage = "en" };
        service.Save(active);
        var summary = active;
        var single = active;
        var draft = active.Clone();
        draft.UiLanguage = "zh";
        var events = new List<string>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = new SettingsWorkflow(config => { service.Save(config); events.Add("save"); }, enabled =>
        {
            Assert(ReferenceEquals(summary, draft) && ReferenceEquals(single, draft),
                "startup must see both windows on the saved config before refresh awaits");
            events.Add("startup");
            return new(true);
        });
        var pending = workflow.CommitAsync(draft, config =>
        {
            events.Add("publish"); summary = config; single = config;
        }, () => { events.Add("refresh"); return gate.Task; });
        Assert(!pending.IsCompleted, "commit must await actual refresh completion");
        Assert(events.SequenceEqual(new[] { "save", "publish", "startup", "refresh" }), "settings commit order changed");
        Assert(active.UiLanguage == "en", "editing and saving a draft must not mutate the prior active object");
        gate.SetResult();
        var result = await pending;
        Assert(result.Saved && result.Startup?.Success == true && result.RefreshError is null, "complete commit should report success");
        Assert(service.Load().UiLanguage == "zh", "reported save must be on disk");
    }

    private static async Task TestSaveFailureAsync(string root)
    {
        var paths = AppPaths.FromRoot(Path.Combine(root, "failed-save"));
        var active = new AppConfig { UiLanguage = "en" };
        new ConfigService(paths).Save(active);
        var original = File.ReadAllBytes(paths.ConfigPath);
        var error = new IOException("synthetic publication failure");
        var service = new ConfigService(paths, ConfigService.WriteTemporaryFile, (_, _, _) => throw error);
        var draft = active.Clone(); draft.UiLanguage = "zh";
        var summary = active; var single = active;
        var startupCalls = 0; var refreshCalls = 0;
        var workflow = new SettingsWorkflow(service.Save, _ => { startupCalls++; return new(true); });
        var result = await workflow.CommitAsync(draft, config => { summary = config; single = config; },
            () => { refreshCalls++; return Task.CompletedTask; });
        Assert(!result.Saved && ReferenceEquals(result.SaveError, error), "save failure must preserve the actual failure receipt");
        Assert(ReferenceEquals(summary, active) && ReferenceEquals(single, active), "failed save must not publish a draft");
        Assert(startupCalls == 0 && refreshCalls == 0, "failed save must not apply startup or refresh windows");
        Assert(File.ReadAllBytes(paths.ConfigPath).SequenceEqual(original), "failed settings save must preserve old JSON");
    }

    private static async Task TestUnavailableStartupAsync(string root)
    {
        var service = Service(root, "unavailable-startup");
        var draft = new AppConfig { UiLanguage = "zh" };
        AppConfig? active = null;
        var refreshed = false;
        var workflow = new SettingsWorkflow(service.Save, _ => new(false, "synthetic unavailable"));
        var result = await workflow.CommitAsync(draft, config => active = config,
            () => { refreshed = true; return Task.CompletedTask; });
        Assert(result.Saved && result.Startup is { Success: false }, "startup unavailable without an exception is a partial result");
        Assert(ReferenceEquals(active, draft) && refreshed, "saved config remains active and refreshes after startup failure");
        Assert(service.Load().UiLanguage == "zh", "startup failure must not roll back saved configuration");
    }

    private static async Task TestThrownStartupAsync(string root)
    {
        var service = Service(root, "thrown-startup");
        var refreshed = false;
        var workflow = new SettingsWorkflow(service.Save, _ => throw new UnauthorizedAccessException("synthetic startup denied"));
        var result = await workflow.CommitAsync(new AppConfig(), _ => { },
            () => { refreshed = true; return Task.CompletedTask; });
        Assert(result.Saved && result.Startup is { Success: false } && refreshed, "thrown startup failure must also remain a partial saved result");
    }

    private static async Task TestRefreshFailureAsync(string root)
    {
        var service = Service(root, "refresh-failure");
        var error = new IOException("synthetic refresh failure");
        var workflow = new SettingsWorkflow(service.Save, _ => new(true));
        var result = await workflow.CommitAsync(new AppConfig(), _ => { }, () => Task.FromException(error));
        Assert(result.Saved && result.Startup?.Success == true && ReferenceEquals(result.RefreshError, error),
            "refresh failure must not be reported as failed persistence");
    }

    private static async Task TestWindowFailureAsync(string root)
    {
        var service = Service(root, "window-failure");
        var error = new InvalidOperationException("synthetic window update failure");
        var calls = 0;
        var workflow = new SettingsWorkflow(service.Save, _ => { calls++; return new(true); });
        var result = await workflow.CommitAsync(new AppConfig(), _ => throw error,
            () => { calls++; return Task.CompletedTask; });
        Assert(result.Saved && ReferenceEquals(result.WindowError, error) && calls == 0,
            "window publication failure must report saved config without claiming later effects");
    }

    private static async Task TestCreatedFileSurvivesConfigFailureAsync(string root)
    {
        var draft = new AppConfig();
        var path = Path.Combine(root, "created-before-save.md");
        var setup = new BoardSetupWorkflow(new MarkdownKanbanService());
        var prepared = setup.PrepareNew(draft, () => KanbanBoardTemplate.TodoDoingDone, () => path);
        Assert(prepared.FileCreated && prepared.Board is not null, "setup must report the actual created file");
        draft.Boards.Add(prepared.Board!);
        var original = File.ReadAllBytes(path);
        var workflow = new SettingsWorkflow(_ => throw new IOException("synthetic config failure"), _ => new(true));
        var result = await workflow.CommitAsync(draft, _ => throw new InvalidOperationException("must not publish"),
            () => throw new InvalidOperationException("must not refresh"));
        Assert(!result.Saved && draft.Boards.Count == 1, "failed commit must leave the created board in the editable draft");
        Assert(File.ReadAllBytes(path).SequenceEqual(original), "config failure must not delete or change the created Markdown");
    }

    private static void TestLocalizedFailures()
    {
        foreach (var language in new[] { "en", "zh" })
        {
            LocalizationService.Use(language);
            foreach (var key in new[] { "Dialog.SystemSettingFailed", "Dialog.ConfigurationSaved", "Error.StartupUnavailable",
                "Message.ConfigurationLoadFailed", "Message.ConfigurationSaveFailed", "Message.CreatedBoardConfigFailed",
                "Message.StartupReadFailed", "Message.StartupApplyFailed", "Message.SavedStartupFailed",
                "Message.SavedWindowsFailed", "Message.SavedRefreshFailed", "Message.OperationFailed" })
            {
                Assert(LocalizationService.Text(key, "synthetic", "failure") != key, "failure receipt needs a translation: " + key);
            }
        }
        LocalizationService.Use("auto");
    }
}
