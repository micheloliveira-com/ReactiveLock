namespace MichelOliveira.Com.ReactiveLock.Core;

using System.Net;
using System.Threading;
using System.Threading.Tasks;

public interface IReactiveLockTrackerStore
{
    Task SetStatusAsync(string hostname, bool isBusy);
}


public interface IReactiveLockTrackerController
{
    Task IncrementAsync();
    Task DecrementAsync(int amount = 1);
}
public class ReactiveLockTrackerController : IReactiveLockTrackerController
{
    private IReactiveLockTrackerStore Store { get; }
    private IReactiveLockTrackerState State { get; }
    private string Hostname { get; }

    private int _inFlightRequestCount;

    public ReactiveLockTrackerController(IReactiveLockTrackerStore store, IReactiveLockTrackerState state)
    {
        Store = store;
        State = state;
        Hostname = System.Net.Dns.GetHostName();
    }

    public async Task IncrementAsync()
    {
        var newCount = Interlocked.Increment(ref _inFlightRequestCount);
        if (newCount == 1)
        {
            await Store.SetStatusAsync(Hostname, true).ConfigureAwait(false);
        }
    }

    public async Task DecrementAsync(int amount = 1)
    {
        var afterCount = Interlocked.Add(ref _inFlightRequestCount, -amount);
        if (afterCount <= 0)
        {
            Interlocked.Exchange(ref _inFlightRequestCount, 0);
            await Store.SetStatusAsync(Hostname, false).ConfigureAwait(false);
        }
    }
}
