namespace MichelOliveira.Com.ReactiveLock.DependencyInjection;

using MichelOliveira.Com.ReactiveLock.Core;

public interface IReactiveLockTrackerFactory
{
    IReactiveLockTrackerController GetTrackerController(string lockKey);
    IReactiveLockTrackerState GetTrackerState(string lockKey);
}