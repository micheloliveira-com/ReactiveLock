using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;

public class PaymentService
{
    private RabbitMqIntegrationStore RabbitMq { get; }
    private ConsoleWriterService ConsoleWriterService { get; }
    private PaymentBatchInserterService BatchInserter { get; }
    private IReactiveLockTrackerState ReactiveLockTrackerState { get; }
    private PaymentProcessorService PaymentProcessorService { get; }


    public PaymentService(
        ConsoleWriterService consoleWriterService,
        RabbitMqIntegrationStore rabbitMq,
        PaymentBatchInserterService batchInserter,
        IReactiveLockTrackerFactory reactiveLockTrackerFactory,
        PaymentProcessorService paymentProcessorService
    )
    {
        ConsoleWriterService = consoleWriterService;
        RabbitMq = rabbitMq;
        BatchInserter = batchInserter;
        ReactiveLockTrackerState = reactiveLockTrackerFactory.GetTrackerState(Constant.REACTIVELOCK_API_PAYMENTS_SUMMARY_NAME);
        PaymentProcessorService = paymentProcessorService;
    }


    public async Task<IResult> EnqueuePaymentAsync(HttpContext context)
    {
        using var ms = new MemoryStream();
        await context.Request.Body.CopyToAsync(ms);
        var rawBody = ms.ToArray();

        await RabbitMq.EnqueueWorkAsync(rawBody).ConfigureAwait(false);

        return Results.Accepted();
    }


    public async Task<IResult> PurgePaymentsAsync()
    {
        await RabbitMq.PurgePaymentsAsync().ConfigureAwait(false);
        return Results.Ok("Payments removed from RabbitMQ-backed storage.");
    }

    private bool TryParseRequest(string message, [NotNullWhen(true)] out PaymentRequest? request)
    {
        request = null;
        var isValid = false;

        try
        {
            var parsed = JsonSerializer.Deserialize(message, JsonContext.Default.PaymentRequest);

            if (parsed != null &&
                parsed.Amount > 0 &&
                parsed.CorrelationId != Guid.Empty)
            {
                request = parsed;
                isValid = true;
            }
        }
        catch (Exception ex)
        {
            ConsoleWriterService.WriteLine($"Failed to deserialize or validate message: {ex.Message}");
        }

        return isValid;
    }

    public async Task ProcessPaymentAsync(string message)
    {
        if (!TryParseRequest(message, out var request))
        {
            return;
        }
        await ReactiveLockTrackerState.WaitIfBlockedAsync().ConfigureAwait(false);
        
        (HttpResponseMessage response, string processor, DateTimeOffset requestedAt) = await PaymentProcessorService.ProcessPaymentAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var parameters = new PaymentInsertParameters(
                CorrelationId: request.CorrelationId,
                Processor: processor,
                Amount: request.Amount,
                RequestedAt: requestedAt
            );
            await BatchInserter.AddAsync(parameters).ConfigureAwait(false);
            return;
        }
        var statusCode = (int)response.StatusCode;

        if (statusCode >= 400 && statusCode < 500)
        {
            ConsoleWriterService.WriteLine($"Discarding message due to client error: {statusCode} {response.ReasonPhrase}");
            return;
        }
        await RabbitMq.EnqueueWorkAsync(Encoding.UTF8.GetBytes(message)).ConfigureAwait(false);
    }   
}
