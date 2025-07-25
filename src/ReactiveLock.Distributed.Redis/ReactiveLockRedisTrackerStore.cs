namespace MichelOliveira.ReactiveLock.Distributed.Redis;

using MichelOliveira.ReactiveLock.Core;
using StackExchange.Redis;
using System.Threading.Tasks;

public class ReactiveLockRedisTrackerStore(IConnectionMultiplexer redis, string redisHashSetKey, string redisHashSetNotifierKey) : IReactiveLockTrackerStore
{
    private IDatabase RedisDb { get; } = redis.GetDatabase();
    private ISubscriber Subscriber { get; } = redis.GetSubscriber();

    public async Task SetStatusAsync(string hostname, bool isBusy)
    {
        var statusValue = isBusy ? "1" : "0";
        await RedisDb.HashSetAsync(redisHashSetKey, hostname, statusValue).ConfigureAwait(false);
        await Subscriber.PublishAsync(RedisChannel.Literal(redisHashSetNotifierKey), statusValue).ConfigureAwait(false);
    }
}
