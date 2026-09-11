using System.Text.Json;
using ReactiveLock.Integration.Shared;
using StackExchange.Redis;

public sealed class RedisIntegrationBackend(IConnectionMultiplexer redis) : IWorkQueue, IPaymentStore
{
    private const string QueueKey = "task-queue";
    private const string PaymentsKey = "payments:batch";
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

    public async Task<PaymentBatchWriteResult> InsertBatchAsync(
        IReadOnlyList<PaymentInsertParameters> payments,
        CancellationToken cancellationToken = default)
    {
        var persisted = 0;
        try
        {
            foreach (var payment in payments)
            {
                var json = JsonSerializer.Serialize(payment, IntegrationJsonContext.Default.PaymentInsertParameters);
                await _database.ListRightPushAsync(PaymentsKey, json).ConfigureAwait(false);
                persisted++;
            }
            return new PaymentBatchWriteResult(persisted);
        }
        catch (Exception exception)
        {
            return new PaymentBatchWriteResult(persisted, exception);
        }
    }

    public async Task<IReadOnlyList<PaymentInsertParameters>> GetPaymentsAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        var values = await _database.ListRangeAsync(PaymentsKey).ConfigureAwait(false);
        return values
            .Select(value => JsonSerializer.Deserialize((byte[])value!, IntegrationJsonContext.Default.PaymentInsertParameters))
            .Where(payment => payment is not null)
            .Where(payment =>
                (!from.HasValue || payment!.RequestedAt >= from) &&
                (!to.HasValue || payment!.RequestedAt <= to))
            .Cast<PaymentInsertParameters>()
            .ToArray();
    }

    public async Task PurgePaymentsAsync(CancellationToken cancellationToken = default) =>
        await _database.KeyDeleteAsync(PaymentsKey).ConfigureAwait(false);
}
