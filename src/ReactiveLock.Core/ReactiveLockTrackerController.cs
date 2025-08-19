namespace MichelOliveira.Com.ReactiveLock.Core;

using System.Net;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Controls and manages the reactive lock state for a single instance.
/// 
/// This controller provides methods to increment and decrement the lock usage count,
/// reflecting whether the instance is busy or idle. When the in-flight lock count
/// crosses the defined threshold, the controller updates the associated <see cref="IReactiveLockTrackerStore"/>.
/// 
/// It is thread-safe and designed for asynchronous coordination of operations within
/// a single application instance or across distributed instances when combined with
/// an appropriate store implementation.
///
/// <para>
/// ⚠️ Notice: This file is part of the ReactiveLock library and is licensed under the MIT License.
/// You must follow license, preserve the copyright notice, and comply with all legal terms
/// when using any part of this software.
/// See the LICENSE file in the project root for full license details.
/// © Michel Oliveira
/// </para>
/// </summary>
public class ReactiveLockTrackerController : IReactiveLockTrackerController
{
    public int BusyThreshold { get; }
    private string InstanceName { get; }
    private IReactiveLockTrackerStore Store { get; }
    private int _inFlightLockCount;

    public ReactiveLockTrackerController(IReactiveLockTrackerStore store, string instanceName = "default", int busyThreshold = 1)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        InstanceName = instanceName ?? throw new ArgumentNullException(nameof(instanceName));
        if (busyThreshold < 1)
            throw new ArgumentOutOfRangeException(nameof(busyThreshold), "Threshold must be at least 1.");
        BusyThreshold = busyThreshold;
    }

    public int GetActualCount()
    {
        return Volatile.Read(ref _inFlightLockCount);
    }

    public async Task IncrementAsync(string? lockData = default)
    {
        if (Interlocked.Increment(ref _inFlightLockCount) != BusyThreshold)
            return;

        await Store.SetStatusAsync(InstanceName, true, lockData).ConfigureAwait(false);
    }

    public async Task DecrementAsync(int amount = 1)
    {
        var afterCount = Interlocked.Add(ref _inFlightLockCount, -amount);
        if (afterCount > 0)
            return;

        Interlocked.Exchange(ref _inFlightLockCount, 0);

        await Store.SetStatusAsync(InstanceName, false).ConfigureAwait(false);
    }
}
