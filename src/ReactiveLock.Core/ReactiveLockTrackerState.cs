namespace MichelOliveira.Com.ReactiveLock.Core;

using System.Threading;
using System.Threading.Tasks;
public interface IReactiveLockTrackerState
{
    /// <summary>
    /// Returns whether the gate is currently blocked.
    /// </summary>
    Task<bool> IsBlockedAsync();

    /// <summary>
    /// If blocked, asynchronously waits until unblocked.
    /// Optional callbacks for when blocked and while waiting.
    /// Returns true if the gate was blocked and waited, false otherwise.
    /// </summary>
    Task<bool> WaitIfBlockedAsync(
        Func<Task>? onBlockedAsync = null,
        TimeSpan? whileBlockedLoopDelay = null,
        Func<Task>? whileBlockedAsync = null);

    /// <summary>
    /// Blocks the gate.
    /// Future calls to WaitIfBlockedAsync will wait asynchronously until unblocked.
    /// </summary>
    Task SetLocalStateBlockedAsync();

    /// <summary>
    /// Unblocks the gate.
    /// All waiting calls to WaitIfBlockedAsync will resume.
    /// </summary>
    Task SetLocalStateUnblockedAsync();
}
public class ReactiveLockTrackerState : IReactiveLockTrackerState
{
    private TaskCompletionSource _tcs = CreateCompletedTcs();
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private readonly IEnumerable<Func<IServiceProvider, Task>> _onLockedHandlers;
    private readonly IEnumerable<Func<IServiceProvider, Task>> _onUnlockedHandlers;

    private IServiceProvider ServiceProvider { get; }

    public ReactiveLockTrackerState(
        IServiceProvider serviceProvider,
        IEnumerable<Func<IServiceProvider, Task>>? onLockedHandlers = null,
        IEnumerable<Func<IServiceProvider, Task>>? onUnlockedHandlers = null)
    {
        ServiceProvider = serviceProvider;
        _onLockedHandlers = onLockedHandlers ?? [];
        _onUnlockedHandlers = onUnlockedHandlers ?? [];
    }

    private static TaskCompletionSource CreateCompletedTcs()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.TrySetResult();
        return tcs;
    }

    public async Task<bool> IsBlockedAsync()
    {
        await _mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            return !_tcs.Task.IsCompleted;
        }
        finally
        {
            _mutex.Release();
        }
    }
    
    public async Task<bool> WaitIfBlockedAsync(
        Func<Task>? onBlockedAsync = null,
        TimeSpan? whileBlockedLoopDelay = null,
        Func<Task>? whileBlockedAsync = null)
    {
        await _mutex.WaitAsync().ConfigureAwait(false);
        Task taskToWait;
        bool isBlocked;
        try
        {
            taskToWait = _tcs.Task;
            isBlocked = !taskToWait.IsCompleted;
        }
        finally
        {
            _mutex.Release();
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
        await _mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_tcs.Task.IsCompleted)
            {
                _tcs.TrySetResult();
                changed = true;
            }
        }
        finally
        {
            _mutex.Release();
        }

        if (changed)
        {
            foreach (var handler in _onUnlockedHandlers)
            {
                _ = Task.Run(async () =>
                    {
                        await handler(ServiceProvider).ConfigureAwait(false);
                    }).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Unblocks the gate.
    /// All waiting calls to WaitIfBlockedAsync will resume.
    /// </summary>
    public async Task SetLocalStateBlockedAsync()
    {
        bool changed = false;
        await _mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_tcs.Task.IsCompleted)
            {
                _tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                changed = true;
            }
        }
        finally
        {
            _mutex.Release();
        }

        if (changed)
        {
            foreach (var handler in _onLockedHandlers)
            {
                _ = Task.Run(async () =>
                    {
                        await handler(ServiceProvider).ConfigureAwait(false);
                    }).ConfigureAwait(false);
            }
        }
    }
}
