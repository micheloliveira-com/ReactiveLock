using System.Text;
using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;

namespace ReactiveLock.Integration.Shared;

public sealed class PaymentProcessorService
{
    private readonly IReactiveLockTrackerController _trackerController;
    private readonly IReactiveLockTrackerState _trackerState;
    private readonly HttpClient _httpDefault;
    private readonly HttpClient _httpFallback;
    private readonly ConsoleWriterService _console;
    private readonly SemaphoreSlim _lastIncrementLock = new(1, 1);
    private DateTimeOffset _lastIncrement = DateTimeOffset.MinValue;

    public PaymentProcessorService(
        IReactiveLockTrackerFactory trackerFactory,
        IHttpClientFactory httpClientFactory,
        ConsoleWriterService console)
    {
        _trackerController = trackerFactory.GetTrackerController(Constant.DEFAULT_PROCESSOR_ERROR_THRESHOLD_NAME);
        _trackerState = trackerFactory.GetTrackerState(Constant.DEFAULT_PROCESSOR_ERROR_THRESHOLD_NAME);
        _httpDefault = httpClientFactory.CreateClient(Constant.DEFAULT_PROCESSOR_NAME);
        _httpFallback = httpClientFactory.CreateClient(Constant.FALLBACK_PROCESSOR_NAME);
        _console = console;
    }

    public async Task<(HttpResponseMessage response, string processor, DateTimeOffset requestedAt)>
        ProcessPaymentAsync(PaymentRequest request)
    {
        var requestedAt = DateTimeOffset.UtcNow;
        var processor = Constant.DEFAULT_PROCESSOR_NAME;
        var json = $$"""
            {
              "amount": {{request.Amount}},
              "requestedAt": "{{requestedAt:o}}",
              "correlationId": "{{request.CorrelationId}}"
            }
            """;

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/payments")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        httpRequest.Options.Set(new HttpRequestOptionsKey<DateTimeOffset>("RequestedAt"), requestedAt);

        var requestClient = _httpDefault;
        if (await _trackerState.IsBlockedAsync().ConfigureAwait(false) &&
            _trackerController.GetActualCount() % 2 == 0)
        {
            requestClient = _httpFallback;
        }

        var response = await requestClient.SendAsync(httpRequest).ConfigureAwait(false);
        if (requestClient == _httpDefault)
        {
            var actualCount = _trackerController.GetActualCount();
            if (response.IsSuccessStatusCode && actualCount > 0)
            {
                _console.WriteLine("Lock decremented.");
                await _trackerController.DecrementAsync().ConfigureAwait(false);
            }
            else if (!response.IsSuccessStatusCode)
            {
                await IncrementLockIfPossibleAsync(actualCount).ConfigureAwait(false);
            }
        }
        else
        {
            var actualCount = _trackerController.GetActualCount();
            await IncrementLockIfPossibleAsync(actualCount).ConfigureAwait(false);
            processor = Constant.FALLBACK_PROCESSOR_NAME;
            _console.WriteLine("Fallback is called.");
        }

        return (response, processor, requestedAt);
    }

    private async Task IncrementLockIfPossibleAsync(int actualCount)
    {
        var shouldIncrement = false;
        var now = DateTimeOffset.UtcNow;

        await _lastIncrementLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (now - _lastIncrement >= TimeSpan.FromSeconds(1))
            {
                _lastIncrement = now;
                shouldIncrement = true;
            }
        }
        finally
        {
            _lastIncrementLock.Release();
        }

        if (shouldIncrement)
        {
            _console.WriteLine($"Incremented from count {actualCount}");
            await _trackerController.IncrementAsync().ConfigureAwait(false);
        }
    }
}
