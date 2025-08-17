using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;
using MichelOliveira.Com.ReactiveLock.Distributed.Grpc;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using Polly.Retry;
using System.Data;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

var grpcReady = false;

var builder = WebApplication.CreateSlimBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8081, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
    });

    options.ListenAnyIP(8080, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
    });
});
var warmupRetryAsyncPolicy = Policy
    .Handle<Exception>()
    .WaitAndRetryAsync(
        retryCount: 60 * 10,
        sleepDurationProvider: _ => TimeSpan.FromSeconds(0.1),
        onRetry: (exception, timeSpan, retryCount, context) =>
        {
            Console.WriteLine(
                $"Retry {retryCount}: {exception.GetType().Name} - {exception.Message}\n" +
                $"StackTrace:\n{exception.StackTrace}\n" +
                new string('-', 40));
        });

builder.Services
    .AddOptions<DefaultOptions>()
    .Bind(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, JsonContext.Default);
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.AddHttpClient(Constant.DEFAULT_PROCESSOR_NAME, o =>
    o.BaseAddress = new Uri(builder.Configuration.GetConnectionString(Constant.DEFAULT_PROCESSOR_NAME)!))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        MaxConnectionsPerServer = int.MaxValue,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
        EnableMultipleHttp2Connections = true,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    })
    .AddHttpMessageHandler<CountingHandler>();

builder.Services.AddTransient<CountingHandler>();
builder.Services.AddSingleton<PaymentService>();
builder.Services.AddSingleton<RunningPaymentsSummaryData>();
builder.Services.AddSingleton<ConsoleWriterService>();
builder.Services.AddSingleton<PaymentSummaryService>();
builder.Services.AddSingleton<PaymentBatchInserterService>();
builder.Services.AddSingleton<InMemoryQueueWorker>();
builder.Services.AddSingleton<PaymentProcessorService>();

builder.Services.AddHostedService(provider => provider.GetRequiredService<InMemoryQueueWorker>());

if (builder.Environment.IsProduction() || builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.SetMinimumLevel(LogLevel.Error);
}

var local = builder.Configuration.GetConnectionString("rpc_local_server");
var remote = builder.Configuration.GetConnectionString("rpc_replica_server");

if (string.IsNullOrWhiteSpace(local) || string.IsNullOrWhiteSpace(remote))
    throw new InvalidOperationException("Missing RPC server addresses in configuration.");

builder.Services.InitializeDistributedGrpcReactiveLock(Dns.GetHostName(), local, remote);

var opts = builder.Configuration
    .Get<DefaultOptions>()!;

Console.WriteLine($"WORKER_SIZE: {opts.WORKER_SIZE}");
Console.WriteLine($"BATCH_SIZE: {opts.BATCH_SIZE}");

builder.Services.AddDistributedGrpcReactiveLock(Constant.DEFAULT_PROCESSOR_ERROR_THRESHOLD_NAME,
                                                    busyThreshold: opts.DEFAULT_PROCESSOR_CIRCUIT_ERROR_THRESHOLD_SECONDS);
builder.Services.AddDistributedGrpcReactiveLock(Constant.REACTIVELOCK_HTTP_NAME);
builder.Services.AddDistributedGrpcReactiveLock(Constant.REACTIVELOCK_GRPC_NAME);
builder.Services.AddDistributedGrpcReactiveLock(Constant.REACTIVELOCK_API_PAYMENTS_SUMMARY_NAME, [
    async(sp) => {
        var summary = sp.GetRequiredService<PaymentSummaryService>();
        await summary.FlushWhileGateBlockedAsync();
    }
]);
builder.Services.AddGrpc();
builder.Services.AddSingleton<ReactiveLockGrpcService>();
builder.Services.AddSingleton<PaymentReplicationService>();
builder.Services.AddSingleton<PaymentReplicationClientManager>(sp =>
{
    return new PaymentReplicationClientManager(local, remote);
});

var app = builder.Build();

var manager = app.Services.GetRequiredService<PaymentReplicationClientManager>();

var apiGroup = app.MapGroup("/");

app.Use(async (context, next) =>
{
    if (context.Connection.LocalPort == 8080)
    {
        if (!grpcReady)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }
    }

    await next();
});
apiGroup.MapGet("/", () => Results.Ok());

apiGroup.MapPost("payments", async (HttpContext context,
    [FromServices] PaymentService paymentService) =>
{
    return await paymentService.EnqueuePaymentAsync(context);
});

apiGroup.MapGet("/payments-summary", async (
    [FromQuery] DateTimeOffset? from,
    [FromQuery] DateTimeOffset? to,
    [FromServices] PaymentSummaryService paymentsSummaryService) =>
{
    return await paymentsSummaryService.GetPaymentsSummaryAsync(from, to);
});

apiGroup.MapPost("/purge-payments", async (
    [FromServices] PaymentService paymentService) =>
{
    return await paymentService.PurgePaymentsAsync();
});

app.MapGrpcService<ReactiveLockGrpcService>();
app.MapGrpcService<PaymentReplicationService>();
_ = Task.Run(async () =>
{
    await warmupRetryAsyncPolicy.ExecuteAsync(async () =>
    {
        await app.UseDistributedGrpcReactiveLockAsync();
        grpcReady = true;
    });
});
app.Run();

