using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using DesktopOverlayBoard.Models;

namespace DesktopOverlayBoard.Application;

public enum WindowRefreshStatus
{
    Applied,
    Deferred,
    Failed,
    Closed,
}

public sealed record WindowRefreshResult(WindowRefreshStatus Status, Exception? Error = null)
{
    public void ThrowIfFailed()
    {
        if (Status == WindowRefreshStatus.Failed)
        {
            ExceptionDispatchInfo.Capture(Error ?? new InvalidOperationException("Failed refresh has no error.")).Throw();
        }
    }

    public void ThrowIfFailedOrDeferred()
    {
        ThrowIfFailed();
        if (Status == WindowRefreshStatus.Deferred) throw new WindowRefreshDeferredException();
    }
}

// A narrow bridge for existing Func<Task> refresh callbacks. A deferred refresh
// remains queued and is distinct from a failed read or a successful apply.
public sealed class WindowRefreshDeferredException : Exception
{
    public Exception? RefreshError { get; }

    public WindowRefreshDeferredException(Exception? refreshError = null)
        : base("Refresh is pending until the active draft ends.", refreshError)
    {
        RefreshError = refreshError;
    }
}

public sealed record WindowBoardIdentity(string Id, string FilePath, string Column)
{
    public static WindowBoardIdentity Capture(BoardConfig board) =>
        new(board.Id, board.FilePath, board.DefaultColumn);

    public bool Matches(BoardConfig current) => Equals(Capture(current));
}

public sealed class WindowRefreshTarget
{
    private readonly AppConfig _configIdentity;
    private readonly WindowBoardIdentity[] _identities;
    private readonly BoardConfig[] _readBoards;

    private WindowRefreshTarget(AppConfig config, IEnumerable<BoardConfig> boards)
    {
        _configIdentity = config;
        _readBoards = boards.Select(CopyBoard).ToArray();
        _identities = _readBoards.Select(WindowBoardIdentity.Capture).ToArray();
    }

    public static WindowRefreshTarget Capture(AppConfig config, IEnumerable<BoardConfig> boards) =>
        new(config, boards);

    public IReadOnlyList<BoardConfig> CreateReadBoards() => _readBoards.Select(CopyBoard).ToArray();

    internal bool Matches(WindowRefreshTarget current) =>
        ReferenceEquals(_configIdentity, current._configIdentity) &&
        _identities.SequenceEqual(current._identities);

    private static BoardConfig CopyBoard(BoardConfig board) => new()
    {
        Id = board.Id,
        DisplayName = board.DisplayName,
        VaultName = board.VaultName,
        FilePath = board.FilePath,
        DefaultColumn = board.DefaultColumn,
        Enabled = board.Enabled,
        WidgetTitle = board.WidgetTitle,
        WidgetNote = board.WidgetNote,
        WidgetTheme = board.WidgetTheme,
    };
}

// The window calls every entry point on its dispatcher. Reads may use Task.Run;
// the awaited continuation returns to that dispatcher before checking or applying.
// The coordinator owns pending work, but the window continues to own draft state
// and its timer. No read is started merely because a draft ended.
public sealed class WindowRefreshCoordinator
{
    private readonly Func<WindowRefreshTarget> _captureTarget;
    private readonly Func<bool> _hasDraft;
    private readonly Func<WindowRefreshTarget, Task<IReadOnlyList<BoardGroup>>> _readAsync;
    private readonly Action<WindowRefreshTarget, IReadOnlyList<BoardGroup>> _apply;
    private readonly Action<Exception> _reportFailure;
    private List<TaskCompletionSource<WindowRefreshResult>> _pendingWaiters = new();
    private List<TaskCompletionSource<WindowRefreshResult>>? _readingWaiters;
    private bool _pending;
    private bool _running;
    private long _draftVersion;

    public WindowRefreshCoordinator(
        Func<WindowRefreshTarget> captureTarget,
        Func<bool> hasDraft,
        Func<WindowRefreshTarget, Task<IReadOnlyList<BoardGroup>>> readAsync,
        Action<WindowRefreshTarget, IReadOnlyList<BoardGroup>> apply,
        Action<Exception> reportFailure)
    {
        _captureTarget = captureTarget;
        _hasDraft = hasDraft;
        _readAsync = readAsync;
        _apply = apply;
        _reportFailure = reportFailure;
    }

    public bool IsClosed { get; private set; }
    public bool HasPending => _pending;

    public Task<WindowRefreshResult> RequestAsync()
    {
        if (IsClosed) return Task.FromResult(new WindowRefreshResult(WindowRefreshStatus.Closed));

        var waiter = new TaskCompletionSource<WindowRefreshResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingWaiters.Add(waiter);
        _pending = true;
        try
        {
            if (_hasDraft()) DeferForDraft();
        }
        catch (Exception error)
        {
            FailPending(error);
        }
        StartIfNeeded();
        return waiter.Task;
    }

    public void NotifyDraftStarted()
    {
        _draftVersion++;
        if (_pending || _readingWaiters is not null) DeferForDraft();
    }

    public Task<WindowRefreshResult>? NotifyDraftEnded()
    {
        _draftVersion++;
        return !IsClosed && _pending ? RequestAsync() : null;
    }

    // Aggregate only these explicitly supplied window commands. There is no
    // shared refresh state: every window still owns its independent coordinator.
    public static async Task RefreshAllAsync(IEnumerable<Func<Task>> refreshes)
    {
        Exception? failure = null;
        var deferred = false;
        foreach (var refresh in refreshes)
        {
            try { await refresh(); }
            catch (WindowRefreshDeferredException pending)
            {
                deferred = true;
                failure ??= pending.RefreshError;
            }
            catch (Exception error) { failure ??= error; }
        }
        if (deferred) throw new WindowRefreshDeferredException(failure);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public void Close()
    {
        if (IsClosed) return;
        IsClosed = true;
        var closed = new WindowRefreshResult(WindowRefreshStatus.Closed);
        Complete(_pendingWaiters, closed);
        if (_readingWaiters is not null) Complete(_readingWaiters, closed);
        _pendingWaiters.Clear();
        _pending = false;
    }

    private void StartIfNeeded()
    {
        if (IsClosed || _running || !_pending) return;
        _running = true;
        _ = DrainAsync();
    }

    private async Task DrainAsync()
    {
        try
        {
            while (!IsClosed && _pending)
            {
                try
                {
                    if (_hasDraft())
                    {
                        DeferForDraft();
                        return;
                    }
                }
                catch (Exception error)
                {
                    FailPending(error);
                    return;
                }

                var waiters = _pendingWaiters;
                _pendingWaiters = new();
                _pending = false;
                _readingWaiters = waiters;
                WindowRefreshTarget? target = null;
                var draftVersion = _draftVersion;
                IReadOnlyList<BoardGroup>? groups = null;
                Exception? readFailure = null;
                try
                {
                    target = _captureTarget();
                    groups = await _readAsync(target);
                }
                catch (Exception error)
                {
                    readFailure = error;
                }

                try
                {
                    if (IsClosed)
                    {
                        Complete(waiters, new(WindowRefreshStatus.Closed));
                    }
                    else if (target is not null && _hasDraft())
                    {
                        CarryPending(waiters);
                        DeferForDraft();
                    }
                    else if (target is not null &&
                        (draftVersion != _draftVersion || !target.Matches(_captureTarget())))
                    {
                        // A stale success or failure belongs to the old identity/draft.
                        // Carry its callers forward to the one current pending read.
                        CarryPending(waiters);
                    }
                    else if (readFailure is not null)
                    {
                        CompleteFailure(waiters, readFailure);
                    }
                    else
                    {
                        _apply(target!, groups!);
                        Complete(waiters, new(WindowRefreshStatus.Applied));
                    }
                }
                catch (Exception error)
                {
                    CompleteFailure(waiters, error);
                }
                finally
                {
                    _readingWaiters = null;
                }
            }
        }
        finally
        {
            _running = false;
        }
    }

    private void CarryPending(IEnumerable<TaskCompletionSource<WindowRefreshResult>> waiters)
    {
        _pendingWaiters.AddRange(waiters.Where(waiter => !waiter.Task.IsCompleted));
        _pending = true;
    }

    private void DeferForDraft()
    {
        var deferred = new WindowRefreshResult(WindowRefreshStatus.Deferred);
        Complete(_pendingWaiters, deferred);
        _pendingWaiters.Clear();
        if (_readingWaiters is not null) Complete(_readingWaiters, deferred);
        _pending = true;
    }

    private void FailPending(Exception error)
    {
        var waiters = _pendingWaiters;
        _pendingWaiters = new();
        _pending = false;
        CompleteFailure(waiters, error);
    }

    private void CompleteFailure(IEnumerable<TaskCompletionSource<WindowRefreshResult>> waiters, Exception error)
    {
        try
        {
            _reportFailure(error);
        }
        catch (Exception reportFailure)
        {
            error = new AggregateException(error, reportFailure);
        }
        Complete(waiters, new(WindowRefreshStatus.Failed, error));
    }

    private static void Complete(IEnumerable<TaskCompletionSource<WindowRefreshResult>> waiters, WindowRefreshResult result)
    {
        foreach (var waiter in waiters) waiter.TrySetResult(result);
    }
}
