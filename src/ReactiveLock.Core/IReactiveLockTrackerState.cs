namespace MichelOliveira.Com.ReactiveLock.Core;

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