namespace MichelOliveira.Com.ReactiveLock.Core;


/// <summary>
/// An in-memory implementation of <see cref="IReactiveLockTrackerStore"/> that tracks
/// reactive lock state for a single instance.  
/// 
/// It uses the provided <see cref="IReactiveLockTrackerState"/> to manage the lock state
/// locally, allowing efficient in-process coordination without external backends.
///
/// <para>
/// ⚠️ Notice: This file is part of the ReactiveLock library and is licensed under the MIT License.
/// You must follow license, preserve the copyright notice, and comply with all legal terms
/// when using any part of this software.
/// See the LICENSE file in the project root for full license details.
/// © Michel Oliveira
/// </para>
/// </summary>
public class InMemoryReactiveLockTrackerStore(IReactiveLockTrackerState state) : IReactiveLockTrackerStore
{

    public Task SetStatusAsync(bool isBusy, string? lockData = default) =>
        isBusy
            ? state.SetLocalStateBlockedAsync(lockData)
            : state.SetLocalStateUnblockedAsync();

}
