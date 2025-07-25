namespace MichelOliveira.Com.ReactiveLock.Core;

public interface IReactiveLockTrackerStore
{
    Task SetStatusAsync(string hostname, bool isBusy);
}