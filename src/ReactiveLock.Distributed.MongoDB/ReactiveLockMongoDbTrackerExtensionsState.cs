namespace MichelOliveira.Com.ReactiveLock.Distributed.MongoDB;

using System.Collections.Concurrent;

internal sealed class ReactiveLockMongoDbTrackerExtensionsState(string instanceName)
{
    public string InstanceName { get; } = instanceName;
    public bool IsInitializing { get; set; }
    public ConcurrentQueue<string> RegisteredLocks { get; } = new();
}
