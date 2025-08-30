namespace MichelOliveira.Com.ReactiveLock.Distributed.Redis;

using global::ReactiveLock.Shared.Distributed;
using MichelOliveira.Com.ReactiveLock.Core;
using Polly;
using StackExchange.Redis;
using System.Threading.Tasks;

/// <summary>
/// Implements a Redis-based tracker for distributed reactive locks.
///
/// Provides methods to check if all tracked locks are idle and to set/update
/// the status of a lock instance in Redis. Supports storing optional lock metadata
/// and notifying subscribers of status changes.
/// 
/// <para>
/// ⚠️ Notice: This file is part of the ReactiveLock library and is licensed under the MIT License.
/// You must follow license, preserve the copyright notice, and comply with all legal terms
/// when using any part of this software.
/// See the LICENSE file in the project root for full license details.
/// © Michel Oliveira
/// </para>
/// </summary>

public class ReactiveLockRedisTrackerStore(
    IConnectionMultiplexer redis, IAsyncPolicy asyncPolicy,
    TimeSpan instanceRenewalPeriodTimeSpan, TimeSpan instanceExpirationPeriodTimeSpan,
    string redisHashSetKey, string redisHashSetNotifierKey) : IReactiveLockTrackerStore
{
    private IDatabase RedisDb { get; } = redis.GetDatabase();
    private ISubscriber Subscriber { get; } = redis.GetSubscriber();
    private ReactiveLockResilientReplicator ReactiveLockResilientReplicator { get; } = new(instanceRenewalPeriodTimeSpan, instanceExpirationPeriodTimeSpan);

    /// <summary>
    /// Checks Redis hash set entries to determine if all locks are idle.
    /// Each entry's value is expected to start with a busy flag ("1" for busy, "0" or empty for idle),
    /// optionally followed by additional lock data separated by a semicolon.
    /// </summary>
    /// <param name="hashKey">The Redis hash key containing lock statuses.</param>
    /// <param name="redisDb">The Redis database instance used for querying.</param>
    /// <returns>
    /// A tuple where:
    /// <list type="bullet">
    ///   <item>
    ///     <description><c>true</c> if all locks are idle or no entries exist; otherwise <c>false</c>.</description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       A single string containing all extra data from busy locks concatenated and separated by <c>#REACTIVELOCK# (IReactiveLockTrackerState.LOCK_DATA_SEPARATOR)</c>, 
    ///       or <c>null</c> if none are busy or no extra data exists.
    ///     </description>
    ///   </item>
    /// </list>
    /// </returns>
    public static async Task<(bool allIdle, string? lockData)> AreAllIdleAsync(
        string hashKey,
        IDatabase redisDb)
    {
        var entries = await redisDb.HashGetAllAsync(hashKey).ConfigureAwait(false);
        if (entries.Length == 0)
            return (true, null);

        var busyEntries = entries
            .Where(entry => !entry.Value.IsNullOrEmpty)
            .Select(entry => entry.Value.ToString()!)
            .Select(raw =>
            {
                int sepIndex = raw.IndexOf(';');
                string busyPart = (sepIndex >= 0 ? raw[..sepIndex] : raw).Trim();
                string? extraPart = sepIndex >= 0 ? raw[(sepIndex + 1)..] : null;
                return (busyPart, extraPart);
            })
            .Where(x => x.busyPart == "1")
            .ToArray();

        if (busyEntries.Length == 0)
            return (true, null);

        var extraParts = busyEntries
            .Select(x => x.extraPart)
            .Where(extra => !string.IsNullOrEmpty(extra))
            .ToArray();

        string? lockData = extraParts.Length > 0
            ? string.Join(IReactiveLockTrackerState.LOCK_DATA_SEPARATOR, extraParts)
            : null;

        return (false, lockData);
    }


    public async Task SetStatusAsync(string instanceName, bool isBusy, string? lockData = default)
    {
        var statusValue = isBusy ? "1" : "0";
        if (!string.IsNullOrEmpty(lockData))
            statusValue += ";" + lockData;

        await ReactiveLockResilientReplicator.ExecuteAsync(instanceName, asyncPolicy, async () =>
        {
            await RedisDb.HashSetAsync(redisHashSetKey, instanceName, statusValue).ConfigureAwait(false);
            await Subscriber.PublishAsync(RedisChannel.Literal(redisHashSetNotifierKey), statusValue).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
}
