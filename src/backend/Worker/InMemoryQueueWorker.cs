using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

public class InMemoryQueueWorker : BackgroundService
{
    private ConcurrentQueue<string> Queue { get; } = new();
    private IServiceScopeFactory ScopeFactory { get; }
    private DefaultOptions Options { get; }

    private CancellationTokenSource InternalCts { get; } = new();

    public InMemoryQueueWorker(IServiceScopeFactory scopeFactory, IOptions<DefaultOptions> options)
    {
        ScopeFactory = scopeFactory;
        Options = options.Value;
    }

    public void Clear()
    {
        Queue.Clear();
        InternalCts.Cancel();
    }

    public void Enqueue(string msg)
    {
        Queue.Enqueue(msg);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken, InternalCts.Token);

        var parallelism = Options.WORKER_SIZE;
        var workers = new Task[parallelism];

        for (int i = 0; i < parallelism; i++)
        {
            workers[i] = Task.Run(() => WorkerLoopAsync(linkedCts.Token), linkedCts.Token);
        }

        return Task.WhenAll(workers);
    }

    private async Task WorkerLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (Queue.TryDequeue(out var msg))
                {
                    using var scope = ScopeFactory.CreateScope();
                    var paymentService = scope.ServiceProvider.GetRequiredService<PaymentService>();
                    await paymentService.ProcessPaymentAsync(msg, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Worker Error] {ex}");
            }
        }
    }
}