namespace MichelOliveira.Com.ReactiveLock.Distributed.MongoDB;

using global::MongoDB.Driver;
using global::ReactiveLock.Shared.Distributed;
using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Polly;

/// <summary>
/// Configures MongoDB-backed distributed ReactiveLock trackers.
///
/// Registers tracker state and controllers with dependency injection, initializes the
/// MongoDB infrastructure, and starts change-stream synchronization for configured locks.
///
/// <para>
/// ⚠️ Notice: This file is part of the ReactiveLock library and is licensed under the MIT License.
/// You must follow the license, preserve the copyright notice, and comply with all legal terms
/// when using any part of this software.
/// See the LICENSE file in the project root for full license details.
/// © Michel Oliveira
/// </para>
/// </summary>
public static class ReactiveLockMongoDbTrackerExtensions
{
    public const string DefaultDatabaseName = "ReactiveLock";
    public const string DefaultCollectionName = "LockStatus";

    private static ReactiveLockMongoDbTrackerExtensionsState? ExtensionsState { get; set; }

    public static void InitializeDistributedMongoDbReactiveLock(
        this IServiceCollection services,
        string instanceName,
        string databaseName = DefaultDatabaseName,
        string collectionName = DefaultCollectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        ReactiveLockConventions.RegisterFactory(services);
        services.TryAddSingleton<IReactiveLockMongoDbClientAdapter>(serviceProvider =>
            new ReactiveLockMongoDbClientAdapter(
                serviceProvider.GetRequiredService<IMongoClient>(),
                databaseName,
                collectionName));

        ExtensionsState = string.IsNullOrEmpty(instanceName) ? null : new(instanceName);
    }

    public static IServiceCollection AddDistributedMongoDbReactiveLock(
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
                "InstanceName not initialized. Call InitializeDistributedMongoDbReactiveLock before adding distributed MongoDB reactive locks.");

        ReactiveLockConventions.RegisterState(services, lockKey, onLockedHandlers, onUnlockedHandlers);
        ReactiveLockConventions.RegisterController(services, lockKey, serviceProvider =>
        {
            var isNotInitializing = !ExtensionsState.IsInitializing;
            var hasPendingLockRegistrations = !ExtensionsState.RegisteredLocks.IsEmpty;
            if (isNotInitializing && hasPendingLockRegistrations)
            {
                throw new InvalidOperationException(
                    @"Distributed MongoDB reactive locks are not initialized.
                    Please ensure you're calling 'await app.UseDistributedMongoDbReactiveLockAsync();'
                    after 'var app = builder.Build();'.");
            }

            var mongoDb = serviceProvider.GetRequiredService<IReactiveLockMongoDbClientAdapter>();
            var store = new ReactiveLockMongoDbTrackerStore(
                mongoDb,
                ExtensionsState.InstanceName,
                customAsyncStorePolicy,
                resiliencyParameters,
                lockKey);

            return new ReactiveLockTrackerController(store, busyThreshold);
        });

        ExtensionsState.RegisteredLocks.Enqueue(lockKey);
        return services;
    }

    public static async Task UseDistributedMongoDbReactiveLockAsync(
        this IApplicationBuilder application,
        IAsyncPolicy? customAsyncSubscriberPolicy = default)
    {
        if (ExtensionsState is null)
            throw new InvalidOperationException(
                "InstanceName not initialized. Call InitializeDistributedMongoDbReactiveLock before adding distributed MongoDB reactive locks.");

        ExtensionsState.IsInitializing = true;
        var mongoDb = application.ApplicationServices.GetRequiredService<IReactiveLockMongoDbClientAdapter>();
        var factory = application.ApplicationServices.GetRequiredService<IReactiveLockTrackerFactory>();
        var retryPolicy = ReactiveLockPollyPolicies.UseOrCreateDefaultRetryPolicy(customAsyncSubscriberPolicy);
        var lockKeys = ExtensionsState.RegisteredLocks.ToHashSet(StringComparer.Ordinal);
        var controllers = lockKeys.ToDictionary(key => key, factory.GetTrackerController);
        var states = lockKeys.ToDictionary(key => key, factory.GetTrackerState);

        await retryPolicy.ExecuteAsync(() => mongoDb.EnsureInfrastructureAsync()).ConfigureAwait(false);

        async Task RefreshStateAsync(string lockKey)
        {
            if (!states.TryGetValue(lockKey, out var state))
                return;

            var (allIdle, lockData) = await ReactiveLockMongoDbTrackerStore
                .AreAllIdleAsync(lockKey, mongoDb)
                .ConfigureAwait(false);

            if (allIdle)
                await state.SetLocalStateUnblockedAsync().ConfigureAwait(false);
            else
                await state.SetLocalStateBlockedAsync(lockData).ConfigureAwait(false);
        }

        var readySignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(() => retryPolicy.ExecuteAsync(() => mongoDb.WatchAsync(
            lockKeys,
            RefreshStateAsync,
            () => readySignal.TrySetResult())));

        await readySignal.Task.ConfigureAwait(false);

        foreach (var controller in controllers.Values)
            await controller.DecrementAsync().ConfigureAwait(false);

        foreach (var lockKey in lockKeys)
            await RefreshStateAsync(lockKey).ConfigureAwait(false);

        ExtensionsState = null;
    }
}
