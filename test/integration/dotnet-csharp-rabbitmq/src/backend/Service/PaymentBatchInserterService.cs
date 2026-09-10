using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Threading.Tasks;
using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;
using Microsoft.Extensions.Options;

public class PaymentBatchInserterService
{
    private ConcurrentQueue<PaymentInsertParameters> Buffer { get; } = new();

    private RabbitMqIntegrationStore RabbitMq { get; }
    private IReactiveLockTrackerController ReactiveLockTrackerController { get; }
    private IReactiveLockTrackerState ReactiveLockTrackerState { get; }
    public DefaultOptions Options { get; }

    public PaymentBatchInserterService(RabbitMqIntegrationStore rabbitMq,
    IReactiveLockTrackerFactory reactiveLockTrackerFactory,
    IOptions<DefaultOptions> options)
    {
        RabbitMq = rabbitMq;
        ReactiveLockTrackerController = reactiveLockTrackerFactory.GetTrackerController(Constant.REACTIVELOCK_RABBITMQ_NAME);
        ReactiveLockTrackerState = reactiveLockTrackerFactory.GetTrackerState(Constant.REACTIVELOCK_RABBITMQ_NAME);
        Options = options.Value;
    }

    public async Task<int> AddAsync(PaymentInsertParameters payment)
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
            var batch = new List<PaymentInsertParameters>(Options.BATCH_SIZE);
            while (batch.Count < Options.BATCH_SIZE && Buffer.TryDequeue(out var item))
                batch.Add(item);

            if (batch.Count == 0)
                break;

            foreach (var payment in batch)
            {
                await RabbitMq.PublishPaymentAsync(payment);
            }

            totalInserted += batch.Count;

            await ReactiveLockTrackerController.DecrementAsync(batch.Count).ConfigureAwait(false);
        }

        return totalInserted;
    }

}
