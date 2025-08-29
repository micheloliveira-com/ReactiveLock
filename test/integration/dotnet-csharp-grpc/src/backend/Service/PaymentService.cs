using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;
using StackExchange.Redis;

public class PaymentService
{
    private ConsoleWriterService ConsoleWriterService { get; }
    private PaymentBatchInserterService BatchInserter { get; }
    private IDatabase RedisDb { get; }
    private IReactiveLockTrackerState ReactiveLockTrackerState { get; }
    private PaymentProcessorService PaymentProcessorService { get; }
    private PaymentReplicationClientManager PaymentReplicationClientManager { get; }
    private PaymentReplicationService PaymentReplicationService { get; }
    public PaymentService(
        ConsoleWriterService consoleWriterService,
        PaymentBatchInserterService batchInserter,
        IReactiveLockTrackerFactory reactiveLockTrackerFactory,
        IConnectionMultiplexer redis,
        PaymentProcessorService paymentProcessorService,
        PaymentReplicationClientManager paymentReplicationClientManager,
        PaymentReplicationService paymentReplicationService
    )
    {
        ConsoleWriterService = consoleWriterService;
        BatchInserter = batchInserter;
        RedisDb = redis.GetDatabase();
        ReactiveLockTrackerState = reactiveLockTrackerFactory.GetTrackerState(Constant.REACTIVELOCK_API_PAYMENTS_SUMMARY_NAME);
        PaymentProcessorService = paymentProcessorService;
        PaymentReplicationClientManager = paymentReplicationClientManager;
        PaymentReplicationService = paymentReplicationService;
    }


    public async Task<IResult> EnqueuePaymentAsync(HttpContext context)
    {
        using var ms = new MemoryStream();
        await context.Request.Body.CopyToAsync(ms);
        var rawBody = ms.ToArray();
        var rawString = System.Text.Encoding.UTF8.GetString(rawBody);

        _ = Task.Run(async () =>
        {
            await RedisDb.ListRightPushAsync(Constant.REDIS_QUEUE_KEY, rawBody).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return Results.Accepted();
    }

    public async Task<IResult> PurgePaymentsAsync()
    {
        BatchInserter.ClearLocalPayments();
        await PaymentReplicationClientManager.ClearPaymentsAsync(PaymentReplicationService);
        return Results.Ok("Payments removed from Grpc.");
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
            var parameters = new Replication.Grpc.PaymentInsertRpcParameters()
            {
                CorrelationId = request.CorrelationId.ToString(),
                Processor = processor,
                Amount = (double)request.Amount,
                RequestedAt = requestedAt.ToString("o")
            };
            await BatchInserter.AddAsync(parameters).ConfigureAwait(false);
            return;
        }
        var statusCode = (int)response.StatusCode;

        if (statusCode >= 400 && statusCode < 500)
        {
            Console.WriteLine($"Discarding message due to client error: {statusCode} {response.ReasonPhrase}");
            return;
        }
        
        await RedisDb.ListRightPushAsync(Constant.REDIS_QUEUE_KEY, message).ConfigureAwait(false);
    }   
}