using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;
using StackExchange.Redis;

public class PaymentBatchInserterService
{
    private ConcurrentQueue<PaymentInsertParameters> Buffer { get; } = new();

    private IDatabase RedisDb { get; }
    private IReactiveLockTrackerController ReactiveLockTrackerController { get; }

    public PaymentBatchInserterService(IConnectionMultiplexer redis,
    IReactiveLockTrackerFactory reactiveLockTrackerFactory)
    {
        RedisDb = redis.GetDatabase();
        ReactiveLockTrackerController = reactiveLockTrackerFactory.GetTrackerController(Constant.REACTIVELOCK_REDIS_NAME);
    }

    public async Task<int> AddAsync(PaymentInsertParameters payment)
    {
        await ReactiveLockTrackerController.IncrementAsync().ConfigureAwait(false);
        Buffer.Enqueue(payment);

        if (Buffer.Count >= Constant.REDIS_BATCH_SIZE)
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
            var batch = new List<PaymentInsertParameters>(Constant.REDIS_BATCH_SIZE);
            while (batch.Count < Constant.REDIS_BATCH_SIZE && Buffer.TryDequeue(out var item))
                batch.Add(item);

            if (batch.Count == 0)
                break;

            var tasks = new List<Task>();

            foreach (var payment in batch)
            {
                string json = JsonSerializer.Serialize(payment, JsonContext.Default.PaymentInsertParameters);
                var task = RedisDb.ListRightPushAsync(Constant.REDIS_PAYMENTS_BATCH_KEY, json);
                tasks.Add(task);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            totalInserted += batch.Count;

            await ReactiveLockTrackerController.DecrementAsync(batch.Count).ConfigureAwait(false);
        }

        return totalInserted;
    }

}
