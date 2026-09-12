namespace ReactiveLock.Integration.Shared;

public interface IWorkQueue
{
    Task EnqueueIncomingAsync(string message, CancellationToken cancellationToken = default);
    Task RequeueAsync(string message, CancellationToken cancellationToken = default);
    Task<string?> DequeueAsync(CancellationToken cancellationToken = default);
}

public interface IPaymentStore
{
    /// <summary>
    /// Persists payments in order and returns how many leading items were persisted.
    /// Implementations should return a partial count when a later write fails.
    /// </summary>
    Task<PaymentBatchWriteResult> InsertBatchAsync(
        IReadOnlyList<PaymentInsertParameters> payments,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentInsertParameters>> GetPaymentsAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default);

    Task PurgePaymentsAsync(CancellationToken cancellationToken = default);
}

public sealed record PaymentBatchWriteResult(int PersistedCount, Exception? Error = null);

public sealed record IntegrationBackendOptions(string ChannelLockName, string DisplayName);
