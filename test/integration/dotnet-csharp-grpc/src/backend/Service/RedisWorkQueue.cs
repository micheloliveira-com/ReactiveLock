using ReactiveLock.Integration.Shared;
using StackExchange.Redis;

public sealed class RedisWorkQueue(IConnectionMultiplexer redis) : IWorkQueue
{
    private const string QueueKey = "task-queue";
    private readonly IDatabase _database = redis.GetDatabase();

    public Task EnqueueIncomingAsync(string message, CancellationToken cancellationToken = default)
    {
        _ = Task.Run(async () =>
            await _database.ListRightPushAsync(QueueKey, message).ConfigureAwait(false));
        return Task.CompletedTask;
    }

    public async Task RequeueAsync(string message, CancellationToken cancellationToken = default) =>
        await _database.ListRightPushAsync(QueueKey, message).ConfigureAwait(false);

    public async Task<string?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        var value = await _database.ListLeftPopAsync(QueueKey).ConfigureAwait(false);
        return value.HasValue ? value.ToString() : null;
    }
}
