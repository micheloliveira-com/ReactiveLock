﻿namespace MichelOliveira.Com.ReactiveLock.Core;

using System.Threading;
using System.Threading.Tasks;

public class ReactiveLockTrackerState : IReactiveLockTrackerState
{
    private TaskCompletionSource Tcs { get; set; } = CreateCompletedTcs();
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
    /// Unblocks the gate.
    /// All waiting calls to WaitIfBlockedAsync will resume.
    /// </summary>
    public async Task SetLocalStateBlockedAsync()
    {
        bool changed = false;
        await Mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Tcs.Task.IsCompleted)
            {
                Tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                changed = true;
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
