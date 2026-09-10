using MichelOliveira.Com.ReactiveLock.Distributed.MongoDB;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using System.Net;
using System.Text.Json;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSingleton<IMongoClient>(_ =>
    new MongoClient(builder.Configuration.GetConnectionString("mongodb")!));

builder.Services.AddOptions<DefaultOptions>().Bind(builder.Configuration);
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
builder.Services.AddSingleton<MongoDbIntegrationStore>();
builder.Services.AddSingleton<MongoDbQueueWorker>();
builder.Services.AddSingleton<PaymentProcessorService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<MongoDbQueueWorker>());

if (builder.Environment.IsProduction() || builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.SetMinimumLevel(LogLevel.Error);
}

builder.Services.InitializeDistributedMongoDbReactiveLock(
    Dns.GetHostName(),
    databaseName: "ReactiveLock",
    collectionName: "LockStatus");

var opts = builder.Configuration.Get<DefaultOptions>()!;
builder.Services.AddDistributedMongoDbReactiveLock(
    Constant.DEFAULT_PROCESSOR_ERROR_THRESHOLD_NAME,
    busyThreshold: opts.DEFAULT_PROCESSOR_CIRCUIT_ERROR_THRESHOLD_SECONDS);
builder.Services.AddDistributedMongoDbReactiveLock(Constant.REACTIVELOCK_HTTP_NAME);
builder.Services.AddDistributedMongoDbReactiveLock(Constant.REACTIVELOCK_MONGODB_NAME);
builder.Services.AddDistributedMongoDbReactiveLock(
    Constant.REACTIVELOCK_API_PAYMENTS_SUMMARY_NAME,
    [async serviceProvider =>
    {
        var summary = serviceProvider.GetRequiredService<PaymentSummaryService>();
        await summary.FlushWhileGateBlockedAsync();
    }]);

var app = builder.Build();
await app.Services.GetRequiredService<MongoDbIntegrationStore>().InitializeAsync();
await app.UseDistributedMongoDbReactiveLockAsync();

var apiGroup = app.MapGroup("/");
apiGroup.MapGet("/", () => Results.Ok());
apiGroup.MapPost("payments", async (HttpContext context, [FromServices] PaymentService service) =>
    await service.EnqueuePaymentAsync(context));
apiGroup.MapGet("/payments-summary", async (
    [FromQuery] DateTimeOffset? from,
    [FromQuery] DateTimeOffset? to,
    [FromServices] PaymentSummaryService service) =>
    await service.GetPaymentsSummaryAsync(from, to));
apiGroup.MapPost("/purge-payments", async ([FromServices] PaymentService service) =>
    await service.PurgePaymentsAsync());

app.Run();
