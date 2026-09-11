using System.Collections.Concurrent;
using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ReactiveLock.Integration.Shared;

public sealed class PaymentBatchInserterService
{
    private readonly ConcurrentQueue<PaymentInsertParameters> _buffer = new();
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly IPaymentStore _paymentStore;
    private readonly IReactiveLockTrackerController _trackerController;
    private readonly DefaultOptions _options;

    public PaymentBatchInserterService(
        IPaymentStore paymentStore,
        IReactiveLockTrackerFactory trackerFactory,
        IOptions<DefaultOptions> options,
        IntegrationBackendOptions backendOptions)
    {
        _paymentStore = paymentStore;
        _trackerController = trackerFactory.GetTrackerController(backendOptions.ChannelLockName);
        _options = options.Value;
    }

    public async Task<int> AddAsync(PaymentInsertParameters payment)
    {
        await _trackerController.IncrementAsync().ConfigureAwait(false);
        _buffer.Enqueue(payment);

        return _buffer.Count >= _options.BATCH_SIZE
            ? await FlushBatchAsync().ConfigureAwait(false)
            : 0;
    }

    public async Task<int> FlushBatchAsync()
    {
        await _flushGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var totalInserted = 0;
            while (!_buffer.IsEmpty)
            {
                var batch = new List<PaymentInsertParameters>(_options.BATCH_SIZE);
                while (batch.Count < _options.BATCH_SIZE && _buffer.TryDequeue(out var item))
                    batch.Add(item);

                if (batch.Count == 0)
                    break;

                PaymentBatchWriteResult result;
                try
                {
                    result = await _paymentStore.InsertBatchAsync(batch).ConfigureAwait(false);
                }
                catch
                {
                    Requeue(batch, 0);
                    throw;
                }

                if (result.PersistedCount < 0 || result.PersistedCount > batch.Count)
                    throw new InvalidOperationException("The payment store returned an invalid persisted count.");

                if (result.PersistedCount > 0)
                {
                    totalInserted += result.PersistedCount;
                    await _trackerController.DecrementAsync(result.PersistedCount).ConfigureAwait(false);
                }

                Requeue(batch, result.PersistedCount);
                if (result.Error is not null)
                    throw new InvalidOperationException("The payment store could not persist the complete batch.", result.Error);
            }

            return totalInserted;
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private void Requeue(IReadOnlyList<PaymentInsertParameters> batch, int firstUnpersisted)
    {
        for (var index = firstUnpersisted; index < batch.Count; index++)
            _buffer.Enqueue(batch[index]);
    }
}
