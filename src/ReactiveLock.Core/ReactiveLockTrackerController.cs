namespace MichelOliveira.Com.ReactiveLock.Core;

using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class ReactiveLockTrackerController : IReactiveLockTrackerController
{
    private IReactiveLockTrackerStore? Store { get; }
    private IReactiveLockTrackerState? State { get; }
    private string? InstanceName { get; }
    private int _inFlightLockCount;

    public ReactiveLockTrackerController(IReactiveLockTrackerState state)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    public ReactiveLockTrackerController(IReactiveLockTrackerStore store, string instanceName)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        InstanceName = instanceName ?? throw new ArgumentNullException(nameof(instanceName));
    }

    public async Task IncrementAsync()
    {
        if (Interlocked.Increment(ref _inFlightLockCount) != 1)
            return;
            
        if (State is not null)
            await State.SetLocalStateBlockedAsync().ConfigureAwait(false);

        if (Store is not null && !string.IsNullOrWhiteSpace(InstanceName))
            await Store.SetStatusAsync(InstanceName, true).ConfigureAwait(false);
    }

    public async Task DecrementAsync(int amount = 1)
    {
        var afterCount = Interlocked.Add(ref _inFlightLockCount, -amount);
        if (afterCount > 0)
            return;

        Interlocked.Exchange(ref _inFlightLockCount, 0);

        if (State is not null)
            await State.SetLocalStateUnblockedAsync().ConfigureAwait(false);

        if (Store is not null && !string.IsNullOrWhiteSpace(InstanceName))
            await Store.SetStatusAsync(InstanceName, false).ConfigureAwait(false);
    }
}
