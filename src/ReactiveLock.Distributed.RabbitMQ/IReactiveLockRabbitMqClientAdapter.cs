namespace MichelOliveira.Com.ReactiveLock.Distributed.RabbitMQ;

/// <summary>
/// Abstracts the RabbitMQ operations used by the tracker so transport behavior can be tested independently.
/// </summary>
public interface IReactiveLockRabbitMqClientAdapter
{
    Task PublishAsync(string exchangeName, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default);

    Task SubscribeAsync(
        string exchangeName,
        Func<ReadOnlyMemory<byte>, Task> handler,
        CancellationToken cancellationToken = default);
}
