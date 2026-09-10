namespace MichelOliveira.Com.ReactiveLock.Distributed.MongoDB;

using global::ReactiveLock.Shared.Distributed;
using MichelOliveira.Com.ReactiveLock.Core;
using Polly;

/// <summary>
/// Stores renewable per-instance lock state in MongoDB.
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
    private readonly ReactiveLockResilientReplicator _replicator = new(asyncPolicy, resiliencyParameters);
    private long _revision = DateTimeOffset.UtcNow.UtcTicks;

    public async Task SetStatusAsync(bool isBusy, string? lockData = default)
    {
        var revision = Interlocked.Increment(ref _revision);
        await _replicator.ExecuteAsync(instanceName, async validUntil =>
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
