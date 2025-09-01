namespace MichelOliveira.Com.ReactiveLock.Distributed.Grpc;

using global::ReactiveLock.Distributed.Grpc;
using global::ReactiveLock.Shared.Distributed;
using Google.Protobuf.WellKnownTypes;
using MichelOliveira.Com.ReactiveLock.Core;
using Polly;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Implements a gRPC-based reactive lock store for distributed lock coordination.
/// 
/// Tracks the busy/idle state of a lock across multiple application instances
/// and propagates updates via gRPC. Provides methods to update the lock status
/// and compute overall lock state from notifications received from remote instances.
/// 
/// <para>
/// ⚠️ Notice: This file is part of the ReactiveLock library and is licensed under the MIT License.
/// You must follow license, preserve the copyright notice, and comply with all legal terms
/// when using any part of this software.
/// See the LICENSE file in the project root for full license details.
/// © Michel Oliveira
/// </para>
/// </summary>
public class ReactiveLockGrpcTrackerStore(
    List<IReactiveLockGrpcClientAdapter> clients, IAsyncPolicy? asyncPolicy,
    (TimeSpan instanceRenewalPeriodTimeSpan, TimeSpan instanceExpirationPeriodTimeSpan, TimeSpan instanceRecoverPeriodTimeSpan) resiliencyParameters,
    string lockKey) : IReactiveLockTrackerStore
{
    private ReactiveLockResilientReplicator ReactiveLockResilientReplicator { get; } =
        new(asyncPolicy, resiliencyParameters);

    /// <summary>
    /// Evaluates if all instances are idle, respecting expiration of busy entries.
    /// </summary>
    public static (bool allIdle, string? lockData) AreAllIdle(LockStatusNotification update)
    {
        if (update.InstancesStatus.Count == 0)
            return (true, null);

        var now = DateTimeOffset.UtcNow;

        var busyEntries = update.InstancesStatus
            .Where(kv => kv.Value.IsBusy
                         && kv.Value.ValidUntil.ToDateTimeOffset() > now)
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

    /// <summary>
    /// Updates the status of this instance in all gRPC clients.
    /// </summary>
    public async Task SetStatusAsync(string instanceName, bool isBusy, string? lockData = null)
    {
        int index = 0;
        foreach (var client in clients)
        {
            var replicatorInstanceName = $"{index++}-{instanceName}";
            await ReactiveLockResilientReplicator.ExecuteAsync(replicatorInstanceName, async (validUntil) =>
            {
                await client.SetStatusAsync(new LockStatusRequest
                {
                    LockKey = lockKey,
                    InstanceId = instanceName,
                    IsBusy = isBusy,
                    LockData = lockData,
                    ValidUntil = Timestamp.FromDateTimeOffset(validUntil)
                }).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
    }
}
