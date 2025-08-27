using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;

public class CountingHandler : DelegatingHandler
{
    private IReactiveLockTrackerController ReactiveLockTrackerController { get; set; }
    private RunningPaymentsSummaryData RunningPaymentsSummaryData { get; }

    public CountingHandler(IReactiveLockTrackerFactory reactiveLockTrackerFactory,
    RunningPaymentsSummaryData runningPaymentsSummaryData)
    {
        ReactiveLockTrackerController = reactiveLockTrackerFactory.GetTrackerController(Constant.REACTIVELOCK_HTTP_NAME);
        RunningPaymentsSummaryData = runningPaymentsSummaryData;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var shouldIncrement = true;
        if (request.Options.TryGetValue(new HttpRequestOptionsKey<DateTimeOffset>("RequestedAt"), out var requestedAt))
        {
            var currentRunningRanges = RunningPaymentsSummaryData.CurrentRanges.ToList();
            if (currentRunningRanges.Any())
            {
                bool requestIsNotInsideAnySummaryRange = !currentRunningRanges.Any(range =>
                    (!range.from.HasValue || requestedAt >= range.from.Value) &&
                    (!range.to.HasValue || requestedAt <= range.to.Value)
                );

                if (requestIsNotInsideAnySummaryRange)
                {
                    shouldIncrement = false;
                }
            }
        }
        if (shouldIncrement)
            await ReactiveLockTrackerController.IncrementAsync().ConfigureAwait(false);
        
        try
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (shouldIncrement)
                await ReactiveLockTrackerController.DecrementAsync().ConfigureAwait(false);
        }
    }
}