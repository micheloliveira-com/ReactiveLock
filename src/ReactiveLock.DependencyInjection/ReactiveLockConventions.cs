namespace MichelOliveira.Com.ReactiveLock.DependencyInjection;

using MichelOliveira.Com.ReactiveLock.Core;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides extension methods and conventions for registering ReactiveLock services
/// into an <see cref="IServiceCollection"/> for dependency injection.
///
/// This class centralizes naming conventions for controller and state keys,
/// and offers helper methods to register:
/// - <see cref="IReactiveLockTrackerFactory"/>
/// - <see cref="IReactiveLockTrackerState"/> for a given lock key
/// - <see cref="IReactiveLockTrackerController"/> for a given lock key
///
/// Ensures consistent registration patterns and keyed singletons for multiple locks
/// within the same DI container.
///
/// <para>
/// ⚠️ Notice: This file is part of the ReactiveLock library and is licensed under the MIT License.
/// You must follow license, preserve the copyright notice, and comply with all legal terms
/// when using any part of this software.
/// See the LICENSE file in the project root for full license details.
/// © Michel Oliveira
/// </para>
/// </summary>
public static class ReactiveLockConventions
{
    private const string CONTROLLER_PREFIX = $"ReactiveLock:Controller:";
    private const string STATE_PREFIX = $"ReactiveLock:State:";

    public static string GetControllerKey(string lockKey)
        => $"{CONTROLLER_PREFIX}{lockKey}";

    public static string GetStateKey(string lockKey)
        => $"{STATE_PREFIX}{lockKey}";


    public static IServiceCollection RegisterFactory(IServiceCollection services)
    {
        return services.AddSingleton<IReactiveLockTrackerFactory, ReactiveLockTrackerFactory>();
    }

    public static IServiceCollection RegisterState(IServiceCollection services, string lockKey,
        IEnumerable<Func<IServiceProvider, Task>>? onLockedHandlers = null,
        IEnumerable<Func<IServiceProvider, Task>>? onUnlockedHandlers = null)
    {
        return services.AddKeyedSingleton<IReactiveLockTrackerState, ReactiveLockTrackerState>(GetStateKey(lockKey), (sp, _) =>
        {
            return new ReactiveLockTrackerState(sp, onLockedHandlers, onUnlockedHandlers);
        });
    }

    public static IServiceCollection RegisterController(
        this IServiceCollection services,
        string lockKey,
        Func<IServiceProvider, IReactiveLockTrackerController> factory)
    {
        services.AddKeyedSingleton(GetControllerKey(lockKey), (sp, _) => factory(sp));
        return services;
    }
}