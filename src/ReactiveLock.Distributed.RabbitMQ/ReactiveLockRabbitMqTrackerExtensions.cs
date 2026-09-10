namespace MichelOliveira.Com.ReactiveLock.Distributed.RabbitMQ;

using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Polly;
using global::ReactiveLock.Shared.Distributed;
using System.Collections.Concurrent;

/// <summary>
/// Configures RabbitMQ-backed distributed ReactiveLock trackers.
/// </summary>
public static class ReactiveLockRabbitMqTrackerExtensions
{
    private const string ExchangePrefix = "ReactiveLock:RabbitMQ:Exchange:";
    private static ReactiveLockRabbitMqTrackerExtensionsState? ExtensionsState { get; set; }

    public static void InitializeDistributedRabbitMqReactiveLock(
        this IServiceCollection services,
        string instanceName)
    {
        ReactiveLockConventions.RegisterFactory(services);
        services.TryAddSingleton<IReactiveLockRabbitMqClientAdapter, ReactiveLockRabbitMqClientAdapter>();
        ExtensionsState = string.IsNullOrEmpty(instanceName) ? null : new(instanceName);
    }

    public static IServiceCollection AddDistributedRabbitMqReactiveLock(
        this IServiceCollection services,
        string lockKey,
        IEnumerable<Func<IServiceProvider, Task>>? onLockedHandlers = null,
        IEnumerable<Func<IServiceProvider, Task>>? onUnlockedHandlers = null,
        int busyThreshold = 1,
        IAsyncPolicy? customAsyncStorePolicy = default,
        (TimeSpan instanceRenewalPeriodTimeSpan,
         TimeSpan instanceExpirationPeriodTimeSpan,
         TimeSpan instanceRecoverPeriodTimeSpan) resiliencyParameters = default)
    {
        if (ExtensionsState is null)
            throw new InvalidOperationException(
                "InstanceName not initialized. Call InitializeDistributedRabbitMqReactiveLock before adding distributed RabbitMQ reactive locks.");

        var exchangeName = $"{ExchangePrefix}{lockKey}";

        ReactiveLockConventions.RegisterState(services, lockKey, onLockedHandlers, onUnlockedHandlers);
        ReactiveLockConventions.RegisterController(services, lockKey, serviceProvider =>
        {
            var isNotInitializing = !ExtensionsState.IsInitializing;
            var hasPendingLockRegistrations = !ExtensionsState.RegisteredLocks.IsEmpty;
            if (isNotInitializing && hasPendingLockRegistrations)
            {
                throw new InvalidOperationException(
                    @"Distributed RabbitMQ reactive locks are not initialized.
                    Please ensure you're calling 'await app.UseDistributedRabbitMqReactiveLockAsync();'
                    after 'var app = builder.Build();'.");
            }

            var rabbitMq = serviceProvider.GetRequiredService<IReactiveLockRabbitMqClientAdapter>();
            var store = new ReactiveLockRabbitMqTrackerStore(
                rabbitMq,
                ExtensionsState.InstanceName,
                customAsyncStorePolicy,
                resiliencyParameters,
                lockKey,
                exchangeName);

            return new ReactiveLockTrackerController(store, busyThreshold);
        });

        ExtensionsState.RegisteredLocks.Enqueue((lockKey, exchangeName));
        return services;
    }

    public static async Task UseDistributedRabbitMqReactiveLockAsync(
        this IApplicationBuilder application,
        IAsyncPolicy? customAsyncSubscriberPolicy = default)
    {
        if (ExtensionsState is null)
            throw new InvalidOperationException(
                "InstanceName not initialized. Call InitializeDistributedRabbitMqReactiveLock before adding distributed RabbitMQ reactive locks.");

        ExtensionsState.IsInitializing = true;
        var instanceName = ExtensionsState.InstanceName;
        var rabbitMq = application.ApplicationServices.GetRequiredService<IReactiveLockRabbitMqClientAdapter>();
        var factory = application.ApplicationServices.GetRequiredService<IReactiveLockTrackerFactory>();
        var retryPolicy = ReactiveLockPollyPolicies.UseOrCreateDefaultRetryPolicy(customAsyncSubscriberPolicy);

        while (ExtensionsState.RegisteredLocks.TryDequeue(out var registration))
        {
            var (lockKey, exchangeName) = registration;
            var state = factory.GetTrackerState(lockKey);
            var controller = factory.GetTrackerController(lockKey);
            var instanceStates = new ConcurrentDictionary<string, ReactiveLockRabbitMqMessage>();

            await retryPolicy.ExecuteAsync(() => rabbitMq.SubscribeAsync(exchangeName, async body =>
            {
                var message = ReactiveLockRabbitMqMessage.Deserialize(body.Span);
                if (message is null || message.LockKey != lockKey)
                    return;

                if (message.Kind == ReactiveLockRabbitMqMessage.SnapshotRequestKind)
                {
                    if (instanceStates.TryGetValue(instanceName, out var ownState))
                        await rabbitMq.PublishAsync(exchangeName, ownState.Serialize()).ConfigureAwait(false);
                    return;
                }

                if (message.Kind != ReactiveLockRabbitMqMessage.StatusKind
                    || string.IsNullOrEmpty(message.InstanceId))
                    return;

                instanceStates.AddOrUpdate(
                    message.InstanceId,
                    message,
                    (_, current) => IsNewer(message, current) ? message : current);

                var (allIdle, lockData) = ReactiveLockRabbitMqTrackerStore.AreAllIdle(instanceStates.Values);
                if (allIdle)
                    await state.SetLocalStateUnblockedAsync().ConfigureAwait(false);
                else
                    await state.SetLocalStateBlockedAsync(lockData).ConfigureAwait(false);
            })).ConfigureAwait(false);

            // Subscribe before publishing so this process observes its own initial state.
            await controller.DecrementAsync().ConfigureAwait(false);
            var request = ReactiveLockRabbitMqMessage.CreateSnapshotRequest(lockKey, instanceName);
            await rabbitMq.PublishAsync(exchangeName, request.Serialize()).ConfigureAwait(false);
        }

        ExtensionsState = null;
    }

    private static bool IsNewer(
        ReactiveLockRabbitMqMessage candidate,
        ReactiveLockRabbitMqMessage current) =>
        current.ValidUntil <= DateTimeOffset.UtcNow
        || candidate.Revision > current.Revision
        || (candidate.Revision == current.Revision
            && candidate.ValidUntilUtcTicks > current.ValidUntilUtcTicks);
}
