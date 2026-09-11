namespace MichelOliveira.Com.ReactiveLock.Distributed.MongoDB;

using global::ReactiveLock.Shared.Distributed;
using MichelOliveira.Com.ReactiveLock.Core;
using Polly;

/// <summary>
/// Stores renewable per-instance lock state in MongoDB.
///
/// Replicates local busy and idle transitions as renewable MongoDB lease documents and
/// resolves the combined state of all active instances for a logical lock.
///
/// <para>
/// ⚠️ Notice: This file is part of the ReactiveLock library and is licensed under the MIT License.
/// You must follow the license, preserve the copyright notice, and comply with all legal terms
/// when using any part of this software.
/// See the LICENSE file in the project root for full license details.
/// © Michel Oliveira
/// </para>
/// </summary>
public sealed class ReactiveLockMongoDbTrackerStore(
    IReactiveLockMongoDbClientAdapter mongoDb,
    string instanceName,
    IAsyncPolicy? asyncPolicy,
    (TimeSpan instanceRenewalPeriodTimeSpan,
     TimeSpan instanceExpirationPeriodTimeSpan,
     TimeSpan instanceRecoverPeriodTimeSpan) resiliencyParameters,
    string lockKey) : IReactiveLockTrackerStore
{
    private ReactiveLockResilientReplicator ReactiveLockResilientReplicator { get; } = new(asyncPolicy, resiliencyParameters);
    private long Revision = DateTimeOffset.UtcNow.UtcTicks;

    public async Task SetStatusAsync(bool isBusy, string? lockData = default)
    {
        var revision = Interlocked.Increment(ref Revision);
        await ReactiveLockResilientReplicator.ExecuteAsync(instanceName, async validUntil =>
        {
            var document = new ReactiveLockMongoDbDocument
            {
                Id = ReactiveLockMongoDbDocument.CreateId(lockKey, instanceName),
                LockKey = lockKey,
                InstanceId = instanceName,
                IsBusy = isBusy,
                LockData = lockData,
                ValidUntilUtc = validUntil.UtcDateTime,
                Revision = revision
            };

            await mongoDb.UpsertStatusAsync(document).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public static async Task<(bool allIdle, string? lockData)> AreAllIdleAsync(
        string lockKey,
        IReactiveLockMongoDbClientAdapter mongoDb,
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        var entries = await mongoDb.GetActiveBusyInstancesAsync(
            lockKey,
            now ?? DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);

        if (entries.Count == 0)
            return (true, null);

        var lockDataEntries = entries
            .OrderBy(entry => entry.InstanceId, StringComparer.Ordinal)
            .Select(entry => entry.LockData)
            .Where(data => !string.IsNullOrEmpty(data))
            .ToArray();

        return (
            false,
            lockDataEntries.Length == 0
                ? null
                : string.Join(IReactiveLockTrackerState.LOCK_DATA_SEPARATOR, lockDataEntries));
    }
}
