namespace MichelOliveira.Com.ReactiveLock.Distributed.Redis;

using System.Collections.Concurrent;
using System.Threading.Tasks;
using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

public static class ReactiveLockRedisTrackerExtensions
{
    
    private const string HASHSET_PREFIX = $"ReactiveLock:Redis:HashSet:";
    private const string HASHSET_NOTIFIER_PREFIX = $"ReactiveLock:Redis:HashSetNotifier:";

    private static ConcurrentQueue<(string lockKey, string redisHashSetKey, string redisHashSetNotifierSubscriptionKey)> RegisteredLocks { get; } = new();

    public static IServiceCollection AddDistributedRedisReactiveLock(
        this IServiceCollection services,
        string lockKey,
        IEnumerable<Func<IServiceProvider, Task>>? onLockedHandlers = null,
        IEnumerable<Func<IServiceProvider, Task>>? onUnlockedHandlers = null)
    {
        string redisHashSetKey = $"{HASHSET_PREFIX}{lockKey}";
        string redisHashSetNotifierKey = $"{HASHSET_NOTIFIER_PREFIX}{lockKey}";

        ReactiveLockConventions.RegisterFactory(services);        
        ReactiveLockConventions.RegisterState(services, lockKey, onLockedHandlers, onUnlockedHandlers);
        ReactiveLockConventions.RegisterController(services, lockKey, (sp) =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var factory = sp.GetRequiredService<IReactiveLockTrackerFactory>();
            var state = factory.GetTrackerState(lockKey);
            var store = new ReactiveLockRedisTrackerStore(redis, redisHashSetKey, redisHashSetNotifierKey);
            return new ReactiveLockTrackerController(store, state);
        });
        
        RegisteredLocks.Enqueue((lockKey, redisHashSetKey, redisHashSetNotifierKey));
        return services;
    }


    public static async Task UseDistributedRedisReactiveLockAsync(this IApplicationBuilder app)
    {
        var redis = app.ApplicationServices.GetRequiredService<IConnectionMultiplexer>();
        var redisDb = redis.GetDatabase();
        var subscriber = redis.GetSubscriber();

        while (RegisteredLocks.TryDequeue(out var lockInfo))
        {
            var (lockKey, redisHashSetKey, redisHashSetNotifierSubscriptionKey) = lockInfo;

            var factory = app.ApplicationServices.GetRequiredService<IReactiveLockTrackerFactory>();
            var state = factory.GetTrackerState(lockKey);
            var controller = factory.GetTrackerController(lockKey);
            await controller.DecrementAsync().ConfigureAwait(false);
            subscriber.Subscribe(RedisChannel.Literal(redisHashSetNotifierSubscriptionKey), (channel, message) =>
            {
                _ = Task.Run(async () =>
                {
                    bool allIdle = await AreAllIdleAsync(redisHashSetKey, redisDb).ConfigureAwait(false);

                    if (allIdle)
                    {
                        await state.SetLocalStateUnblockedAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        await state.SetLocalStateBlockedAsync().ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
            });
        }
    }
    
    private static async Task<bool> AreAllIdleAsync(string hashKey, IDatabase redisDb)
    {
        var values = await redisDb.HashGetAllAsync(hashKey)
                        .ConfigureAwait(false);
        if (values.Length == 0) return true;

        foreach (var entry in values)
        {
            if (int.TryParse(entry.Value.ToString(), out var count) && count > 0)
                return false;
        }

        return true;
    }

}