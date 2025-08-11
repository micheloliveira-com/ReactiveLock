namespace MichelOliveira.Com.ReactiveLock.Distributed.Grpc;

using global::ReactiveLock.Distributed.Grpc;
using static global::ReactiveLock.Distributed.Grpc.ReactiveLockGrpc;

using MichelOliveira.Com.ReactiveLock.Core;
using System.Linq;
using System.Threading.Tasks;

public class ReactiveLockGrpcTrackerStore(ReactiveLockGrpcClient client, string lockKey)
    : IReactiveLockTrackerStore
{
    public static (bool allIdle, string? lockData) AreAllIdle(LockStatusNotification update)
    {
        if (update.InstancesStatus.Count == 0)
            return (true, null);

        var busyEntries = update.InstancesStatus
            .Where(kv => kv.Value.IsBusy)
            .Select(kv => kv.Value.LockData)
            .ToArray();

        if (busyEntries.Length == 0)
            return (true, null);

        var extraParts = busyEntries
            .Where(extra => !string.IsNullOrEmpty(extra))
            .ToArray();

        string? lockData = extraParts.Length > 0
            ? string.Join(IReactiveLockTrackerState.LOCK_DATA_SEPARATOR, extraParts)
            : null;

        return (false, lockData);
    }

    public async Task SetStatusAsync(string instanceName, bool isBusy, string? lockData = null)
    {
        await client.SetStatusAsync(new LockStatusRequest
        {
            LockKey = lockKey,
            InstanceId = instanceName,
            IsBusy = isBusy,
            LockData = lockData
        }).ConfigureAwait(false);
    }
}