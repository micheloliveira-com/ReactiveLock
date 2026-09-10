namespace MichelOliveira.Com.ReactiveLock.Distributed.RabbitMQ;

using MichelOliveira.Com.ReactiveLock.Core;
using Polly;
using global::ReactiveLock.Shared.Distributed;

/// <summary>
/// Publishes this instance's renewable lock state through RabbitMQ.
/// </summary>
public sealed class ReactiveLockRabbitMqTrackerStore(
    IReactiveLockRabbitMqClientAdapter rabbitMq,
    string instanceName,
    IAsyncPolicy? asyncPolicy,
    (TimeSpan instanceRenewalPeriodTimeSpan,
     TimeSpan instanceExpirationPeriodTimeSpan,
     TimeSpan instanceRecoverPeriodTimeSpan) resiliencyParameters,
    string lockKey,
    string exchangeName) : IReactiveLockTrackerStore
{
    private readonly ReactiveLockResilientReplicator _replicator = new(asyncPolicy, resiliencyParameters);
    // A timestamp-based seed prevents a restarted process with the same instance name
    // from being rejected by peers that still cache its previous revision.
    private long _revision = DateTimeOffset.UtcNow.UtcTicks;

    public async Task SetStatusAsync(bool isBusy, string? lockData = default)
    {
        var revision = Interlocked.Increment(ref _revision);

        await _replicator.ExecuteAsync(instanceName, async validUntil =>
        {
            var message = new ReactiveLockRabbitMqMessage(
                ReactiveLockRabbitMqMessage.StatusKind,
                lockKey,
                instanceName,
                isBusy,
                lockData,
                validUntil.UtcTicks,
                revision);

            await rabbitMq.PublishAsync(exchangeName, message.Serialize()).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public static (bool allIdle, string? lockData) AreAllIdle(
        IEnumerable<ReactiveLockRabbitMqMessage> instanceStates,
        DateTimeOffset? now = null)
    {
        var currentTime = now ?? DateTimeOffset.UtcNow;
        var busyEntries = instanceStates
            .Where(entry => entry.Kind == ReactiveLockRabbitMqMessage.StatusKind
                            && entry.IsBusy
                            && entry.ValidUntil > currentTime)
            .ToArray();

        if (busyEntries.Length == 0)
            return (true, null);

        var lockDataEntries = busyEntries
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
