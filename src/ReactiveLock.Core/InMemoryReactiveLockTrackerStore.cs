namespace MichelOliveira.Com.ReactiveLock.Core;

public class InMemoryReactiveLockTrackerStore(IReactiveLockTrackerState state) : IReactiveLockTrackerStore
{

    public Task SetStatusAsync(string instanceName, bool isBusy, string? lockData = default) =>
        isBusy
            ? state.SetLocalStateBlockedAsync(lockData)
            : state.SetLocalStateUnblockedAsync();

}
