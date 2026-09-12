using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;

namespace ReactiveLock.Integration.Shared;

public sealed class PaymentService
{
    private readonly IWorkQueue _workQueue;
    private readonly IPaymentStore _paymentStore;
    private readonly ConsoleWriterService _console;
    private readonly PaymentBatchInserterService _batchInserter;
    private readonly IReactiveLockTrackerState _summaryTrackerState;
    private readonly PaymentProcessorService _processor;
    private readonly IntegrationBackendOptions _backendOptions;

    public PaymentService(
        IWorkQueue workQueue,
        IPaymentStore paymentStore,
        ConsoleWriterService console,
        PaymentBatchInserterService batchInserter,
        IReactiveLockTrackerFactory trackerFactory,
        PaymentProcessorService processor,
        IntegrationBackendOptions backendOptions)
    {
        _workQueue = workQueue;
        _paymentStore = paymentStore;
        _console = console;
        _batchInserter = batchInserter;
        _summaryTrackerState = trackerFactory.GetTrackerState(Constant.REACTIVELOCK_API_PAYMENTS_SUMMARY_NAME);
        _processor = processor;
        _backendOptions = backendOptions;
    }

    public async Task<IResult> EnqueuePaymentAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
        await _workQueue.EnqueueIncomingAsync(body, context.RequestAborted).ConfigureAwait(false);
        return Results.Accepted();
    }

    public async Task<IResult> PurgePaymentsAsync(CancellationToken cancellationToken = default)
    {
        await _paymentStore.PurgePaymentsAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok($"Payments removed from {_backendOptions.DisplayName}.");
    }

    public async Task ProcessPaymentAsync(string message)
    {
        if (!TryParseRequest(message, out var request))
            return;

        await _summaryTrackerState.WaitIfBlockedAsync().ConfigureAwait(false);
        var (response, processor, requestedAt) = await _processor.ProcessPaymentAsync(request).ConfigureAwait(false);
        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                await _batchInserter.AddAsync(new PaymentInsertParameters(
                    request.CorrelationId,
                    processor,
                    request.Amount,
                    requestedAt)).ConfigureAwait(false);
                return;
            }

            var statusCode = (int)response.StatusCode;
            if (statusCode is >= 400 and < 500)
            {
                _console.WriteLine($"Discarding message due to client error: {statusCode} {response.ReasonPhrase}");
                return;
            }
        }

        await _workQueue.RequeueAsync(message).ConfigureAwait(false);
    }

    private bool TryParseRequest(string message, [NotNullWhen(true)] out PaymentRequest? request)
    {
        request = null;
        try
        {
            var parsed = JsonSerializer.Deserialize(message, IntegrationJsonContext.Default.PaymentRequest);
            if (parsed is null || parsed.Amount <= 0 || parsed.CorrelationId == Guid.Empty)
                return false;

            request = parsed;
            return true;
        }
        catch (Exception exception)
        {
            _console.WriteLine($"Failed to deserialize or validate message: {exception.Message}");
            return false;
        }
    }
}
