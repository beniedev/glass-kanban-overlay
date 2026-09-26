using DesktopOverlayBoard.Application;
using DesktopOverlayBoard.Models;
using DesktopOverlayBoard.Services;
using System.Text;
using static DesktopOverlayBoard.Tests.TestAssert;

namespace DesktopOverlayBoard.Tests;

internal static class WindowRefreshTests
{
    public static async Task RunAsync(string root)
    {
        await CharacterizeLegacyReadOrderingAsync();
        await CharacterizeLegacyDraftQueueAsync();
        await CharacterizeLegacyLoadedCloseAsync();
        CharacterizeLegacyAddTargetPathDrift(root);
        await TestSingleReadAndCoalescedRequestsAsync();
        await TestConfigReferenceAndBoardIdentityAsync();
        await TestDraftDefersWithoutFalseSuccessAsync();
        await TestDraftRoundTripInvalidatesEarlierReadAsync();
        await TestModalCompletionDoesNotEndLaterDraftAsync();
        await TestModalCommitDefersWithoutHoldingDraftAsync();
        await TestAllWindowsRefreshKeepsFailureAndPendingAsync();
        TestDeferredBackgroundFailureIsObserved();
        await TestFailureIsObservableAndRetryIsExplicitAsync();
        await TestApplyFailureIsObservableAsync();
        await TestLateFailureBelongsToOldTargetAsync();
        await TestCloseSettlesWaitersAndLoadedCannotRestartAsync();
        TestEndingDraftWithoutPendingDoesNotRead();
        TestDraftIdentityAllowsSameTargetConfigReplacement();
        Console.WriteLine("WindowRefreshTests: 4 controlled legacy characterizations and 14 coordinator scenarios passed");
    }

    // These characterize the old algorithm's await boundaries, not an observed
    // real-window incident: both ReloadAsync methods checked draft before/after
    // await, then applied directly. Single Loaded started its timer after await.
    private sealed class LegacyGate
    {
        public bool ShouldDefer { get; private set; }
        public bool HasPendingRefresh { get; private set; }
        public void BeginDraft() => ShouldDefer = true;
        public void Defer() => HasPendingRefresh = true;
    }

    private static async Task LegacyReloadAsync(LegacyGate gate, Func<Task<int>> read, Action<int> apply)
    {
        if (gate.ShouldDefer) { gate.Defer(); return; }
        var result = await read();
        if (gate.ShouldDefer) { gate.Defer(); return; }
        apply(result);
    }

    private static async Task CharacterizeLegacyReadOrderingAsync()
    {
        var gate = new LegacyGate();
        var first = new TaskCompletionSource<int>();
        var second = new TaskCompletionSource<int>();
        var visible = 0;
        var earlier = LegacyReloadAsync(gate, () => first.Task, value => visible = value);
        var later = LegacyReloadAsync(gate, () => second.Task, value => visible = value);
        second.SetResult(2);
        await later;
        first.SetResult(1);
        await earlier;
        Assert(visible == 1, "controlled old algorithm should demonstrate late older read replacing newer result");
    }

    private static async Task CharacterizeLegacyDraftQueueAsync()
    {
        var gate = new LegacyGate();
        gate.BeginDraft();
        var applied = false;
        var request = LegacyReloadAsync(gate, () => Task.FromResult(1), _ => applied = true);
        await request;
        Assert(request.IsCompleted && !applied && gate.HasPendingRefresh,
            "controlled old algorithm completes await after only queueing, with no actual refresh");
    }

    private static async Task CharacterizeLegacyLoadedCloseAsync()
    {
        var read = new TaskCompletionSource<int>();
        var timerStarted = false;
        async Task LoadedAsync()
        {
            await LegacyReloadAsync(new LegacyGate(), () => read.Task, _ => { });
            timerStarted = true;
        }
        var loaded = LoadedAsync();
        timerStarted = false; // Closing stopped the timer while Loaded awaited.
        read.SetResult(1);
        await loaded;
        Assert(timerStarted, "controlled old Loaded ordering restarts timer after the earlier close stop");
    }

    private static void CharacterizeLegacyAddTargetPathDrift(string root)
    {
        var directory = Path.Combine(root, "synthetic-target-drift");
        Directory.CreateDirectory(directory);
        var oldPath = Path.Combine(directory, "board-a.md");
        var newPath = Path.Combine(directory, "board-b.md");
        const string source = "## Todo\n\n- [ ] Existing synthetic task\n\n## Done\n";
        File.WriteAllText(oldPath, source, new UTF8Encoding(false));
        File.WriteAllText(newPath, source, new UTF8Encoding(false));
        var service = new MarkdownKanbanService();
        var oldBoard = Board("board-a", oldPath, "Todo");
        var oldHash = service.LoadGroup(oldBoard, incompleteOnly: false).ColumnRangeHash;
        var identity = WindowBoardIdentity.Capture(oldBoard);
        var changedBoard = Board("board-a", newPath, "Todo");
        const string input = "Synthetic retained draft";

        var result = service.AddTask(changedBoard, changedBoard.DefaultColumn, oldHash, input);
        Assert(result.Success && File.ReadAllText(oldPath) == source && File.ReadAllText(newPath).Contains(input),
            "controlled old AddTask(new board, old hash) chain can write another identical source despite the hash check");
        Assert(!identity.Matches(changedBoard), "draft identity must detect path drift that an equal column hash cannot reject");
    }

    private static async Task TestSingleReadAndCoalescedRequestsAsync()
    {
        var fixture = new Fixture();
        var first = fixture.Coordinator.RequestAsync();
        var second = fixture.Coordinator.RequestAsync();
        var third = fixture.Coordinator.RequestAsync();
        Assert(fixture.Reads.Count == 1 && fixture.MaxActiveReads == 1, "concurrent requests must share one active read");
        Assert(!second.IsCompleted && !third.IsCompleted, "queued awaiters must not complete before their read");
        fixture.Complete(0);
        Assert((await first).Status == WindowRefreshStatus.Applied, "first requester should await actual apply");
        Assert(fixture.Reads.Count == 2 && fixture.MaxActiveReads == 1, "two queued requests must produce one additional read");
        fixture.Complete(1);
        Assert((await second).Status == WindowRefreshStatus.Applied && (await third).Status == WindowRefreshStatus.Applied,
            "both merged callers should observe actual second apply");
        Assert(fixture.Reads.Count == 2 && fixture.Applied.Count == 2 && !fixture.Coordinator.HasPending,
            "coalesced queue should drain once without extra read");
    }

    private static async Task TestConfigReferenceAndBoardIdentityAsync()
    {
        foreach (var change in new Action<Fixture>[]
        {
            fixture => fixture.Config = new AppConfig { Boards = fixture.Config.Boards },
            fixture => fixture.Config.Boards[0].Id = "board-b",
            fixture => fixture.Config.Boards[0].FilePath = "board-b.md",
            fixture => fixture.Config.Boards[0].DefaultColumn = "Doing",
            fixture => fixture.Config.Boards.Add(Board("board-b", "board-b.md", "Doing")),
        })
        {
            var fixture = new Fixture();
            var request = fixture.Coordinator.RequestAsync();
            var original = fixture.Reads[0].Boards[0];
            change(fixture);
            fixture.Complete(0);
            Assert(fixture.Applied.Count == 0 && fixture.Reads.Count == 2 && !request.IsCompleted,
                "a changed config reference or target identity must discard old result and read current target");
            Assert(original.Id == "board-a" && original.FilePath == "board-a.md" && original.DefaultColumn == "Todo",
                "reader must receive captured board values instead of mutable active board");
            fixture.Complete(1);
            Assert((await request).Status == WindowRefreshStatus.Applied && fixture.Applied.Count == 1,
                "original caller must settle only after the current target actually applies");
        }
    }

    private static async Task TestDraftDefersWithoutFalseSuccessAsync()
    {
        var fixture = new Fixture();
        var beforeDraft = fixture.Coordinator.RequestAsync();
        fixture.Gate.BeginDraft();
        fixture.Coordinator.NotifyDraftStarted();
        fixture.Complete(0);
        var duringDraft = fixture.Coordinator.RequestAsync();
        Assert(fixture.Coordinator.HasPending && fixture.Reads.Count == 1 && fixture.Applied.Count == 0,
            "active draft must preserve controls and defer a current read");
        Assert((await beforeDraft).Status == WindowRefreshStatus.Deferred && (await duringDraft).Status == WindowRefreshStatus.Deferred,
            "draft-blocked requests must explicitly report Deferred, never a fake successful apply");
        fixture.Gate.EndDraft();
        _ = fixture.Coordinator.NotifyDraftEnded();
        Assert(fixture.Reads.Count == 2, "ending draft should launch just the existing merged pending read");
        fixture.Complete(1);
        Assert(fixture.Applied.Count == 1 && !fixture.Coordinator.HasPending,
            "pending must apply once after draft ends without retaining its earlier completed callers");
    }

    private static async Task TestDraftRoundTripInvalidatesEarlierReadAsync()
    {
        var fixture = new Fixture();
        var request = fixture.Coordinator.RequestAsync();
        fixture.Gate.BeginDraft();
        fixture.Coordinator.NotifyDraftStarted();
        fixture.Gate.EndDraft();
        _ = fixture.Coordinator.NotifyDraftEnded();
        fixture.Complete(0);
        Assert(fixture.Applied.Count == 0 && fixture.Reads.Count == 2,
            "a draft that already ended must still invalidate the result captured before it started");
        fixture.Complete(1);
        Assert((await request).Status == WindowRefreshStatus.Deferred && fixture.Applied.Count == 1,
            "draft-start notification must report defer while only the fresh background result applies");
    }

    // Calls the same per-modal completion closure as MainWindow.EditTaskAsync.
    // The read and draft transition are controlled; this is not a real modal
    // user interaction or an assertion about when a WPF dialog is dismissed.
    private static async Task TestModalCompletionDoesNotEndLaterDraftAsync()
    {
        foreach (var saved in new[] { true, false })
        {
            var fixture = new Fixture();
            fixture.Gate.BeginDraft();
            fixture.Coordinator.NotifyDraftStarted();
            if (!saved) await fixture.Coordinator.RequestAsync();
            var completions = 0;
            var completeDraft = MainWindow.CreateModalDraftCompletion(async forceRefresh =>
            {
                completions++;
                fixture.Gate.EndDraft();
                var refresh = fixture.Coordinator.NotifyDraftEnded();
                if (forceRefresh) refresh ??= fixture.Coordinator.RequestAsync();
                if (refresh is not null) await refresh;
            });

            var previousCompletion = completeDraft(saved);
            Assert(completions == 1 && fixture.Reads.Count == 1 && !previousCompletion.IsCompleted,
                "saved or canceled modal must end its own draft before awaiting the one actual pending read");
            fixture.Gate.BeginDraft();
            fixture.Coordinator.NotifyDraftStarted();
            await previousCompletion;
            await completeDraft(false); // EditTaskAsync's finally runs after its success/cancel wait.
            Assert(completions == 1 && fixture.Gate.HasActiveDraft && fixture.Coordinator.HasPending,
                "old modal finally must not end a later draft or clear its pending protection");
            fixture.Complete(0);
            Assert(fixture.Applied.Count == 0 && fixture.Reads.Count == 1,
                "late modal read must not apply over the new draft while it remains active");
            fixture.Gate.EndDraft();
            var resumed = fixture.Coordinator.NotifyDraftEnded();
            fixture.Complete(1);
            Assert(resumed is not null && (await resumed).Status == WindowRefreshStatus.Applied &&
                fixture.Applied.Count == 1 && !fixture.Coordinator.HasPending,
                "only the later draft's own end may consume the retained pending refresh");
        }
    }

    private static async Task TestModalCommitDefersWithoutHoldingDraftAsync()
    {
        var fixture = new Fixture();
        fixture.Gate.BeginDraft();
        fixture.Coordinator.NotifyDraftStarted();
        var input = "Keep synthetic unsaved text";
        var persisted = false;
        var saving = true;
        var refreshDeferred = false;
        var workflow = new SettingsWorkflow(_ => persisted = true, _ => new(true));
        SettingsCommitResult? result = null;
        async Task CommitSettingsAsync()
        {
            try
            {
                result = await workflow.CommitAsync(
                    new AppConfig { Boards = [Board("board-b", "board-b.md", "Doing")] },
                    saved => fixture.Config = saved, async () =>
                    {
                        var refresh = await fixture.Coordinator.RequestAsync();
                        refresh.ThrowIfFailedOrDeferred();
                    });
                refreshDeferred = result.RefreshDeferred;
            }
            finally
            {
                saving = false;
            }
        }

        await CommitSettingsAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert(persisted && refreshDeferred && !saving && fixture.Reads.Count == 0,
            "settings modal must settle saved/deferred instead of holding saving until its disabled owner ends draft");
        Assert(input == "Keep synthetic unsaved text" && fixture.Gate.HasActiveDraft && fixture.Coordinator.HasPending,
            "deferred modal commit must keep input and active draft while coordinator alone retains pending");
        Assert(result is { Saved: true, RefreshDeferred: true, RefreshError: null },
            "actual settings workflow must report saved/deferred without converting it into a refresh error");
        fixture.Gate.EndDraft();
        var resumed = fixture.Coordinator.NotifyDraftEnded();
        Assert(fixture.Reads.Count == 1 && fixture.Reads[0].Boards[0].Id == "board-b",
            "after modal closes and draft ends pending must read saved current config");
        fixture.Complete(0);
        Assert(resumed is not null && (await resumed).Status == WindowRefreshStatus.Applied,
            "draft-end consumer must expose the actual pending apply task");
        Assert(fixture.Applied.Count == 1 && !fixture.Coordinator.HasPending && fixture.Failures.Count == 0,
            "saved/deferred refresh must consume once without inventing a failure");
    }

    private static async Task TestAllWindowsRefreshKeepsFailureAndPendingAsync()
    {
        var blocked = new Fixture();
        var ready = new Fixture();
        blocked.Gate.BeginDraft();
        blocked.Coordinator.NotifyDraftStarted();
        var error = new IOException("Synthetic sibling-window refresh failure");
        var workflow = new SettingsWorkflow(_ => { }, _ => new(true));
        var commit = workflow.CommitAsync(new AppConfig(), _ => { }, () =>
            WindowRefreshCoordinator.RefreshAllAsync(new Func<Task>[]
            {
                async () => (await blocked.Coordinator.RequestAsync()).ThrowIfFailedOrDeferred(),
                async () => (await ready.Coordinator.RequestAsync()).ThrowIfFailedOrDeferred(),
            }));
        Assert(ready.Reads.Count == 1 && !commit.IsCompleted && blocked.Coordinator.HasPending,
            "a deferred first window must not prevent another window's real awaited read");
        ready.Fail(0, error);
        var result = await commit;
        Assert(result.Saved && result.RefreshDeferred && ReferenceEquals(result.RefreshError, error),
            "actual failure must retain its identity while another window remains genuinely pending");
        Assert(blocked.Coordinator.HasPending && blocked.Reads.Count == 0 && ready.Failures.Count == 1,
            "aggregate failure must not discard pending or duplicate a failure report");
        blocked.Gate.EndDraft();
        var resumed = blocked.Coordinator.NotifyDraftEnded();
        blocked.Complete(0);
        Assert(resumed is not null && (await resumed).Status == WindowRefreshStatus.Applied,
            "pending window must still consume its own read after an aggregate failure");
    }

    private static void TestDeferredBackgroundFailureIsObserved()
    {
        var fixture = new Fixture();
        fixture.Gate.BeginDraft();
        fixture.Coordinator.NotifyDraftStarted();
        var deferred = fixture.Coordinator.RequestAsync();
        Assert(deferred.IsCompletedSuccessfully && deferred.Result.Status == WindowRefreshStatus.Deferred,
            "blocked request must distinguish defer before returning");
        fixture.Gate.EndDraft();
        _ = fixture.Coordinator.NotifyDraftEnded();
        var error = new IOException("Synthetic deferred-background read failure");
        fixture.Fail(0, error);
        Assert(fixture.Failures.Count == 1 && ReferenceEquals(fixture.Failures[0], error) && fixture.Applied.Count == 0,
            "background pending consumer must observe failure and report visible status even without an awaiting modal");
    }

    private static async Task TestFailureIsObservableAndRetryIsExplicitAsync()
    {
        var fixture = new Fixture();
        var request = fixture.Coordinator.RequestAsync();
        var error = new IOException("Synthetic read failure");
        fixture.Fail(0, error);
        var result = await request;
        Assert(result.Status == WindowRefreshStatus.Failed && ReferenceEquals(result.Error, error),
            "read failure must be the actual outcome seen by awaiter");
        Exception? thrown = null;
        try { result.ThrowIfFailed(); }
        catch (Exception actual) { thrown = actual; }
        Assert(ReferenceEquals(thrown, error), "Task adapter must be able to propagate original failure to settings workflow");
        Assert(fixture.Failures.Count == 1 && fixture.Reads.Count == 1 && fixture.Applied.Count == 0,
            "failure should be reported once and must not automatically loop retry");
        var retry = fixture.Coordinator.RequestAsync();
        fixture.Complete(1);
        Assert((await retry).Status == WindowRefreshStatus.Applied, "later explicit request must recover after failure");
    }

    private static async Task TestApplyFailureIsObservableAsync()
    {
        var fixture = new Fixture();
        var error = new InvalidOperationException("Synthetic apply failure");
        fixture.ApplyFailure = error;
        var request = fixture.Coordinator.RequestAsync();
        fixture.Complete(0);
        var result = await request;
        Assert(result.Status == WindowRefreshStatus.Failed && ReferenceEquals(result.Error, error) && fixture.Failures.Count == 1,
            "apply exceptions must settle callers as failure instead of leaving the queue running");
    }

    private static async Task TestLateFailureBelongsToOldTargetAsync()
    {
        var fixture = new Fixture();
        var request = fixture.Coordinator.RequestAsync();
        fixture.Config = new AppConfig { Boards = [Board("board-b", "board-b.md", "Doing")] };
        fixture.Fail(0, new IOException("Synthetic old-target failure"));
        Assert(fixture.Failures.Count == 0 && fixture.Reads.Count == 2 && !request.IsCompleted,
            "late old-target failure must not pollute new target and original requester must still await current read");
        fixture.Complete(1);
        Assert((await request).Status == WindowRefreshStatus.Applied, "new target must apply after obsolete failure");
    }

    private static async Task TestCloseSettlesWaitersAndLoadedCannotRestartAsync()
    {
        var fixture = new Fixture();
        var timerStarts = 0;
        async Task LoadedAsync()
        {
            var result = await fixture.Coordinator.RequestAsync();
            result.ThrowIfFailed();
            if (!fixture.Coordinator.IsClosed) timerStarts++;
        }
        var loaded = LoadedAsync();
        var queued = fixture.Coordinator.RequestAsync();
        fixture.Coordinator.Close();
        await loaded;
        Assert((await queued).Status == WindowRefreshStatus.Closed && timerStarts == 0,
            "close must settle both active and queued callers while Loaded skips restarting timer");
        fixture.Fail(0, new IOException("Synthetic closed-window failure"));
        var afterClose = await fixture.Coordinator.RequestAsync();
        Assert(afterClose.Status == WindowRefreshStatus.Closed && fixture.Reads.Count == 1 && fixture.Applied.Count == 0 && fixture.Failures.Count == 0,
            "closed coordinator must suppress late UI/error and permanently reject new reads");
        _ = fixture.Coordinator.NotifyDraftEnded();
        Assert(!fixture.Coordinator.HasPending && fixture.Reads.Count == 1, "draft notifications cannot revive closed work");
    }

    private static void TestEndingDraftWithoutPendingDoesNotRead()
    {
        var fixture = new Fixture();
        fixture.Gate.BeginDraft();
        fixture.Coordinator.NotifyDraftStarted();
        fixture.Gate.EndDraft();
        _ = fixture.Coordinator.NotifyDraftEnded();
        Assert(fixture.Reads.Count == 0 && !fixture.Coordinator.HasPending, "draft end should consume only valid pending work");
    }

    private static void TestDraftIdentityAllowsSameTargetConfigReplacement()
    {
        var original = Board("board-a", "board-a.md", "Todo");
        var identity = WindowBoardIdentity.Capture(original);
        var configReplacement = new AppConfig { Boards = [Board("board-a", "board-a.md", "Todo")] };
        configReplacement.Boards[0].WidgetTheme = "mist";
        configReplacement.SummaryWindow.Left = 200;
        Assert(identity.Matches(configReplacement.Boards[0]),
            "theme/layout or config reference replacement must not reject a draft for the same board/path/column");
        foreach (var changed in new[]
        {
            Board("board-b", "board-a.md", "Todo"),
            Board("board-a", "board-b.md", "Todo"),
            Board("board-a", "board-a.md", "Doing"),
        })
        {
            Assert(!identity.Matches(changed), "draft guard must reject exactly board ID/path/column changes");
        }
    }

    private static BoardConfig Board(string id, string path, string column) => new()
    {
        Id = id,
        FilePath = path,
        DefaultColumn = column,
        DisplayName = "Synthetic board",
    };

    private sealed class Read
    {
        public required IReadOnlyList<BoardConfig> Boards { get; init; }
        public TaskCompletionSource<IReadOnlyList<BoardGroup>> Completion { get; } = new();
    }

    private sealed class Fixture
    {
        public AppConfig Config { get; set; } = new() { Boards = [Board("board-a", "board-a.md", "Todo")] };
        public PendingRefreshGate Gate { get; } = new();
        public List<Read> Reads { get; } = new();
        public List<IReadOnlyList<BoardGroup>> Applied { get; } = new();
        public List<Exception> Failures { get; } = new();
        public Exception? ApplyFailure { get; set; }
        public int MaxActiveReads { get; private set; }
        private int _activeReads;
        public WindowRefreshCoordinator Coordinator { get; }

        public Fixture()
        {
            Coordinator = new WindowRefreshCoordinator(
                () => WindowRefreshTarget.Capture(Config, Config.Boards.Where(board => board.Enabled)),
                () => Gate.HasActiveDraft,
                ReadAsync,
                (_, groups) =>
                {
                    if (ApplyFailure is not null) throw ApplyFailure;
                    Applied.Add(groups);
                },
                Failures.Add);
        }

        private async Task<IReadOnlyList<BoardGroup>> ReadAsync(WindowRefreshTarget target)
        {
            var read = new Read { Boards = target.CreateReadBoards() };
            Reads.Add(read);
            _activeReads++;
            MaxActiveReads = Math.Max(MaxActiveReads, _activeReads);
            try { return await read.Completion.Task; }
            finally { _activeReads--; }
        }

        public void Complete(int index)
        {
            var read = Reads[index];
            read.Completion.SetResult(read.Boards.Select(board => new BoardGroup
            {
                Board = board,
                ColumnTitle = board.DefaultColumn,
                ColumnRangeHash = "synthetic-column-hash",
                SourceHash = "synthetic-source-hash",
            }).ToArray());
        }

        public void Fail(int index, Exception error) => Reads[index].Completion.SetException(error);
    }
}
