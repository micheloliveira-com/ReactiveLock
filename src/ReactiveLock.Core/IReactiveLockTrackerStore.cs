namespace MichelOliveira.Com.ReactiveLock.Core;

/// <summary>
/// Represents a store responsible for persisting and updating lock status information for hosts.
/// </summary>
public interface IReactiveLockTrackerStore
{
    /// <summary>
    /// Sets the busy or idle status for the specified host.
    /// </summary>
    /// <param name="hostname">The unique identifier of the host.</param>
    /// <param name="isBusy">True if the host is busy (locked); false if idle (unlocked).</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task SetStatusAsync(string hostname, bool isBusy);
}
