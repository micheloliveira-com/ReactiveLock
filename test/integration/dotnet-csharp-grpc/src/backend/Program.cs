using MichelOliveira.Com.ReactiveLock.Distributed.Grpc;
using Polly;
using ReactiveLock.Integration.Shared;
using StackExchange.Redis;
using System.Net;

var grpcReady = false;
var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8081, listen =>
        listen.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
    options.ListenAnyIP(8080, listen =>
        listen.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1);
});

var warmupRetryAsyncPolicy = Policy
    .Handle<Exception>()
    .WaitAndRetryAsync(
        retryCount: 600,
        sleepDurationProvider: _ => TimeSpan.FromMilliseconds(100),
        onRetry: (exception, _, retryCount, _) =>
            Console.WriteLine($"Retry {retryCount}: {exception.GetType().Name} - {exception.Message}"));
var warmupRetryPolicy = Policy
    .Handle<Exception>()
    .WaitAndRetry(
        retryCount: 600,
        sleepDurationProvider: _ => TimeSpan.FromMilliseconds(100),
        onRetry: (exception, _, retryCount, _) =>
            Console.WriteLine($"Retry {retryCount}: {exception.GetType().Name} - {exception.Message}"));

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var options = ConfigurationOptions.Parse(builder.Configuration.GetConnectionString("redis")!);
    return warmupRetryPolicy.Execute(() =>
    {
        Console.WriteLine("[Redis] Attempting connection...");
        var connection = ConnectionMultiplexer.Connect(options);
        if (!connection.IsConnected)
            throw new InvalidOperationException("Redis connection failed (IsConnected = false)");
        Console.WriteLine("[Redis] Connected successfully.");
        return connection;
    });
});

var local = builder.Configuration.GetConnectionString("rpc_local_server");
var remote = builder.Configuration.GetConnectionString("rpc_replica_server");
if (string.IsNullOrWhiteSpace(local) || string.IsNullOrWhiteSpace(remote))
    throw new InvalidOperationException("Missing RPC server addresses in configuration.");

builder.AddIntegrationApplication("grpc", "Grpc");
builder.Services.AddSingleton<RedisWorkQueue>();
builder.Services.AddSingleton<IWorkQueue>(services => services.GetRequiredService<RedisWorkQueue>());
builder.Services.AddSingleton<GrpcPaymentStore>();
builder.Services.AddSingleton<IPaymentStore>(services => services.GetRequiredService<GrpcPaymentStore>());
builder.Services.AddGrpc();
builder.Services.AddSingleton<ReactiveLockGrpcService>();
builder.Services.AddSingleton<PaymentReplicationService>();
builder.Services.AddSingleton(_ => new PaymentReplicationClientManager(local, remote));

builder.Services.InitializeDistributedGrpcReactiveLock(Dns.GetHostName(), local, remote);
var options = builder.Configuration.Get<DefaultOptions>()!;
Console.WriteLine($"WORKER_SIZE: {options.WORKER_SIZE}");
Console.WriteLine($"BATCH_SIZE: {options.BATCH_SIZE}");
builder.Services.AddDistributedGrpcReactiveLock(
    Constant.DEFAULT_PROCESSOR_ERROR_THRESHOLD_NAME,
    busyThreshold: options.DEFAULT_PROCESSOR_CIRCUIT_ERROR_THRESHOLD_SECONDS);
builder.Services.AddDistributedGrpcReactiveLock(Constant.REACTIVELOCK_HTTP_NAME);
builder.Services.AddDistributedGrpcReactiveLock("grpc");
builder.Services.AddDistributedGrpcReactiveLock(
    Constant.REACTIVELOCK_API_PAYMENTS_SUMMARY_NAME,
    [async services => await services.GetRequiredService<PaymentSummaryService>().FlushWhileGateBlockedAsync()]);

var app = builder.Build();
app.Use(async (context, next) =>
{
    if (context.Connection.LocalPort == 8080 && !grpcReady)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return;
    }
    await next();
});
app.MapIntegrationEndpoints();
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
