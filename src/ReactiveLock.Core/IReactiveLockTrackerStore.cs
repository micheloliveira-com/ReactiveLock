namespace MichelOliveira.Com.ReactiveLock.Core;

/// <summary>
/// Represents a store responsible for persisting and updating lock status information for instance.
/// </summary>
public interface IReactiveLockTrackerStore
{
    /// <summary>
    /// Sets the busy or idle status for the specified instance.
    /// </summary>
    /// <param name="instanceName">The unique identifier of the instance.</param>
    /// <param name="isBusy">True if the instance is busy (locked); false if idle (unlocked).</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task SetStatusAsync(string instanceName, bool isBusy);
}
