using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

public class PaymentBatchInserterService
{
    private ConcurrentQueue<PaymentInsertParameters> Buffer { get; } = new();

    private IDatabase RedisDb { get; }
    private IReactiveLockTrackerController ReactiveLockTrackerController { get; }
    private IReactiveLockTrackerState ReactiveLockTrackerState { get; }
    public DefaultOptions Options { get; }

    public PaymentBatchInserterService(IConnectionMultiplexer redis,
    IReactiveLockTrackerFactory reactiveLockTrackerFactory,
    IOptions<DefaultOptions> options)
    {
        RedisDb = redis.GetDatabase();
        ReactiveLockTrackerController = reactiveLockTrackerFactory.GetTrackerController(Constant.REACTIVELOCK_REDIS_NAME);
        ReactiveLockTrackerState = reactiveLockTrackerFactory.GetTrackerState(Constant.REACTIVELOCK_REDIS_NAME);
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
                string json = JsonSerializer.Serialize(payment, JsonContext.Default.PaymentInsertParameters);
                await RedisDb.ListRightPushAsync(Constant.REDIS_PAYMENTS_BATCH_KEY, json);
            }

            totalInserted += batch.Count;

            await ReactiveLockTrackerController.DecrementAsync(batch.Count).ConfigureAwait(false);
        }

        return totalInserted;
    }

}
