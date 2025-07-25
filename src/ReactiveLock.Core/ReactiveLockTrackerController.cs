namespace MichelOliveira.Com.ReactiveLock.Core;

using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class ReactiveLockTrackerController : IReactiveLockTrackerController
{
    private IReactiveLockTrackerStore Store { get; }
    private string InstanceName { get; }

    private int _inFlightRequestCount;

    public ReactiveLockTrackerController(IReactiveLockTrackerStore store, string instanceName)
    {
        Store = store;
        InstanceName = instanceName ?? throw new ArgumentNullException(nameof(instanceName));
    }

    public async Task IncrementAsync()
    {
        var newCount = Interlocked.Increment(ref _inFlightRequestCount);
        if (newCount == 1)
        {
            await Store.SetStatusAsync(InstanceName, true).ConfigureAwait(false);
        }
    }

    public async Task DecrementAsync(int amount = 1)
    {
        var afterCount = Interlocked.Add(ref _inFlightRequestCount, -amount);
        if (afterCount <= 0)
        {
            Interlocked.Exchange(ref _inFlightRequestCount, 0);
            await Store.SetStatusAsync(InstanceName, false).ConfigureAwait(false);
        }
    }
}
