using System.Collections.Concurrent;
using Grpc.Core;
using Google.Protobuf.WellKnownTypes;
using ReactiveLock.Distributed.Grpc;


public class ReactiveLockGrpcService : ReactiveLockGrpc.ReactiveLockGrpcBase
{
    private ConcurrentDictionary<string, LockGroup> Groups { get; } = [];
    public override async Task<Empty> SetStatus(LockStatusRequest request, ServerCallContext context)
    {
        var group = Groups.GetOrAdd(request.LockKey, _ => new LockGroup());
        group.InstanceStates[request.InstanceId] =
                new InstanceLockStatus()
                {
                    IsBusy = request.IsBusy,
                    LockData = request.LockData
                };
        await BroadcastAsync(request.LockKey, group);
        return new Empty();
    }

    public override async Task SubscribeLockStatus(IAsyncStreamReader<LockStatusRequest> requestStream,
                                                   IServerStreamWriter<LockStatusNotification> responseStream,
                                                   ServerCallContext context)
    {
        await foreach (var req in requestStream.ReadAllAsync(context.CancellationToken).ConfigureAwait(false))
        {
            var group = Groups.GetOrAdd(req.LockKey, _ => new LockGroup());
            group.Subscribers.Add(new Subscriber(responseStream, requestStream));

            await responseStream.WriteAsync(new LockStatusNotification
            {
                LockKey = req.LockKey,
                InstancesStatus = { group.InstanceStates }
            }).ConfigureAwait(false);

            break;
        }
        await Task.Delay(Timeout.Infinite, context.CancellationToken).ConfigureAwait(false);
    }

    private async Task BroadcastAsync(string lockKey, LockGroup group)
    {
        var notification = new LockStatusNotification
        {
            LockKey = lockKey,
            InstancesStatus = { group.InstanceStates }
        };

        foreach (var subscriber in group.Subscribers.ToArray())
        {
            try
            {
                await subscriber.ResponseStream.WriteAsync(notification).ConfigureAwait(false);
                //await subscriber.RequestStream.MoveNext();
            }
            catch
            {
                group.Subscribers.TryTake(out _);
            }
        }
    }
}
