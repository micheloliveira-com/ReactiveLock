using StackExchange.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Extensions.Options;

public class RedisQueueWorker : BackgroundService
{
    private IDatabase Db { get; }
    private IServiceScopeFactory ScopeFactory { get; }
    public DefaultOptions Options { get; }

    public RedisQueueWorker(IConnectionMultiplexer redis, IServiceScopeFactory scopeFactory,
     IOptions<DefaultOptions> options)
    {
        Db = redis.GetDatabase();
        ScopeFactory = scopeFactory;
        Options = options.Value;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var paralelism = Options.WORKER_SIZE;
        var workers = new Task[paralelism];
        for (int i = 0; i < paralelism; i++)
        {
            workers[i] = Task.Run(() => WorkerLoopAsync(stoppingToken).ConfigureAwait(false), stoppingToken);
        }

        return Task.WhenAll(workers);
    }

    private async Task WorkerLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                RedisValue msg;

                while (!cancellationToken.IsCancellationRequested && (msg = await Db.ListLeftPopAsync(Constant.REDIS_QUEUE_KEY).ConfigureAwait(false)).HasValue)
                {
                    using var scope = ScopeFactory.CreateScope();
                    var paymentService = scope.ServiceProvider.GetRequiredService<PaymentService>();
                    await paymentService.ProcessPaymentAsync(msg!).ConfigureAwait(false);
                }
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                Console.WriteLine($"[Worker Error] {ex}");
            }
        }
    }
}
