namespace MichelOliveira.Com.ReactiveLock.Distributed.Grpc;

using static global::ReactiveLock.Distributed.Grpc.ReactiveLockGrpc;
using global::Grpc.Net.Client;
using global::ReactiveLock.Distributed.Grpc;
using global::Grpc.Core;

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;
using System.Linq;
using System.Threading.Tasks;
using System;
using Polly;
using global::ReactiveLock.Shared.Distributed;

/// <summary>
/// Provides extension methods to integrate ReactiveLock with gRPC-based distributed lock tracking.
/// 
/// Allows registration and initialization of distributed reactive locks across multiple
/// application instances using gRPC. Handles state synchronization between local and remote instances,
/// and ensures proper initialization and subscription to lock updates.
/// 
/// <para>
/// ⚠️ Notice: This file is part of the ReactiveLock library and is licensed under the MIT License.
/// You must follow license, preserve the copyright notice, and comply with all legal terms
/// when using any part of this software.
/// See the LICENSE file in the project root for full license details.
/// © Michel Oliveira
/// </para>
/// </summary>
public static class ReactiveLockGrpcTrackerExtensions
{
    private static bool? IsInitializing { get; set; }
    private static ConcurrentQueue<string> RegisteredLocks { get; } = new();
    private static string? StoredInstanceName { get; set; }
    private static IReactiveLockGrpcClientAdapter? LocalClient { get; set; }
    private static List<IReactiveLockGrpcClientAdapter> RemoteClients  { get; set; } = new();

    public static void InitializeDistributedGrpcReactiveLock(this IServiceCollection services, string instanceName, string mainGrpcServer, params string[] replicaGrpcServers)
    {
        ReactiveLockConventions.RegisterFactory(services);
        StoredInstanceName = instanceName;
        LocalClient = new ReactiveLockGrpcClientAdapter(new ReactiveLockGrpcClient(GrpcChannel.ForAddress(mainGrpcServer)));
        RemoteClients.AddRange(
            replicaGrpcServers.Select(url =>
                new ReactiveLockGrpcClientAdapter(
                    new ReactiveLockGrpcClient(GrpcChannel.ForAddress(url))
                )
            )
        );
    }

    public static IServiceCollection AddDistributedGrpcReactiveLock(
        this IServiceCollection services,
        string lockKey,
        IEnumerable<Func<IServiceProvider, Task>>? onLockedHandlers = null,
        IEnumerable<Func<IServiceProvider, Task>>? onUnlockedHandlers = null,
        int busyThreshold = 1)
    {
        if (LocalClient is null || string.IsNullOrEmpty(StoredInstanceName))
        {
            throw new InvalidOperationException(
                "InstanceName not initialized. Call InitializeDistributedGrpcReactiveLock before adding distributed Grpc reactive locks.");
        }

        ReactiveLockConventions.RegisterState(services, lockKey, onLockedHandlers, onUnlockedHandlers);
        ReactiveLockConventions.RegisterController(services, lockKey, _ =>
        {
            var isInitializing = IsInitializing.HasValue && IsInitializing.Value;
            var isNotInitializing = !isInitializing;
            var hasPendingLockRegistrations = !RegisteredLocks.IsEmpty;

            if (isNotInitializing && hasPendingLockRegistrations)
            {
                throw new InvalidOperationException(
                    @"Distributed Grpc reactive locks are not initialized.
                    Please ensure you're calling 'await app.UseDistributedGrpcReactiveLockAsync();'
                    on your IApplicationBuilder instance after 'var app = builder.Build();'.");
            }
            var store = new ReactiveLockGrpcTrackerStore(LocalClient, lockKey);
            return new ReactiveLockTrackerController(store, StoredInstanceName, busyThreshold);
        });

        RegisteredLocks.Enqueue(lockKey);
        return services;
    }
    
    private static async Task SubscribeToUpdates(
        IReactiveLockGrpcClientAdapter client,
        string storedInstanceName,
        string lockKey,
        TaskCompletionSource readySignal,
        IReactiveLockTrackerState state,
        IAsyncPolicy retryPolicy)
    {
        await retryPolicy.ExecuteAsync(async () =>
        {
            var call = client.SubscribeLockStatus();
            await call.RequestStream.WriteAsync(new LockStatusRequest
            {
                LockKey = lockKey,
                InstanceId = storedInstanceName!
            }).ConfigureAwait(false);

            readySignal.TrySetResult();

            await foreach (var update in call.ResponseStream.ReadAllAsync().ConfigureAwait(false))
            {
                var (allIdle, lockData) = ReactiveLockGrpcTrackerStore.AreAllIdle(update);

                if (allIdle)
                    await state.SetLocalStateUnblockedAsync().ConfigureAwait(false);
                else
                    await state.SetLocalStateBlockedAsync(lockData).ConfigureAwait(false);
            }
        });
    }


    public static async Task UseDistributedGrpcReactiveLockAsync(this IApplicationBuilder app)
    {
        IsInitializing = true;
        var factory = app.ApplicationServices.GetRequiredService<IReactiveLockTrackerFactory>();

        var instanceLocalClient = LocalClient!;
        var instanceStoredInstanceName = StoredInstanceName!;
        var instanceRemoteClients = RemoteClients;

        var readySignals = new List<Task>();
        var retryPolicy = ReactiveLockPollyPolicies.CreateRetryPolicy();

        foreach (var lockKey in RegisteredLocks)
        {
            var state = factory.GetTrackerState(lockKey);
            var controller = factory.GetTrackerController(lockKey);
            await controller.DecrementAsync().ConfigureAwait(false);


            var tcsLocal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            readySignals.Add(tcsLocal.Task);
            _ = Task.Run(() => SubscribeToUpdates(instanceLocalClient, instanceStoredInstanceName, lockKey, tcsLocal, state, retryPolicy));

            foreach (var remote in instanceRemoteClients)
            {
                var tcsRemote = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                readySignals.Add(tcsRemote.Task);
                _ = Task.Run(() => SubscribeToUpdates(remote, instanceStoredInstanceName, lockKey, tcsRemote, state, retryPolicy));
            }
        }

        await Task.WhenAll(readySignals).ConfigureAwait(false);

        IsInitializing = null;
        StoredInstanceName = null;
        LocalClient = null;
        RemoteClients = new();
    }

}
