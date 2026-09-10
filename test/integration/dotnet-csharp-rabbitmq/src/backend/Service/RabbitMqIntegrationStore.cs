using System.Collections.Concurrent;
using System.Text.Json;
using global::RabbitMQ.Client;
using global::RabbitMQ.Client.Events;

public sealed class RabbitMqIntegrationStore(IConnection connection) : IAsyncDisposable
{
    private readonly ConcurrentQueue<PaymentInsertParameters> _payments = [];
    private readonly SemaphoreSlim _publisherGate = new(1, 1);
    private IChannel? _publisherChannel;
    private IChannel? _paymentSubscriberChannel;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _paymentSubscriberChannel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await _paymentSubscriberChannel.ExchangeDeclareAsync(
            Constant.PAYMENTS_EXCHANGE,
            ExchangeType.Fanout,
            durable: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        var queue = await _paymentSubscriberChannel.QueueDeclareAsync(
            queue: string.Empty,
            durable: false,
            exclusive: true,
            autoDelete: true,
            cancellationToken: cancellationToken);
        await _paymentSubscriberChannel.QueueBindAsync(
            queue.QueueName,
            Constant.PAYMENTS_EXCHANGE,
            string.Empty,
            cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(_paymentSubscriberChannel);
        consumer.ReceivedAsync += async (_, args) =>
        {
            try
            {
                var message = JsonSerializer.Deserialize(args.Body.Span, JsonContext.Default.PaymentReplicationMessage);
                if (message?.Kind == PaymentReplicationMessage.PaymentKind && message.Payment is not null)
                    _payments.Enqueue(message.Payment);
                else if (message?.Kind == PaymentReplicationMessage.PurgeKind)
                    ClearPayments();

                await _paymentSubscriberChannel.BasicAckAsync(args.DeliveryTag, false);
            }
            catch
            {
                await _paymentSubscriberChannel.BasicNackAsync(args.DeliveryTag, false, false);
            }
        };

        await _paymentSubscriberChannel.BasicConsumeAsync(
            queue.QueueName,
            autoAck: false,
            consumer,
            cancellationToken);
    }

    public Task EnqueueWorkAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default) =>
        PublishAsync(string.Empty, Constant.WORK_QUEUE, body, declareWorkQueue: true, cancellationToken);

    public async Task PublishPaymentAsync(
        PaymentInsertParameters payment,
        CancellationToken cancellationToken = default)
    {
        var message = new PaymentReplicationMessage(PaymentReplicationMessage.PaymentKind, payment);
        var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonContext.Default.PaymentReplicationMessage);
        await PublishAsync(Constant.PAYMENTS_EXCHANGE, string.Empty, body, false, cancellationToken);
    }

    public async Task PurgePaymentsAsync(CancellationToken cancellationToken = default)
    {
        ClearPayments();
        var message = new PaymentReplicationMessage(PaymentReplicationMessage.PurgeKind, null);
        var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonContext.Default.PaymentReplicationMessage);
        await PublishAsync(Constant.PAYMENTS_EXCHANGE, string.Empty, body, false, cancellationToken);
    }

    public PaymentInsertParameters[] GetPayments() => _payments.ToArray();

    public async Task ConsumeWorkQueueAsync(
        Func<string, Task> handler,
        CancellationToken cancellationToken)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(
            Constant.WORK_QUEUE,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.BasicQosAsync(0, 1, false, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) =>
        {
            try
            {
                await handler(System.Text.Encoding.UTF8.GetString(args.Body.Span));
                await channel.BasicAckAsync(args.DeliveryTag, false);
            }
            catch
            {
                await channel.BasicNackAsync(args.DeliveryTag, false, true);
            }
        };

        await channel.BasicConsumeAsync(
            Constant.WORK_QUEUE,
            autoAck: false,
            consumer,
            cancellationToken);

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private async Task PublishAsync(
        string exchange,
        string routingKey,
        ReadOnlyMemory<byte> body,
        bool declareWorkQueue,
        CancellationToken cancellationToken)
    {
        await _publisherGate.WaitAsync(cancellationToken);
        try
        {
            _publisherChannel ??= await connection.CreateChannelAsync(
                new CreateChannelOptions(true, true),
                cancellationToken);

            if (declareWorkQueue)
            {
                await _publisherChannel.QueueDeclareAsync(
                    Constant.WORK_QUEUE,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    cancellationToken: cancellationToken);
            }
            else
            {
                await _publisherChannel.ExchangeDeclareAsync(
                    Constant.PAYMENTS_EXCHANGE,
                    ExchangeType.Fanout,
                    durable: false,
                    autoDelete: false,
                    cancellationToken: cancellationToken);
            }

            var properties = new BasicProperties { DeliveryMode = DeliveryModes.Persistent };
            await _publisherChannel.BasicPublishAsync(
                exchange,
                routingKey,
                mandatory: false,
                properties,
                body,
                cancellationToken);
        }
        finally
        {
            _publisherGate.Release();
        }
    }

    private void ClearPayments()
    {
        while (_payments.TryDequeue(out _)) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_paymentSubscriberChannel is not null)
            await _paymentSubscriberChannel.DisposeAsync();
        if (_publisherChannel is not null)
            await _publisherChannel.DisposeAsync();
        _publisherGate.Dispose();
    }
}
