namespace MichelOliveira.Com.ReactiveLock.DependencyInjection;

using MichelOliveira.Com.ReactiveLock.Core;
using Microsoft.Extensions.DependencyInjection;
using System;

/// <summary>
/// Factory for retrieving ReactiveLock tracker instances from a dependency injection container.
///
/// Provides methods to obtain:
/// - <see cref="IReactiveLockTrackerState"/> for a specific lock key
/// - <see cref="IReactiveLockTrackerController"/> for a specific lock key
///
/// This factory relies on keyed DI registration conventions defined in
/// <see cref="ReactiveLockConventions"/> to resolve the correct instances.
///
/// Throws <see cref="InvalidOperationException"/> if a requested keyed service is not registered.
///
/// <para>
/// ⚠️ Notice: This file is part of the ReactiveLock library and is licensed under the MIT License.
/// You must follow license, preserve the copyright notice, and comply with all legal terms
/// when using any part of this software.
/// See the LICENSE file in the project root for full license details.
/// © Michel Oliveira
/// </para>
/// </summary>
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
