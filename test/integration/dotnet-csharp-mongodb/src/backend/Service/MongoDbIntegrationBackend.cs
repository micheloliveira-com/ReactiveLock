using ReactiveLock.Integration.Shared;

public sealed class MongoDbIntegrationBackend(MongoDbIntegrationStore store) : IWorkQueue, IPaymentStore
{
    public Task EnqueueIncomingAsync(string message, CancellationToken cancellationToken = default) =>
        store.EnqueueWorkAsync(message, cancellationToken);

    public Task RequeueAsync(string message, CancellationToken cancellationToken = default) =>
        store.EnqueueWorkAsync(message, cancellationToken);

    public Task<string?> DequeueAsync(CancellationToken cancellationToken = default) =>
        store.DequeueWorkAsync(cancellationToken);

    public async Task<PaymentBatchWriteResult> InsertBatchAsync(
        IReadOnlyList<PaymentInsertParameters> payments,
        CancellationToken cancellationToken = default)
    {
        var persisted = 0;
        try
        {
            foreach (var payment in payments)
            {
                await store.InsertPaymentAsync(payment, cancellationToken).ConfigureAwait(false);
                persisted++;
            }
            return new PaymentBatchWriteResult(persisted);
        }
        catch (Exception exception)
        {
            return new PaymentBatchWriteResult(persisted, exception);
        }
    }

    public Task<IReadOnlyList<PaymentInsertParameters>> GetPaymentsAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default) =>
        store.GetPaymentsAsync(from, to, cancellationToken);

    public Task PurgePaymentsAsync(CancellationToken cancellationToken = default) =>
        store.PurgePaymentsAsync(cancellationToken);
}
