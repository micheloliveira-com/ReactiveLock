namespace MichelOliveira.Com.ReactiveLock.DependencyInjection;

using MichelOliveira.Com.ReactiveLock.Core;
using Microsoft.Extensions.DependencyInjection;

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