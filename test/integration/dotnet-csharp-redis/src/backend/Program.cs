using MichelOliveira.Com.ReactiveLock.Distributed.Redis;
using Polly;
using ReactiveLock.Integration.Shared;
using StackExchange.Redis;
using System.Net;

var builder = WebApplication.CreateSlimBuilder(args);
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

builder.AddIntegrationApplication("redis", "Redis");
builder.Services.AddSingleton<RedisIntegrationBackend>();
builder.Services.AddSingleton<IWorkQueue>(services => services.GetRequiredService<RedisIntegrationBackend>());
builder.Services.AddSingleton<IPaymentStore>(services => services.GetRequiredService<RedisIntegrationBackend>());

builder.Services.InitializeDistributedRedisReactiveLock(Dns.GetHostName());
var options = builder.Configuration.Get<DefaultOptions>()!;
Console.WriteLine($"WORKER_SIZE: {options.WORKER_SIZE}");
Console.WriteLine($"BATCH_SIZE: {options.BATCH_SIZE}");
builder.Services.AddDistributedRedisReactiveLock(
    Constant.DEFAULT_PROCESSOR_ERROR_THRESHOLD_NAME,
    busyThreshold: options.DEFAULT_PROCESSOR_CIRCUIT_ERROR_THRESHOLD_SECONDS);
builder.Services.AddDistributedRedisReactiveLock(Constant.REACTIVELOCK_HTTP_NAME);
builder.Services.AddDistributedRedisReactiveLock("redis");
builder.Services.AddDistributedRedisReactiveLock(
    Constant.REACTIVELOCK_API_PAYMENTS_SUMMARY_NAME,
    [async services => await services.GetRequiredService<PaymentSummaryService>().FlushWhileGateBlockedAsync()]);

var app = builder.Build();
await app.UseDistributedRedisReactiveLockAsync();
app.MapIntegrationEndpoints();
app.Run();
