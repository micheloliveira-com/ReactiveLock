namespace ReactiveLock.Tests;

using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.Distributed.RabbitMQ;
using System.Collections.Concurrent;

public class ReactiveLockRabbitMqTrackerStoreTests
{
    [Fact]
    public async Task SetStatusAsync_PublishesBusyStatusWithLockDataAndExpiration()
    {
        var rabbitMq = new FakeRabbitMqClientAdapter();
        var store = new ReactiveLockRabbitMqTrackerStore(
            rabbitMq,
            "instance-1",
            null,
            (default, TimeSpan.FromSeconds(30), default),
            "orders",
            "exchange-orders");

        var before = DateTimeOffset.UtcNow;
        await store.SetStatusAsync(true, "order-42");

        var published = Assert.Single(rabbitMq.Published);
        Assert.Equal("exchange-orders", published.ExchangeName);

        var message = ReactiveLockRabbitMqMessage.Deserialize(published.Body);
        Assert.NotNull(message);
        Assert.Equal(ReactiveLockRabbitMqMessage.StatusKind, message.Kind);
        Assert.Equal("orders", message.LockKey);
        Assert.Equal("instance-1", message.InstanceId);
        Assert.True(message.IsBusy);
        Assert.Equal("order-42", message.LockData);
        Assert.True(message.ValidUntil > before.AddSeconds(25));
    }

    [Fact]
    public async Task SetStatusAsync_IncrementsRevisionForEachTransition()
    {
        var rabbitMq = new FakeRabbitMqClientAdapter();
        var store = new ReactiveLockRabbitMqTrackerStore(
            rabbitMq, "instance-1", null, default, "orders", "exchange-orders");

        await store.SetStatusAsync(true);
        await store.SetStatusAsync(false);

        var messages = rabbitMq.Published
            .Select(entry => ReactiveLockRabbitMqMessage.Deserialize(entry.Body)!)
            .ToArray();

        Assert.Equal(2, messages.Length);
        Assert.True(messages[1].Revision > messages[0].Revision);
        Assert.False(messages[1].IsBusy);
    }

    [Fact]
    public void AreAllIdle_ReturnsTrueForNoOrExpiredBusyInstances()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = Status("one", true, "expired", now.AddSeconds(-1));

        Assert.True(ReactiveLockRabbitMqTrackerStore.AreAllIdle([]).allIdle);
        var result = ReactiveLockRabbitMqTrackerStore.AreAllIdle([expired], now);
        Assert.True(result.allIdle);
        Assert.Null(result.lockData);
    }

    [Fact]
    public void AreAllIdle_CombinesDataFromValidBusyInstances()
    {
        var now = DateTimeOffset.UtcNow;
        var entries = new[]
        {
            Status("one", true, "first", now.AddMinutes(1)),
            Status("two", false, "ignored", now.AddMinutes(1)),
            Status("three", true, "second", now.AddMinutes(1))
        };

        var result = ReactiveLockRabbitMqTrackerStore.AreAllIdle(entries, now);

        Assert.False(result.allIdle);
        Assert.Equal($"first{IReactiveLockTrackerState.LOCK_DATA_SEPARATOR}second", result.lockData);
    }

    [Fact]
    public void Message_RoundTripsAndCreatesSnapshotRequest()
    {
        var message = Status("instance", true, "data", DateTimeOffset.UtcNow.AddMinutes(1));
        var roundTrip = ReactiveLockRabbitMqMessage.Deserialize(message.Serialize());

        Assert.Equal(message, roundTrip);

        var request = ReactiveLockRabbitMqMessage.CreateSnapshotRequest("orders", "requester");
        Assert.Equal(ReactiveLockRabbitMqMessage.SnapshotRequestKind, request.Kind);
        Assert.Equal("orders", request.LockKey);
    }

    private static ReactiveLockRabbitMqMessage Status(
        string instance,
        bool isBusy,
        string? data,
        DateTimeOffset validUntil) =>
        new(
            ReactiveLockRabbitMqMessage.StatusKind,
            "orders",
            instance,
            isBusy,
            data,
            validUntil.UtcTicks,
            1);

    internal sealed class FakeRabbitMqClientAdapter : IReactiveLockRabbitMqClientAdapter
    {
        private readonly ConcurrentDictionary<string, ConcurrentBag<Func<ReadOnlyMemory<byte>, Task>>> _handlers = [];

        public ConcurrentQueue<(string ExchangeName, byte[] Body)> Published { get; } = [];
        public ConcurrentQueue<string> SubscribedExchanges { get; } = [];

        public async Task PublishAsync(
            string exchangeName,
            ReadOnlyMemory<byte> body,
            CancellationToken cancellationToken = default)
        {
            var copy = body.ToArray();
            Published.Enqueue((exchangeName, copy));

            if (!_handlers.TryGetValue(exchangeName, out var handlers))
                return;

            foreach (var handler in handlers)
                await handler(copy).ConfigureAwait(false);
        }

        public Task SubscribeAsync(
            string exchangeName,
            Func<ReadOnlyMemory<byte>, Task> handler,
            CancellationToken cancellationToken = default)
        {
            _handlers.GetOrAdd(exchangeName, _ => []).Add(handler);
            SubscribedExchanges.Enqueue(exchangeName);
            return Task.CompletedTask;
        }
    }
}
