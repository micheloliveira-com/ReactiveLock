using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;

namespace ReactiveLock.Integration.Shared;

public sealed class CountingHandler : DelegatingHandler
{
    private readonly IReactiveLockTrackerController _trackerController;
    private readonly RunningPaymentsSummaryData _summaryData;

    public CountingHandler(
        IReactiveLockTrackerFactory reactiveLockTrackerFactory,
        RunningPaymentsSummaryData summaryData)
    {
        _trackerController = reactiveLockTrackerFactory.GetTrackerController(Constant.REACTIVELOCK_HTTP_NAME);
        _summaryData = summaryData;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var shouldIncrement = true;
        if (request.Options.TryGetValue(
                new HttpRequestOptionsKey<DateTimeOffset>("RequestedAt"),
                out var requestedAt))
        {
            var ranges = _summaryData.CurrentRanges.ToList();
            if (ranges.Count > 0)
            {
                shouldIncrement = ranges.Any(range =>
                    (!range.from.HasValue || requestedAt >= range.from.Value) &&
                    (!range.to.HasValue || requestedAt <= range.to.Value));
            }
        }

        if (shouldIncrement)
            await _trackerController.IncrementAsync().ConfigureAwait(false);

        try
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (shouldIncrement)
                await _trackerController.DecrementAsync().ConfigureAwait(false);
        }
    }
}
