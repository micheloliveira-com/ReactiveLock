using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;

namespace ReactiveLock.Integration.Shared;

public sealed class PaymentSummaryService
{
    private readonly IReactiveLockTrackerFactory _lockFactory;
    private readonly IPaymentStore _paymentStore;
    private readonly PaymentBatchInserterService _batchInserter;
    private readonly ConsoleWriterService _console;
    private readonly RunningPaymentsSummaryData _summaryData;
    private readonly IntegrationBackendOptions _backendOptions;

    public PaymentSummaryService(
        IReactiveLockTrackerFactory lockFactory,
        IPaymentStore paymentStore,
        PaymentBatchInserterService batchInserter,
        ConsoleWriterService console,
        RunningPaymentsSummaryData summaryData,
        IntegrationBackendOptions backendOptions)
    {
        _lockFactory = lockFactory;
        _paymentStore = paymentStore;
        _batchInserter = batchInserter;
        _console = console;
        _summaryData = summaryData;
        _backendOptions = backendOptions;
    }

    public async Task<IResult> GetPaymentsSummaryAsync(DateTimeOffset? from, DateTimeOffset? to)
    {
        var paymentsLock = _lockFactory.GetTrackerController(Constant.REACTIVELOCK_API_PAYMENTS_SUMMARY_NAME);
        var lockData = $"{(from.HasValue ? from.Value.UtcDateTime.ToString("o") : "")};" +
                       $"{(to.HasValue ? to.Value.UtcDateTime.ToString("o") : "")}";
        await paymentsLock.IncrementAsync(lockData).ConfigureAwait(false);

        var persistenceGate = _lockFactory.GetTrackerState(_backendOptions.ChannelLockName);
        var httpGate = _lockFactory.GetTrackerState(Constant.REACTIVELOCK_HTTP_NAME);

        try
        {
            await WaitWithTimeoutAsync(async () =>
            {
                await persistenceGate.WaitIfBlockedAsync().ConfigureAwait(false);
                await httpGate.WaitIfBlockedAsync().ConfigureAwait(false);
            }, TimeSpan.FromSeconds(40)).ConfigureAwait(false);

            var payments = await _paymentStore.GetPaymentsAsync(from, to).ConfigureAwait(false);
            var grouped = payments
                .GroupBy(payment => payment.Processor)
                .ToDictionary(
                    group => group.Key,
                    group => new PaymentSummaryResult(
                        group.Key,
                        group.LongCount(),
                        group.Sum(payment => payment.Amount)));

            var defaultResult = grouped.GetValueOrDefault(
                Constant.DEFAULT_PROCESSOR_NAME,
                new PaymentSummaryResult(Constant.DEFAULT_PROCESSOR_NAME, 0, 0));
            var fallbackResult = grouped.GetValueOrDefault(
                Constant.FALLBACK_PROCESSOR_NAME,
                new PaymentSummaryResult(Constant.FALLBACK_PROCESSOR_NAME, 0, 0));

            return Results.Ok(new PaymentSummaryResponse(
                new PaymentSummary(defaultResult.TotalRequests, defaultResult.TotalAmount),
                new PaymentSummary(fallbackResult.TotalRequests, fallbackResult.TotalAmount)));
        }
        finally
        {
            await paymentsLock.DecrementAsync().ConfigureAwait(false);
        }
    }

    public async Task FlushWhileGateBlockedAsync()
    {
        _console.WriteLine($"[{_backendOptions.DisplayName}] Gate blocked.");
        var state = _lockFactory.GetTrackerState(Constant.REACTIVELOCK_API_PAYMENTS_SUMMARY_NAME);
        var lockEntries = await state.GetLockDataEntriesIfBlockedAsync().ConfigureAwait(false);
        foreach (var range in ParseRanges(lockEntries))
            _summaryData.CurrentRanges.Add(range);

        await state.WaitIfBlockedAsync(
            whileBlockedLoopDelay: TimeSpan.FromMilliseconds(10),
            whileBlockedAsync: async () =>
            {
                try
                {
                    var count = await _batchInserter.FlushBatchAsync().ConfigureAwait(false);
                    if (count > 0)
                        _console.WriteLine($"[{_backendOptions.DisplayName}] Processed batch with {count} records.");
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"[{_backendOptions.DisplayName}][Error] Exception while processing message: {exception}");
                }
            }).ConfigureAwait(false);

        _summaryData.CurrentRanges.Clear();
    }

    private static IEnumerable<(DateTimeOffset? from, DateTimeOffset? to)> ParseRanges(IEnumerable<string> entries)
    {
        foreach (var entry in entries.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var parts = entry.Split(';', 2);
            yield return (ParsePart(parts.ElementAtOrDefault(0)), ParsePart(parts.ElementAtOrDefault(1)));
        }

        static DateTimeOffset? ParsePart(string? value) =>
            string.IsNullOrWhiteSpace(value) || !DateTimeOffset.TryParse(value, out var parsed)
                ? null
                : parsed;
    }

    private async Task<bool> WaitWithTimeoutAsync(Func<Task> taskFactory, TimeSpan timeout)
    {
        var task = taskFactory();
        if (await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false) != task)
        {
            _console.WriteLine($"[Timeout] Task did not complete within {timeout.TotalMilliseconds}ms.");
            return false;
        }

        await task.ConfigureAwait(false);
        return true;
    }
}
