namespace MichelOliveira.Com.ReactiveLock.Core;

/// <summary>
/// Controller interface for managing reactive lock state per instance.
/// Provides methods to increment and decrement the lock usage count,
/// reflecting whether the instance is busy or idle.
///
/// <para>
/// ⚠️ Notice: This file is part of the ReactiveLock library and is licensed under the MIT License.
/// You must follow license, preserve the copyright notice, and comply with all legal terms
/// when using any part of this software.
/// See the LICENSE file in the project root for full license details.
/// © Michel Oliveira
/// </para>
/// </summary>
public interface IReactiveLockTrackerController
{
    /// <summary>
    /// Gets the current number of active lock usages for the instance.
    /// This reflects the actual in-flight lock count at the moment of calling.
    /// </summary>
    /// <returns>The current active lock count.</returns>
    int GetActualCount();
    
    /// <summary>
    /// Increments the internal count of active lock usages for the current instance.
    /// When the count changes from zero to one, the lock status is marked as busy.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task IncrementAsync(string? lockData = default);

    /// <summary>
    /// Decrements the internal count of active lock usages for the current instance by the specified amount.
    /// When the count drops to zero or below, the lock status is marked as idle.
    /// </summary>
    /// <param name="amount">The amount to decrement the active count by (default is 1).</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DecrementAsync(int amount = 1);
}
