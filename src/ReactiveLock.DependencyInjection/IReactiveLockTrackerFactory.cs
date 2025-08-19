namespace MichelOliveira.Com.ReactiveLock.DependencyInjection;

using MichelOliveira.Com.ReactiveLock.Core;

/// <summary>
/// Factory interface to retrieve reactive lock tracker components by lock key.
/// Provides access to both controller and state objects associated with a specific lock.
///
/// <para>
/// ⚠️ Notice: This file is part of the ReactiveLock library and is licensed under the MIT License.
/// You must follow license, preserve the copyright notice, and comply with all legal terms
/// when using any part of this software.
/// See the LICENSE file in the project root for full license details.
/// © Michel Oliveira
/// </para>
/// </summary>
public interface IReactiveLockTrackerFactory
{
    /// <summary>
    /// Gets the reactive lock tracker controller for the specified lock key.
    /// The controller manages operations related to the lock's lifecycle.
    /// </summary>
    /// <param name="lockKey">Unique identifier for the lock.</param>
    /// <returns>The <see cref="IReactiveLockTrackerController"/> associated with the lock key.</returns>
    IReactiveLockTrackerController GetTrackerController(string lockKey);

    /// <summary>
    /// Gets the reactive lock tracker state for the specified lock key.
    /// The state represents the current locking status and notifies changes.
    /// </summary>
    /// <param name="lockKey">Unique identifier for the lock.</param>
    /// <returns>The <see cref="IReactiveLockTrackerState"/> associated with the lock key.</returns>
    IReactiveLockTrackerState GetTrackerState(string lockKey);
}
