namespace MichelOliveira.Com.ReactiveLock.Distributed.RabbitMQ;

using System.Collections.Concurrent;

internal sealed class ReactiveLockRabbitMqTrackerExtensionsState(string instanceName)
{
    public string InstanceName { get; } = instanceName;
    public bool IsInitializing { get; set; }
    public ConcurrentQueue<(string LockKey, string ExchangeName)> RegisteredLocks { get; } = new();
}
