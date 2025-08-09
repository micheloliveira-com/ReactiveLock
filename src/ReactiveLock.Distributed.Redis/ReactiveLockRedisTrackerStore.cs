namespace MichelOliveira.Com.ReactiveLock.Distributed.Redis;

using MichelOliveira.Com.ReactiveLock.Core;
using StackExchange.Redis;
using System.Threading.Tasks;

public class ReactiveLockRedisTrackerStore(IConnectionMultiplexer redis, string redisHashSetKey, string redisHashSetNotifierKey) : IReactiveLockTrackerStore
{
    private IDatabase RedisDb { get; } = redis.GetDatabase();
    private ISubscriber Subscriber { get; } = redis.GetSubscriber();

    public async Task SetStatusAsync(string instanceName, bool isBusy, string? lockData = default)
    {
        var statusValue = isBusy ? "1" : "0";
        if (!string.IsNullOrEmpty(lockData))
            statusValue += ";" + lockData;
        await RedisDb.HashSetAsync(redisHashSetKey, instanceName, statusValue).ConfigureAwait(false);
        await Subscriber.PublishAsync(RedisChannel.Literal(redisHashSetNotifierKey), statusValue).ConfigureAwait(false);
    }
}
