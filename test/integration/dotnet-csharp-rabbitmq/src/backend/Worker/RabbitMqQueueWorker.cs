using Microsoft.Extensions.Options;

public sealed class RabbitMqQueueWorker(
    RabbitMqIntegrationStore rabbitMq,
    IServiceScopeFactory scopeFactory,
    IOptions<DefaultOptions> options) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workers = Enumerable.Range(0, options.Value.WORKER_SIZE)
            .Select(_ => rabbitMq.ConsumeWorkQueueAsync(async message =>
            {
                using var scope = scopeFactory.CreateScope();
                var paymentService = scope.ServiceProvider.GetRequiredService<PaymentService>();
                await paymentService.ProcessPaymentAsync(message);
            }, stoppingToken));

        return Task.WhenAll(workers);
    }
}
