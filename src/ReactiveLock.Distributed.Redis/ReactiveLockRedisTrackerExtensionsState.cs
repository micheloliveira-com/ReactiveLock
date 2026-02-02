namespace MichelOliveira.Com.ReactiveLock.Distributed.Redis;

using System.Collections.Concurrent;

internal sealed class ReactiveLockRedisTrackerExtensionsState(string instanceName)
{
    public string InstanceName { get; } = instanceName;
    public bool IsInitializing { get; set; }

    public ConcurrentQueue<(
        string lockKey, 
        string redisHashSetKey, 
        string redisHashSetNotifierSubscriptionKey)> RegisteredLocks { get; } = new();
}