using System.Data;
using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;
using System.Text.Json;

public class PaymentSummaryService
{
    private IReactiveLockTrackerFactory LockFactory { get; }
    private PaymentBatchInserterService BatchInserter { get; }
    private ConsoleWriterService ConsoleWriterService { get; }
    private PaymentReplicationService PaymentReplicationService { get; }
    private RunningPaymentsSummaryData RunningPaymentsSummaryData { get; }

    public PaymentSummaryService(
        IReactiveLockTrackerFactory lockFactory,
        PaymentBatchInserterService batchInserter,
        ConsoleWriterService consoleWriterService,
        PaymentReplicationService paymentReplicationService,
        RunningPaymentsSummaryData runningPaymentsSummaryData)
    {
        LockFactory = lockFactory;
        BatchInserter = batchInserter;
        ConsoleWriterService = consoleWriterService;
        PaymentReplicationService = paymentReplicationService;
        RunningPaymentsSummaryData = runningPaymentsSummaryData;
    }

    public async Task<IResult> GetPaymentsSummaryAsync(DateTimeOffset? from, DateTimeOffset? to)
    {
        var paymentsLock = LockFactory.GetTrackerController(Constant.REACTIVELOCK_API_PAYMENTS_SUMMARY_NAME);

        var lockData = $"{(from.HasValue ? from.Value.UtcDateTime.ToString("o") : "")};" +
                    $"{(to.HasValue ? to.Value.UtcDateTime.ToString("o") : "")}";

        await paymentsLock.IncrementAsync(lockData).ConfigureAwait(false);

        var grpcChannelBlockingGate = LockFactory.GetTrackerState(Constant.REACTIVELOCK_GRPC_NAME);
        var channelBlockingGate = LockFactory.GetTrackerState(Constant.REACTIVELOCK_HTTP_NAME);

        try
        {
            await WaitWithTimeoutAsync(async () =>
            {
                await grpcChannelBlockingGate.WaitIfBlockedAsync().ConfigureAwait(false);
                await channelBlockingGate.WaitIfBlockedAsync().ConfigureAwait(false);
            }, timeout: TimeSpan.FromSeconds(20)).ConfigureAwait(false);

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
        ConsoleWriterService.WriteLine("[Grpc] Gate blocked.");
        var state = LockFactory.GetTrackerState(Constant.REACTIVELOCK_API_PAYMENTS_SUMMARY_NAME);

        var lockEntries = await state.GetLockDataEntriesIfBlockedAsync();
        if (lockEntries.Length > 0)
        {
            var parsedRanges = lockEntries
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(entry =>
            {
                var parts = entry.Split(';', 2);

                DateTimeOffset? ParsePart(string? s) =>
                    string.IsNullOrWhiteSpace(s) ? null :
                    DateTimeOffset.TryParse(s, out var dt) ? dt : null;

                return (from: ParsePart(parts.ElementAtOrDefault(0)), to: ParsePart(parts.ElementAtOrDefault(1)));
            });

            foreach (var range in parsedRanges)
            {
                RunningPaymentsSummaryData.CurrentRanges.Add(range);
            }
        }

        await state.WaitIfBlockedAsync(
            whileBlockedLoopDelay: TimeSpan.FromMilliseconds(10),
            whileBlockedAsync: async () =>
            {
                try
                {
                    var processedCount = await BatchInserter.FlushBatchAsync().ConfigureAwait(false);
                    if (processedCount > 0)
                    {
                        ConsoleWriterService.WriteLine($"[Grpc] Processed batch with {processedCount} records.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Grpc][Error] Exception while processing message: {ex}");
                }
            }).ConfigureAwait(false);

        RunningPaymentsSummaryData.CurrentRanges.Clear();
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
