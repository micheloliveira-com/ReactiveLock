using Microsoft.Extensions.Options;

namespace ReactiveLock.Integration.Shared;

public sealed class QueueWorker(
    IWorkQueue workQueue,
    PaymentService paymentService,
    IOptions<DefaultOptions> options) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workers = Enumerable.Range(0, options.Value.WORKER_SIZE)
            .Select(_ => Task.Run(() => WorkerLoopAsync(stoppingToken), stoppingToken));
        return Task.WhenAll(workers);
    }

    private async Task WorkerLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                string? message;
                while (!cancellationToken.IsCancellationRequested &&
                       (message = await workQueue.DequeueAsync(cancellationToken).ConfigureAwait(false)) is not null)
                {
                    await paymentService.ProcessPaymentAsync(message).ConfigureAwait(false);
                }

                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Console.WriteLine($"[Worker Error] {exception}");
            }
        }
    }
}
