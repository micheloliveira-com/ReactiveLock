namespace MichelOliveira.Com.ReactiveLock.Core;

public class InMemoryReactiveLockTrackerStore(IReactiveLockTrackerState state) : IReactiveLockTrackerStore
{

    public Task SetStatusAsync(string instanceName, bool isBusy) =>
        isBusy
            ? state.SetLocalStateBlockedAsync()
            : state.SetLocalStateUnblockedAsync();

}
