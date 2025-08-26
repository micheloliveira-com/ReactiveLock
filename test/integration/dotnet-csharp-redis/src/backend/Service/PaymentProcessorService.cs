using System.Text;
using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;
using Microsoft.Extensions.Options;

public class PaymentProcessorService
{
    private IReactiveLockTrackerController ReactiveLockTrackerController { get; }
    private IReactiveLockTrackerState ReactiveLockTrackerState { get; }
    private HttpClient HttpDefault { get; }
    private HttpClient HttpFallback { get; }
    private DefaultOptions Options { get; }
    private ConsoleWriterService ConsoleWriterService { get; }

    private SemaphoreSlim LastIncrementLockObject { get; } = new(1, 1);
    private DateTimeOffset LastIncrement { get; set; } = DateTimeOffset.MinValue;


    public PaymentProcessorService(
        IReactiveLockTrackerFactory reactiveLockTrackerFactory,
        IHttpClientFactory factory,
        IOptions<DefaultOptions> options,
        ConsoleWriterService consoleWriterService
    )
    {
        ReactiveLockTrackerController = reactiveLockTrackerFactory.GetTrackerController(Constant.DEFAULT_PROCESSOR_ERROR_THRESHOLD_NAME);
        ReactiveLockTrackerState = reactiveLockTrackerFactory.GetTrackerState(Constant.DEFAULT_PROCESSOR_ERROR_THRESHOLD_NAME);
        HttpDefault = factory.CreateClient(Constant.DEFAULT_PROCESSOR_NAME);
        HttpFallback = factory.CreateClient(Constant.FALLBACK_PROCESSOR_NAME);
        Options = options.Value;
        ConsoleWriterService = consoleWriterService;
    }
    public async Task<(HttpResponseMessage response, string processor)> ProcessPaymentAsync(PaymentRequest request, DateTimeOffset requestedAt)
    {
        string processor = Constant.DEFAULT_PROCESSOR_NAME;
        string jsonString = $@"{{
            ""amount"": {request.Amount},
            ""requestedAt"": ""{requestedAt:o}"",
            ""correlationId"": ""{request.CorrelationId}""
        }}";

        var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/payments")
        {
            Content = content
        };

        httpRequest.Options.Set(new HttpRequestOptionsKey<DateTimeOffset>("RequestedAt"), requestedAt);

        HttpClient requestClient = HttpDefault;
        if (await ReactiveLockTrackerState.IsBlockedAsync())
        {
            if (ReactiveLockTrackerController.GetActualCount() % 2 == 0)
            {
                requestClient = HttpFallback;
            }
        }
        var message = await requestClient.SendAsync(httpRequest).ConfigureAwait(false);

        if (requestClient == HttpDefault)
        {
            var actualCount = ReactiveLockTrackerController.GetActualCount();
            if (message.IsSuccessStatusCode && actualCount > 0)
            {
                ConsoleWriterService.WriteLine($"Lock decremented.");
                await ReactiveLockTrackerController.DecrementAsync().ConfigureAwait(false);
            }
            else if (!message.IsSuccessStatusCode)
            {
                await IncrementLockIfPossible(actualCount).ConfigureAwait(false);
            }
        }
        if (requestClient == HttpFallback)
        {
            var actualCount = ReactiveLockTrackerController.GetActualCount();
            await IncrementLockIfPossible(actualCount).ConfigureAwait(false);
            processor = Constant.FALLBACK_PROCESSOR_NAME;
            ConsoleWriterService.WriteLine($"Fallback is called.");
        }
        return (message, processor);
    }

    private async Task IncrementLockIfPossible(int actualCount)
    {
        bool shouldIncrement = false;
        var now = DateTimeOffset.UtcNow;

        await LastIncrementLockObject.WaitAsync().ConfigureAwait(false);
        try
        {
            if (now - LastIncrement >= TimeSpan.FromSeconds(1))
            {
                LastIncrement = now;
                shouldIncrement = true;
            }
        }
        finally
        {
            LastIncrementLockObject.Release();
        }

        if (shouldIncrement)
        {
            ConsoleWriterService.WriteLine($"Incremented from count {actualCount}");
            await ReactiveLockTrackerController.IncrementAsync().ConfigureAwait(false);
        }
    }
}