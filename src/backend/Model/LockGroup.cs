
using System.Collections.Concurrent;
using Grpc.Core;
using ReactiveLock.Distributed.Grpc;
public class Subscriber
{
    public IServerStreamWriter<LockStatusNotification> ResponseStream { get; }
    public IAsyncStreamReader<LockStatusRequest> RequestStream { get; }

    public Subscriber(IServerStreamWriter<LockStatusNotification> responseStream,
                      IAsyncStreamReader<LockStatusRequest> requestStream)
    {
        ResponseStream = responseStream;
        RequestStream = requestStream;
    }}
public class LockGroup
{
    public ConcurrentDictionary<string, InstanceLockStatus> InstanceStates { get; } = new();
    public ConcurrentBag<Subscriber> Subscribers { get; } = new();
}