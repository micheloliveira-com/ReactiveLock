using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Extensions.Options;

public class MongoDbQueueWorker : BackgroundService
{
    private MongoDbIntegrationStore Store { get; }
    private IServiceScopeFactory ScopeFactory { get; }
    public DefaultOptions Options { get; }

    public MongoDbQueueWorker(MongoDbIntegrationStore store, IServiceScopeFactory scopeFactory,
     IOptions<DefaultOptions> options)
    {
        Store = store;
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
                string? message;
                while (!cancellationToken.IsCancellationRequested
                       && (message = await Store.DequeueWorkAsync(cancellationToken).ConfigureAwait(false)) is not null)
                {
                    using var scope = ScopeFactory.CreateScope();
                    var paymentService = scope.ServiceProvider.GetRequiredService<PaymentService>();
                    await paymentService.ProcessPaymentAsync(message).ConfigureAwait(false);
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
