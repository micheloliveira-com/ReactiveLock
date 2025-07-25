namespace MichelOliveira.Com.ReactiveLock.Core;

/// <summary>
/// Controller interface for managing reactive lock state per instance.
/// Provides methods to increment and decrement the lock usage count,
/// reflecting whether the instance is busy or idle.
/// </summary>
public interface IReactiveLockTrackerController
{
    /// <summary>
    /// Increments the internal count of active lock usages for the current instance.
    /// When the count changes from zero to one, the lock status is marked as busy.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task IncrementAsync();

    /// <summary>
    /// Decrements the internal count of active lock usages for the current instance by the specified amount.
    /// When the count drops to zero or below, the lock status is marked as idle.
    /// </summary>
    /// <param name="amount">The amount to decrement the active count by (default is 1).</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DecrementAsync(int amount = 1);
}
