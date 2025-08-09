namespace MichelOliveira.Com.ReactiveLock.Distributed.Redis;

using MichelOliveira.Com.ReactiveLock.Core;
using StackExchange.Redis;
using System.Threading.Tasks;

public class ReactiveLockRedisTrackerStore(IConnectionMultiplexer redis, string redisHashSetKey, string redisHashSetNotifierKey) : IReactiveLockTrackerStore
{
    private IDatabase RedisDb { get; } = redis.GetDatabase();
    private ISubscriber Subscriber { get; } = redis.GetSubscriber();


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

        var busyLockDatas = entries
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
            .Select(x => x.extraPart)
            .Where(extra => !string.IsNullOrEmpty(extra))
            .ToArray();

        if (busyLockDatas.Length == 0)
            return (true, null);

        string lockData = string.Join(IReactiveLockTrackerState.LOCK_DATA_SEPARATOR, busyLockDatas);

        return (false, lockData);
    }

    public async Task SetStatusAsync(string instanceName, bool isBusy, string? lockData = default)
    {
        var statusValue = isBusy ? "1" : "0";
        if (!string.IsNullOrEmpty(lockData))
            statusValue += ";" + lockData;
        await RedisDb.HashSetAsync(redisHashSetKey, instanceName, statusValue).ConfigureAwait(false);
        await Subscriber.PublishAsync(RedisChannel.Literal(redisHashSetNotifierKey), statusValue).ConfigureAwait(false);
    }
}
