using System;
using System.Threading;

namespace DesktopOverlayBoard.Services;

public sealed class SingleInstanceService : IDisposable
{
    private const string DefaultMutexName = @"Local\GlassKanbanOverlay.SingleInstance";
    private const string DefaultActivationEventName = @"Local\GlassKanbanOverlay.Activate";

    private readonly string _mutexName;
    private readonly string _activationEventName;
    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _registeredWait;
    private int _disposed;

    public SingleInstanceService()
        : this(DefaultMutexName, DefaultActivationEventName)
    {
    }

    public SingleInstanceService(string mutexName, string activationEventName)
    {
        if (string.IsNullOrWhiteSpace(mutexName))
        {
            throw new ArgumentException("A mutex name is required.", nameof(mutexName));
        }

        if (string.IsNullOrWhiteSpace(activationEventName))
        {
            throw new ArgumentException("An activation event name is required.", nameof(activationEventName));
        }

        _mutexName = mutexName;
        _activationEventName = activationEventName;
    }

    public bool TryAcquire(Action onActivationRequested)
    {
        ArgumentNullException.ThrowIfNull(onActivationRequested);

        if (_mutex is not null)
        {
            return true;
        }

        try
        {
            // ponytail: one process-wide mutex plus one auto-reset signal; no broker or pipe.
            // Create the signal before the mutex so a second process can always wake a
            // primary process that is still finishing startup.
            _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _activationEventName);
            _mutex = new Mutex(false, _mutexName);

            var ownsMutex = false;
            try
            {
                ownsMutex = _mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            if (!ownsMutex)
            {
                _activationEvent.Set();
                Dispose();
                return false;
            }

            _registeredWait = ThreadPool.RegisterWaitForSingleObject(
                _activationEvent,
                (_, timedOut) =>
                {
                    if (timedOut || Volatile.Read(ref _disposed) != 0)
                    {
                        return;
                    }

                    try
                    {
                        onActivationRequested();
                    }
                    catch (Exception ex)
                    {
                        LogService.Error(ex, "Single-instance activation failed");
                    }
                },
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);

            return true;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Single-instance initialization failed");
            Dispose();
            return false;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _registeredWait?.Unregister(null);
        _registeredWait = null;
        _activationEvent?.Dispose();
        _activationEvent = null;

        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The current process may not own the mutex after a failed acquisition.
            }

            _mutex.Dispose();
            _mutex = null;
        }

        GC.SuppressFinalize(this);
    }
}
