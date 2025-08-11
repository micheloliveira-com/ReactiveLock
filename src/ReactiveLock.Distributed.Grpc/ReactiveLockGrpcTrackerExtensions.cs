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
public static class ReactiveLockGrpcTrackerExtensions
{
    private static bool? IsInitializing { get; set; }
    private static readonly ConcurrentQueue<string> RegisteredLocks = new();
    private static string? StoredInstanceName;
    private static ReactiveLockGrpcClient? LocalClient;
    private static List<ReactiveLockGrpcClient> RemoteClients = new();

    public static void InitializeDistributedGrpcReactiveLock(this IServiceCollection services, string instanceName, string mainGrpcServer, params string[] replicaGrpcServers)
    {
        ReactiveLockConventions.RegisterFactory(services);
        StoredInstanceName = instanceName;
        LocalClient = new ReactiveLockGrpcClient(GrpcChannel.ForAddress(mainGrpcServer));
        RemoteClients.AddRange(replicaGrpcServers.Select(url => new ReactiveLockGrpcClient(GrpcChannel.ForAddress(url))));
    }

    public static IServiceCollection AddDistributedGrpcReactiveLock(
        this IServiceCollection services,
        string lockKey,
        IEnumerable<Func<IServiceProvider, Task>>? onLockedHandlers = null,
        IEnumerable<Func<IServiceProvider, Task>>? onUnlockedHandlers = null)
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
            return new ReactiveLockTrackerController(store, StoredInstanceName);
        });

        RegisteredLocks.Enqueue(lockKey);
        return services;
    }
    public static async Task UseDistributedGrpcReactiveLockAsync(this IApplicationBuilder app)
    {
        IsInitializing = true;
        var factory = app.ApplicationServices.GetRequiredService<IReactiveLockTrackerFactory>();

        var instanceLocalClient = LocalClient!;
        var instanceStoredInstanceName = StoredInstanceName!;
        var instanceRemoteClients = RemoteClients;

        var readySignals = new List<Task>();

        foreach (var lockKey in RegisteredLocks)
        {
            var state = factory.GetTrackerState(lockKey);
            var controller = factory.GetTrackerController(lockKey);
            await controller.DecrementAsync().ConfigureAwait(false);

            async Task SubscribeToUpdates(ReactiveLockGrpcClient client, string storedInstanceName, TaskCompletionSource readySignal)
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
                    var (allIdle, lockData) = ReactiveLockGrpcTrackerStore.AreAllIdleFromRpc(update);

                    if (allIdle)
                        await state.SetLocalStateUnblockedAsync().ConfigureAwait(false);
                    else
                        await state.SetLocalStateBlockedAsync(lockData).ConfigureAwait(false);
                }
            }

            var tcsLocal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            readySignals.Add(tcsLocal.Task);
            _ = Task.Run(() => SubscribeToUpdates(instanceLocalClient, instanceStoredInstanceName, tcsLocal));

            foreach (var remote in instanceRemoteClients)
            {
                var tcsRemote = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                readySignals.Add(tcsRemote.Task);
                _ = Task.Run(() => SubscribeToUpdates(remote, instanceStoredInstanceName, tcsRemote));
            }
        }

        await Task.WhenAll(readySignals).ConfigureAwait(false);

        IsInitializing = null;
        StoredInstanceName = null;
        LocalClient = null;
        RemoteClients = new();
    }

}
