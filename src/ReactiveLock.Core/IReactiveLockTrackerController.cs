namespace MichelOliveira.Com.ReactiveLock.Core;

public interface IReactiveLockTrackerController
{
    Task IncrementAsync();
    Task DecrementAsync(int amount = 1);
}