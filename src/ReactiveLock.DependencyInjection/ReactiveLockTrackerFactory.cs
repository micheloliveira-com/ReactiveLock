namespace MichelOliveira.Com.ReactiveLock.DependencyInjection;

using MichelOliveira.Com.ReactiveLock.Core;
using Microsoft.Extensions.DependencyInjection;
using System;

public class ReactiveLockTrackerFactory : IReactiveLockTrackerFactory
{
    private IServiceProvider ServiceProvider { get; }

    public ReactiveLockTrackerFactory(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
    }

    public IReactiveLockTrackerState GetTrackerState(string lockKey)
    {
        var stateKey = ReactiveLockConventions.GetStateKey(lockKey);
        var keyedProvider = ServiceProvider.GetKeyedService<IReactiveLockTrackerState>(stateKey)
            ?? throw new InvalidOperationException($"No state keyed service provider available for key '{stateKey}'.");
        return keyedProvider;
    }

    public IReactiveLockTrackerController GetTrackerController(string lockKey)
    {
        var controllerKey = ReactiveLockConventions.GetControllerKey(lockKey);
        var keyedProvider = ServiceProvider.GetKeyedService<IReactiveLockTrackerController>(controllerKey)
            ?? throw new InvalidOperationException($"No controller keyed service provider available for key '{controllerKey}'.");
        return keyedProvider;
    }
}
