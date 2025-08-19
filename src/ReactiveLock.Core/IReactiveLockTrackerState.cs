namespace MichelOliveira.Com.ReactiveLock.Core;

/// <summary>
/// Represents the state of a reactive lock for a single instance, providing methods
/// to check, wait on, block, and unblock the lock asynchronously.  
/// 
/// This interface allows tracking of lock metadata, supporting reactive and coordinated
/// behaviors in in-process or distributed scenarios.  
///
/// <para>
/// ⚠️ Notice: This file is part of the ReactiveLock library and is licensed under the MIT License.
/// You must follow license, preserve the copyright notice, and comply with all legal terms
/// when using any part of this software.
/// See the LICENSE file in the project root for full license details.
/// © Michel Oliveira
/// </para>
/// </summary>
public interface IReactiveLockTrackerState
{
    /// <summary>
    /// Returns whether the gate is currently blocked.
    /// </summary>
    Task<bool> IsBlockedAsync();
    
    /// <summary>
    /// Separator string used to concatenate multiple lock data entries into a single string.
    /// When multiple busy lock metadata strings are combined, they are joined using this separator.
    /// </summary>
    const string LOCK_DATA_SEPARATOR = "#REACTIVELOCK#";

    /// <summary>
    /// Gets the current lock metadata entries as an array of strings if the gate is blocked;
    /// otherwise returns an empty array.
    /// Splits the stored lock data string by the defined <see cref="LOCK_DATA_SEPARATOR"/>.
    /// </summary>
    /// <returns>
    /// A task that resolves to an array of lock data entries, or empty if not blocked or no data.
    /// </returns>
    Task<string[]> GetLockDataEntriesIfBlockedAsync();

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
    /// When multiple lock data entries exist, they may be concatenated using the separator <c>#REACTIVELOCK# (IReactiveLockTrackerState.LOCK_DATA_SEPARATOR)</c>.
    /// This data can be used for logging or diagnostics when the gate is blocked.
    /// </param>
    Task SetLocalStateBlockedAsync(string? lockData = null);

    /// <summary>
    /// Unblocks the gate.
    /// All waiting calls to WaitIfBlockedAsync will resume.
    /// </summary>
    Task SetLocalStateUnblockedAsync();
}