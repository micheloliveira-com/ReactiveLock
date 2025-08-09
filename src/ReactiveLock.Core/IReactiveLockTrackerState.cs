namespace MichelOliveira.Com.ReactiveLock.Core;

public interface IReactiveLockTrackerState
{
    /// <summary>
    /// Gets the current lock metadata string if the gate is blocked; otherwise returns null.
    /// This allows callers to retrieve additional context or reasons why the lock is currently held.
    /// </summary>
    /// <returns>
    /// A task that resolves to the lock data string if blocked, or null if not blocked.
    /// </returns>
    Task<string?> GetLockDataIfBlockedAsync();

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
    /// Future calls to <see cref="WaitIfBlockedAsync"/> will wait asynchronously until unblocked.
    /// </summary>
    /// <param name="lockData">
    /// Optional string containing metadata about the lock state, such as reason or identifier.
    /// This data can be used for logging or diagnostics when the gate is blocked.
    /// </param>
    Task SetLocalStateBlockedAsync(string? lockData = null);

    /// <summary>
    /// Unblocks the gate.
    /// All waiting calls to WaitIfBlockedAsync will resume.
    /// </summary>
    Task SetLocalStateUnblockedAsync();
}