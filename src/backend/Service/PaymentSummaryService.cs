using System.Data;
using Dapper;
using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;
using System.Text.Json;

public class PaymentSummaryService
{
    private IReactiveLockTrackerFactory LockFactory { get; }
    private PaymentBatchInserterService BatchInserter { get; }
    private ConsoleWriterService ConsoleWriterService { get; }
    private PaymentReplicationService PaymentReplicationService { get; }

    public PaymentSummaryService(
        IReactiveLockTrackerFactory lockFactory,
        PaymentBatchInserterService batchInserter,
        ConsoleWriterService consoleWriterService,
        PaymentReplicationService paymentReplicationService)
    {
        LockFactory = lockFactory;
        BatchInserter = batchInserter;
        ConsoleWriterService = consoleWriterService;
        PaymentReplicationService = paymentReplicationService;
    }

    public async Task<IResult> GetPaymentsSummaryAsync(DateTimeOffset? from, DateTimeOffset? to)
    {
        var paymentsLock = LockFactory.GetTrackerController(Constant.REACTIVELOCK_API_PAYMENTS_SUMMARY_NAME);
        await paymentsLock.IncrementAsync().ConfigureAwait(false);

        var redisChannelBlockingGate = LockFactory.GetTrackerState(Constant.REACTIVELOCK_REDIS_NAME);
        var channelBlockingGate = LockFactory.GetTrackerState(Constant.REACTIVELOCK_HTTP_NAME);

        try
        {
            await WaitWithTimeoutAsync(async () =>
            {
                await redisChannelBlockingGate.WaitIfBlockedAsync().ConfigureAwait(false);
                await channelBlockingGate.WaitIfBlockedAsync().ConfigureAwait(false);
            }, timeout: TimeSpan.FromSeconds(1.3)).ConfigureAwait(false);

            // Get all replicated payments snapshot from your gRPC replication manager
            var allPayments = PaymentReplicationService.GetReplicatedPaymentsSnapshot();

            // Filter by date range
            var payments = allPayments
                .Where(p => {
                    var requestedAt = DateTimeOffset.Parse(p.RequestedAt); // assuming string ISO 8601 here
                    return (!from.HasValue || requestedAt >= from) &&
                        (!to.HasValue || requestedAt <= to);
                })
                .ToList();

            var grouped = payments
                .GroupBy(p => p.Processor)
                .ToDictionary(g => g.Key,
                            g => new PaymentSummaryResult(
                                Processor: g.Key,
                                TotalRequests: g.Count(),
                                TotalAmount: g.Sum(p => (decimal)p.Amount)));

            var defaultResult = grouped.TryGetValue(Constant.DEFAULT_PROCESSOR_NAME, out var d)
                ? d
                : new PaymentSummaryResult(Constant.DEFAULT_PROCESSOR_NAME, 0, 0);

            var fallbackResult = grouped.TryGetValue(Constant.FALLBACK_PROCESSOR_NAME, out var f)
                ? f
                : new PaymentSummaryResult(Constant.FALLBACK_PROCESSOR_NAME, 0, 0);

            var response = new PaymentSummaryResponse(
                new PaymentSummary(defaultResult.TotalRequests, defaultResult.TotalAmount),
                new PaymentSummary(fallbackResult.TotalRequests, fallbackResult.TotalAmount)
            );

            return Results.Ok(response);
        }
        finally
        {
            await paymentsLock.DecrementAsync().ConfigureAwait(false);
        }
    }



    public async Task FlushWhileGateBlockedAsync()
    {
        ConsoleWriterService.WriteLine("[Redis] Gate blocked.");
        var state = LockFactory.GetTrackerState(Constant.REACTIVELOCK_API_PAYMENTS_SUMMARY_NAME);

        await state.WaitIfBlockedAsync(
            whileBlockedLoopDelay: TimeSpan.FromMilliseconds(10),
            whileBlockedAsync: async () =>
            {
                try
                {
                    var processedCount = await BatchInserter.FlushBatchAsync().ConfigureAwait(false);
                    if (processedCount > 0)
                    {
                        ConsoleWriterService.WriteLine($"[Redis] Processed batch with {processedCount} records.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Redis][Error] Exception while processing message: {ex}");
                }
            }).ConfigureAwait(false);
    }

    private async Task<bool> WaitWithTimeoutAsync(Func<Task> taskFactory, TimeSpan timeout)
    {
        var task = taskFactory();
        var timeoutTask = Task.Delay(timeout);
        var completedTask = await Task.WhenAny(task, timeoutTask).ConfigureAwait(false);

        if (completedTask == timeoutTask)
        {
            ConsoleWriterService.WriteLine($"[Timeout] Task did not complete within {timeout.TotalMilliseconds}ms.");
            return false;
        }

        await task.ConfigureAwait(false);
        return true;
    }
}
