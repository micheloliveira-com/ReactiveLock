using MichelOliveira.Com.ReactiveLock.Distributed.RabbitMQ;
using Microsoft.AspNetCore.Mvc;
using Polly;
using System.Net;
using System.Text.Json;
using global::RabbitMQ.Client;

var builder = WebApplication.CreateSlimBuilder(args);

var warmupRetryPolicy = Policy
    .Handle<Exception>()
    .WaitAndRetry(
        retryCount: 60 * 10,
        sleepDurationProvider: _ => TimeSpan.FromSeconds(0.1),
        onRetry: (exception, _, retryCount, _) =>
            Console.WriteLine($"Retry {retryCount}: {exception.GetType().Name} - {exception.Message}"));

builder.Services.AddSingleton<global::RabbitMQ.Client.IConnection>(_ =>
{
    var factory = new ConnectionFactory
    {
        Uri = new Uri(builder.Configuration.GetConnectionString("rabbitmq")!),
        AutomaticRecoveryEnabled = true,
        TopologyRecoveryEnabled = true,
        ClientProvidedName = $"ReactiveLock-{Dns.GetHostName()}"
    };

    return warmupRetryPolicy.Execute(() =>
        factory.CreateConnectionAsync().GetAwaiter().GetResult());
});

builder.Services
    .AddOptions<DefaultOptions>()
    .Bind(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, JsonContext.Default);
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.AddHttpClient(Constant.DEFAULT_PROCESSOR_NAME, client =>
    client.BaseAddress = new Uri(builder.Configuration.GetConnectionString(Constant.DEFAULT_PROCESSOR_NAME)!))
    .AddHttpMessageHandler<CountingHandler>();

builder.Services.AddHttpClient(Constant.FALLBACK_PROCESSOR_NAME, client =>
    client.BaseAddress = new Uri(builder.Configuration.GetConnectionString(Constant.FALLBACK_PROCESSOR_NAME)!))
    .AddHttpMessageHandler<CountingHandler>();

builder.Services.AddTransient<CountingHandler>();
builder.Services.AddSingleton<PaymentService>();
builder.Services.AddSingleton<RunningPaymentsSummaryData>();
builder.Services.AddSingleton<ConsoleWriterService>();
builder.Services.AddSingleton<PaymentSummaryService>();
builder.Services.AddSingleton<PaymentBatchInserterService>();
builder.Services.AddSingleton<RabbitMqIntegrationStore>();
builder.Services.AddSingleton<RabbitMqQueueWorker>();
builder.Services.AddSingleton<PaymentProcessorService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<RabbitMqQueueWorker>());

if (builder.Environment.IsProduction() || builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.SetMinimumLevel(LogLevel.Error);
}

builder.Services.InitializeDistributedRabbitMqReactiveLock(Dns.GetHostName());

var opts = builder.Configuration.Get<DefaultOptions>()!;
builder.Services.AddDistributedRabbitMqReactiveLock(
    Constant.DEFAULT_PROCESSOR_ERROR_THRESHOLD_NAME,
    busyThreshold: opts.DEFAULT_PROCESSOR_CIRCUIT_ERROR_THRESHOLD_SECONDS);
builder.Services.AddDistributedRabbitMqReactiveLock(Constant.REACTIVELOCK_HTTP_NAME);
builder.Services.AddDistributedRabbitMqReactiveLock(Constant.REACTIVELOCK_RABBITMQ_NAME);
builder.Services.AddDistributedRabbitMqReactiveLock(
    Constant.REACTIVELOCK_API_PAYMENTS_SUMMARY_NAME,
    [async serviceProvider =>
    {
        var summary = serviceProvider.GetRequiredService<PaymentSummaryService>();
        await summary.FlushWhileGateBlockedAsync();
    }]);

var app = builder.Build();
await app.Services.GetRequiredService<RabbitMqIntegrationStore>().InitializeAsync();
await app.UseDistributedRabbitMqReactiveLockAsync();

var apiGroup = app.MapGroup("/");
apiGroup.MapGet("/", () => Results.Ok());
apiGroup.MapPost("payments", async (
    HttpContext context,
    [FromServices] PaymentService paymentService) =>
    await paymentService.EnqueuePaymentAsync(context));
apiGroup.MapGet("/payments-summary", async (
    [FromQuery] DateTimeOffset? from,
    [FromQuery] DateTimeOffset? to,
    [FromServices] PaymentSummaryService paymentSummaryService) =>
    await paymentSummaryService.GetPaymentsSummaryAsync(from, to));
apiGroup.MapPost("/purge-payments", async (
    [FromServices] PaymentService paymentService) =>
    await paymentService.PurgePaymentsAsync());

app.Run();
