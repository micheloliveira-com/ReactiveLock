namespace MichelOliveira.Com.ReactiveLock.Distributed.RabbitMQ;

using global::RabbitMQ.Client;
using global::RabbitMQ.Client.Events;
using System.Collections.Concurrent;

/// <summary>
/// RabbitMQ.Client implementation used to publish confirmed messages and create broadcast subscriptions.
/// </summary>
public sealed class ReactiveLockRabbitMqClientAdapter(IConnection connection) :
    IReactiveLockRabbitMqClientAdapter,
    IAsyncDisposable
{
    private readonly SemaphoreSlim _publisherGate = new(1, 1);
    private readonly ConcurrentBag<IChannel> _subscriberChannels = [];
    private IChannel? _publisherChannel;

    public async Task PublishAsync(
        string exchangeName,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default)
    {
        await _publisherGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_publisherChannel is null || !_publisherChannel.IsOpen)
            {
                if (_publisherChannel is not null)
                    await _publisherChannel.DisposeAsync().ConfigureAwait(false);

                _publisherChannel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(
                        publisherConfirmationsEnabled: true,
                        publisherConfirmationTrackingEnabled: true),
                    cancellationToken).ConfigureAwait(false);
            }

            await _publisherChannel.ExchangeDeclareAsync(
                exchangeName,
                ExchangeType.Fanout,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var properties = new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent
            };

            await _publisherChannel.BasicPublishAsync(
                exchangeName,
                routingKey: string.Empty,
                mandatory: false,
                basicProperties: properties,
                body,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _publisherGate.Release();
        }
    }

    public async Task SubscribeAsync(
        string exchangeName,
        Func<ReadOnlyMemory<byte>, Task> handler,
        CancellationToken cancellationToken = default)
    {
        var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        try
        {
            await channel.ExchangeDeclareAsync(
                exchangeName,
                ExchangeType.Fanout,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var queue = await channel.QueueDeclareAsync(
                queue: string.Empty,
                durable: false,
                exclusive: true,
                autoDelete: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await channel.QueueBindAsync(
                queue.QueueName,
                exchangeName,
                routingKey: string.Empty,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await channel.BasicQosAsync(0, 1, global: false, cancellationToken).ConfigureAwait(false);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, eventArgs) =>
            {
                try
                {
                    await handler(eventArgs.Body).ConfigureAwait(false);
                    await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false).ConfigureAwait(false);
                }
                catch
                {
                    await channel.BasicNackAsync(
                        eventArgs.DeliveryTag,
                        multiple: false,
                        requeue: false).ConfigureAwait(false);
                }
            };

            await channel.BasicConsumeAsync(
                queue.QueueName,
                autoAck: false,
                consumer,
                cancellationToken).ConfigureAwait(false);

            _subscriberChannels.Add(channel);
        }
        catch
        {
            await channel.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var channel in _subscriberChannels)
            await channel.DisposeAsync().ConfigureAwait(false);

        if (_publisherChannel is not null)
            await _publisherChannel.DisposeAsync().ConfigureAwait(false);

        _publisherGate.Dispose();
    }
}
