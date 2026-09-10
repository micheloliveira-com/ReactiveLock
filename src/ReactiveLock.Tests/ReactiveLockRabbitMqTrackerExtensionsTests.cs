namespace ReactiveLock.Tests;

using MichelOliveira.Com.ReactiveLock.DependencyInjection;
using MichelOliveira.Com.ReactiveLock.Distributed.RabbitMQ;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using static ReactiveLock.Tests.ReactiveLockRabbitMqTrackerStoreTests;

public class ReactiveLockRabbitMqTrackerExtensionsTests
{
    [Fact]
    public void AddDistributedRabbitMqReactiveLock_ThrowsWhenNotInitialized()
    {
        var services = new ServiceCollection();
        services.InitializeDistributedRabbitMqReactiveLock(string.Empty);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddDistributedRabbitMqReactiveLock("orders"));

        Assert.Contains("InstanceName not initialized", exception.Message);
    }

    [Fact]
    public async Task UseDistributedRabbitMqReactiveLockAsync_SubscribesAndPublishesInitialState()
    {
        var adapter = new FakeRabbitMqClientAdapter();
        var services = new ServiceCollection();
        services.AddSingleton<IReactiveLockRabbitMqClientAdapter>(adapter);
        services.InitializeDistributedRabbitMqReactiveLock("instance-1");
        services.AddDistributedRabbitMqReactiveLock("orders");

        var app = new ApplicationBuilder(services.BuildServiceProvider());
        await app.UseDistributedRabbitMqReactiveLockAsync();

        Assert.Single(adapter.SubscribedExchanges);
        Assert.Contains(adapter.Published, published =>
            ReactiveLockRabbitMqMessage.Deserialize(published.Body)?.Kind
                == ReactiveLockRabbitMqMessage.StatusKind);
        Assert.Contains(adapter.Published, published =>
            ReactiveLockRabbitMqMessage.Deserialize(published.Body)?.Kind
                == ReactiveLockRabbitMqMessage.SnapshotRequestKind);
    }

    [Fact]
    public async Task InstancesSharingAnExchange_ObserveBusyAndIdleTransitions()
    {
        var adapter = new FakeRabbitMqClientAdapter();
        var first = await BuildInstanceAsync(adapter, "instance-1");
        var second = await BuildInstanceAsync(adapter, "instance-2");

        var firstFactory = first.ApplicationServices.GetRequiredService<IReactiveLockTrackerFactory>();
        var secondFactory = second.ApplicationServices.GetRequiredService<IReactiveLockTrackerFactory>();
        var controller = firstFactory.GetTrackerController("orders");
        var remoteState = secondFactory.GetTrackerState("orders");

        await controller.IncrementAsync("order-42");
        Assert.True(await remoteState.IsBlockedAsync());
        Assert.Equal("order-42", await remoteState.GetLockDataIfBlockedAsync());

        await controller.DecrementAsync();
        Assert.False(await remoteState.IsBlockedAsync());
    }

    private static async Task<ApplicationBuilder> BuildInstanceAsync(
        FakeRabbitMqClientAdapter adapter,
        string instanceName)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IReactiveLockRabbitMqClientAdapter>(adapter);
        services.InitializeDistributedRabbitMqReactiveLock(instanceName);
        services.AddDistributedRabbitMqReactiveLock("orders");

        var app = new ApplicationBuilder(services.BuildServiceProvider());
        await app.UseDistributedRabbitMqReactiveLockAsync();
        return app;
    }
}
