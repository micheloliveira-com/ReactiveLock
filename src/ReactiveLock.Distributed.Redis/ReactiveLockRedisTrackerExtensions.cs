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
    private static string? StoredInstanceName { get; set; }
    private static bool? IsInitializing { get; set; }

    /// <summary>
    /// Initializes the distributed Redis reactive lock system by registering the factory
    /// and storing the instance name for subsequent calls.
    /// Must be called before AddDistributedRedisReactiveLock.
    /// </summary>
    /// <param name="services">The IServiceCollection to register services to.</param>
    /// <param name="instanceName">The unique name identifying this instance.</param>
    public static void InitializeDistributedRedisReactiveLock(this IServiceCollection services, string instanceName)
    {
        ReactiveLockConventions.RegisterFactory(services);
        StoredInstanceName = instanceName;
    }
    
    /// <summary>
    /// Registers distributed Redis reactive lock services, configuring lock state, controller, and handlers.
    /// </summary>
    /// <param name="services">The IServiceCollection to add the lock services to.</param>
    /// <param name="lockKey">Unique key to identify the distributed lock.</param>
    /// <param name="onLockedHandlers">Optional collection of async handlers triggered when the lock is acquired.</param>
    /// <param name="onUnlockedHandlers">Optional collection of async handlers triggered when the lock is released.</param>
    /// <returns>The updated IServiceCollection instance.</returns>
    public static IServiceCollection AddDistributedRedisReactiveLock(
        this IServiceCollection services,
        string lockKey,
        IEnumerable<Func<IServiceProvider, Task>>? onLockedHandlers = null,
        IEnumerable<Func<IServiceProvider, Task>>? onUnlockedHandlers = null)
    {
        if (string.IsNullOrEmpty(StoredInstanceName))
        {
            throw new InvalidOperationException(
                "InstanceName not initialized. Call InitializeDistributedRedisReactiveLock before adding distributed Redis reactive locks.");
        }

        string redisHashSetKey = $"{HASHSET_PREFIX}{lockKey}";
        string redisHashSetNotifierKey = $"{HASHSET_NOTIFIER_PREFIX}{lockKey}";

        ReactiveLockConventions.RegisterState(services, lockKey, onLockedHandlers, onUnlockedHandlers);
        ReactiveLockConventions.RegisterController(services, lockKey, (sp) =>
        {
            var isInitializing = IsInitializing.HasValue && IsInitializing.Value;
            var isNotInitializing = !isInitializing;
            var hasPendingLockRegistrations = !RegisteredLocks.IsEmpty;

            if (isNotInitializing && hasPendingLockRegistrations)
            {
                throw new InvalidOperationException(
                    @"Distributed Redis reactive locks are not initialized.
                    Please ensure you're calling 'await app.UseDistributedRedisReactiveLockAsync();'
                    on your IApplicationBuilder instance after 'var app = builder.Build();'.");
            }
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var store = new ReactiveLockRedisTrackerStore(redis, redisHashSetKey, redisHashSetNotifierKey);
            return new ReactiveLockTrackerController(store, StoredInstanceName);
        });

        RegisteredLocks.Enqueue((lockKey, redisHashSetKey, redisHashSetNotifierKey));
        return services;
    }

    /// <summary>
    /// Initializes and subscribes to Redis notifications to track and update distributed lock states.
    /// </summary>
    /// <param name="application">The IApplicationBuilder used to access application services.</param>
    /// <returns>A Task representing the asynchronous operation.</returns>
    public static async Task UseDistributedRedisReactiveLockAsync(this IApplicationBuilder application)
    {
        IsInitializing = true;
        var redis = application.ApplicationServices.GetRequiredService<IConnectionMultiplexer>();
        var redisDb = redis.GetDatabase();
        var subscriber = redis.GetSubscriber();

        while (RegisteredLocks.TryDequeue(out var lockInfo))
        {
            var (lockKey, redisHashSetKey, redisHashSetNotifierSubscriptionKey) = lockInfo;

            var factory = application.ApplicationServices.GetRequiredService<IReactiveLockTrackerFactory>();
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
        IsInitializing = null;
        StoredInstanceName = null;
    }
    
    /// <summary>
    /// Checks Redis hash set entries to determine if all locks are idle (count zero or none).
    /// </summary>
    /// <param name="hashKey">Redis hash key holding lock counts.</param>
    /// <param name="redisDb">Redis database instance to query.</param>
    /// <returns>True if all lock counts are zero or no entries exist; otherwise false.</returns>
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
