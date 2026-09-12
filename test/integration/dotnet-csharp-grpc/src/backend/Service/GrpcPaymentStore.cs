using ReactiveLock.Integration.Shared;
using Replication.Grpc;

public sealed class GrpcPaymentStore(
    PaymentReplicationClientManager replicationManager,
    PaymentReplicationService replicationService) : IPaymentStore
{
    public async Task<PaymentBatchWriteResult> InsertBatchAsync(
        IReadOnlyList<PaymentInsertParameters> payments,
        CancellationToken cancellationToken = default)
    {
        var rpcPayments = payments.Select(ToRpcPayment).ToArray();
        try
        {
            await replicationManager.PublishPaymentsBatchAsync(rpcPayments, replicationService)
                .ConfigureAwait(false);
            return new PaymentBatchWriteResult(payments.Count);
        }
        catch (Exception exception)
        {
            // The replication manager records locally before contacting the peer,
            // matching the original integration host's behavior.
            return new PaymentBatchWriteResult(payments.Count, exception);
        }
    }

    public Task<IReadOnlyList<PaymentInsertParameters>> GetPaymentsAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        var payments = replicationService.GetReplicatedPaymentsSnapshot()
            .Select(ToPayment)
            .Where(payment =>
                (!from.HasValue || payment.RequestedAt >= from) &&
                (!to.HasValue || payment.RequestedAt <= to))
            .ToArray();
        return Task.FromResult<IReadOnlyList<PaymentInsertParameters>>(payments);
    }

    public Task PurgePaymentsAsync(CancellationToken cancellationToken = default) =>
        replicationManager.ClearPaymentsAsync(replicationService);

    private static PaymentInsertRpcParameters ToRpcPayment(PaymentInsertParameters payment) => new()
    {
        CorrelationId = payment.CorrelationId.ToString(),
        Processor = payment.Processor,
        Amount = (double)payment.Amount,
        RequestedAt = payment.RequestedAt.ToString("o")
    };

    private static PaymentInsertParameters ToPayment(PaymentInsertRpcParameters payment) => new(
        Guid.Parse(payment.CorrelationId),
        payment.Processor,
        (decimal)payment.Amount,
        DateTimeOffset.Parse(payment.RequestedAt));
}
