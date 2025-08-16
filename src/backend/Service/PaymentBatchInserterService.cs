using System.Collections.Concurrent;
using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;
using Microsoft.Extensions.Options;
using Replication.Grpc;

public class PaymentBatchInserterService
{
    private ConcurrentQueue<PaymentInsertRpcParameters> Buffer { get; } = new();
    private IReactiveLockTrackerController ReactiveLockTrackerController { get; }
    public DefaultOptions Options { get; }
    public PaymentReplicationService PaymentReplicationService { get; }

    private readonly PaymentReplicationClientManager ReplicationManager;

    public PaymentBatchInserterService(
        IReactiveLockTrackerFactory reactiveLockTrackerFactory,
        IOptions<DefaultOptions> options,
        PaymentReplicationClientManager replicationManager,
        PaymentReplicationService paymentReplicationService)
    {
        ReactiveLockTrackerController = reactiveLockTrackerFactory.GetTrackerController(Constant.REACTIVELOCK_GRPC_NAME);
        Options = options.Value;
        ReplicationManager = replicationManager;
        PaymentReplicationService = paymentReplicationService;
    }

    public async Task<int> AddAsync(PaymentInsertRpcParameters payment)
    {
        await ReactiveLockTrackerController.IncrementAsync().ConfigureAwait(false);
        Buffer.Enqueue(payment);

        if (Buffer.Count >= Options.BATCH_SIZE)
        {
            return await FlushBatchAsync().ConfigureAwait(false);
        }
        return 0;
    }

    public async Task<int> FlushBatchAsync()
    {
        if (Buffer.IsEmpty)
            return 0;

        int totalInserted = 0;

        while (!Buffer.IsEmpty)
        {
            var batch = new List<PaymentInsertRpcParameters>(Options.BATCH_SIZE);
            while (batch.Count < Options.BATCH_SIZE && Buffer.TryDequeue(out var item))
                batch.Add(item);

            if (batch.Count == 0)
                break;

            await ReplicationManager.PublishPaymentsBatchAsync(batch, PaymentReplicationService);

            totalInserted += batch.Count;

            await ReactiveLockTrackerController.DecrementAsync(batch.Count).ConfigureAwait(false);
        }

        return totalInserted;
    }
}
