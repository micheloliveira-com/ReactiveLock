namespace MichelOliveira.Com.ReactiveLock.Core;

using System.Net;
using System.Threading;
using System.Threading.Tasks;

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
