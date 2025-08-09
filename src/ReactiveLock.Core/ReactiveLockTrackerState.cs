namespace MichelOliveira.Com.ReactiveLock.Core;

using System.Threading;
using System.Threading.Tasks;

public class ReactiveLockTrackerState : IReactiveLockTrackerState
{
    private TaskCompletionSource Tcs { get; set; } = CreateCompletedTcs();
    private string? LockData { get; set; }
    private SemaphoreSlim Mutex { get; } = new(1, 1);

    private IEnumerable<Func<IServiceProvider, Task>> OnLockedHandlers { get; }
    private IEnumerable<Func<IServiceProvider, Task>> OnUnlockedHandlers { get; }

    private IServiceProvider HandlerServiceProvider { get; }

    public ReactiveLockTrackerState(
        IServiceProvider handlerServiceProvider = null!,
        IEnumerable<Func<IServiceProvider, Task>>? onLockedHandlers = null,
        IEnumerable<Func<IServiceProvider, Task>>? onUnlockedHandlers = null)
    {
        HandlerServiceProvider = handlerServiceProvider;
        OnLockedHandlers = onLockedHandlers ?? [];
        OnUnlockedHandlers = onUnlockedHandlers ?? [];
    }

    private static TaskCompletionSource CreateCompletedTcs()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.TrySetResult();
        return tcs;
    }

    public async Task<string[]> GetLockDataEntriesIfBlockedAsync()
    {
        var data = await GetLockDataIfBlockedAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(data))
            return [];

        return data.Split(IReactiveLockTrackerState.LOCK_DATA_SEPARATOR, StringSplitOptions.RemoveEmptyEntries);
    }

    public async Task<string?> GetLockDataIfBlockedAsync()
    {
        await Mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            return LockData;
        }
        finally
        {
            Mutex.Release();
        }
    }

    public async Task<bool> IsBlockedAsync()
    {
        await Mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            return !Tcs.Task.IsCompleted;
        }
        finally
        {
            Mutex.Release();
        }
    }

    public async Task<bool> WaitIfBlockedAsync(
        Func<Task>? onBlockedAsync = null,
        TimeSpan? whileBlockedLoopDelay = null,
        Func<Task>? whileBlockedAsync = null)
    {
        await Mutex.WaitAsync().ConfigureAwait(false);
        Task taskToWait;
        bool isBlocked;
        try
        {
            taskToWait = Tcs.Task;
            isBlocked = !taskToWait.IsCompleted;
        }
        finally
        {
            Mutex.Release();
        }

        if (isBlocked)
        {
            if (onBlockedAsync != null)
            {
                await onBlockedAsync().ConfigureAwait(false);
            }

            if (whileBlockedAsync != null)
            {
                var delay = whileBlockedLoopDelay ?? TimeSpan.FromMilliseconds(10);
                while (!taskToWait.IsCompleted)
                {
                    await whileBlockedAsync().ConfigureAwait(false);
                    await Task.Delay(delay).ConfigureAwait(false);
                }
            }
        }

        await taskToWait.ConfigureAwait(false);
        return isBlocked;
    }

    /// <summary>
    /// Blocks the gate.
    /// Future calls to WaitIfBlockedAsync will asynchronously wait until unblocked.
    /// </summary>
    public async Task SetLocalStateUnblockedAsync()
    {
        bool changed = false;
        await Mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!Tcs.Task.IsCompleted)
            {
                Tcs.TrySetResult();
                changed = true;
                LockData = null;
            }
        }
        finally
        {
            Mutex.Release();
        }

        if (changed)
        {
            foreach (var handler in OnUnlockedHandlers)
            {
                _ = Task.Run(async () =>
                {
                    await handler(HandlerServiceProvider).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Blocks the gate.
    /// Future calls to <see cref="WaitIfBlockedAsync"/> will asynchronously wait until the gate is unblocked.
    /// Sets optional lock metadata that can be used for diagnostics or logging.
    /// </summary>
    /// <param name="lockData">
    /// Optional string containing metadata about the lock state, such as a reason or identifier.
    /// This value is stored internally and can be accessed while the gate is blocked.
    /// </param>
    public async Task SetLocalStateBlockedAsync(string? lockData = null)
    {
        bool changed = false;
        await Mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Tcs.Task.IsCompleted)
            {
                Tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                changed = true;
                LockData = lockData;
            }
        }
        finally
        {
            Mutex.Release();
        }

        if (changed)
        {
            foreach (var handler in OnLockedHandlers)
            {
                _ = Task.Run(async () =>
                {
                    await handler(HandlerServiceProvider).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
        }
    }
}
