namespace MichelOliveira.ReactiveLock.DependencyInjection;

using MichelOliveira.ReactiveLock.Core;
using Microsoft.Extensions.DependencyInjection;
using System;

public interface IReactiveLockTrackerFactory
{
    IReactiveLockTrackerController GetTrackerController(string key);
    IReactiveLockTrackerState GetTrackerState(string key);
}

public class ReactiveLockTrackerFactory : IReactiveLockTrackerFactory
{
    private readonly IServiceProvider _serviceProvider;

    public ReactiveLockTrackerFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IReactiveLockTrackerState GetTrackerState(string lockKey)
    {
        var stateKey = ReactiveLockConventions.GetStateKey(lockKey);
        var keyedProvider = _serviceProvider.GetKeyedService<IReactiveLockTrackerState>(stateKey)
            ?? throw new InvalidOperationException($"No state keyed service provider available for key '{stateKey}'.");
        return keyedProvider;
    }

    public IReactiveLockTrackerController GetTrackerController(string lockKey)
    {
        var controllerKey = ReactiveLockConventions.GetControllerKey(lockKey);
        var keyedProvider = _serviceProvider.GetKeyedService<IReactiveLockTrackerController>(controllerKey)
            ?? throw new InvalidOperationException($"No controller keyed service provider available for key '{controllerKey}'.");
        return keyedProvider;
    }
}
