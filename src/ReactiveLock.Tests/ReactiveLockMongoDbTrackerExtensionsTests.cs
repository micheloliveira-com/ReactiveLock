namespace ReactiveLock.Tests;

using MichelOliveira.Com.ReactiveLock.DependencyInjection;
using MichelOliveira.Com.ReactiveLock.Distributed.MongoDB;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using static ReactiveLock.Tests.ReactiveLockMongoDbTrackerStoreTests;

public class ReactiveLockMongoDbTrackerExtensionsTests
{
    [Theory]
    [InlineData("", "LockStatus")]
    [InlineData("ReactiveLock", "")]
    public void InitializeDistributedMongoDbReactiveLock_RejectsBlankStorageNames(
        string databaseName,
        string collectionName)
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() =>
            services.InitializeDistributedMongoDbReactiveLock(
                "instance-1",
                databaseName,
                collectionName));
    }

    [Fact]
    public void AddDistributedMongoDbReactiveLock_ThrowsWhenNotInitialized()
    {
        var services = new ServiceCollection();
        services.InitializeDistributedMongoDbReactiveLock(string.Empty);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddDistributedMongoDbReactiveLock("orders"));

        Assert.Contains("InstanceName not initialized", exception.Message);
    }

    [Fact]
    public async Task UseDistributedMongoDbReactiveLockAsync_ThrowsWhenNotInitialized()
    {
        var services = new ServiceCollection();
        services.InitializeDistributedMongoDbReactiveLock(string.Empty);
        var app = new ApplicationBuilder(services.BuildServiceProvider());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            app.UseDistributedMongoDbReactiveLockAsync());

        Assert.Contains("InstanceName not initialized", exception.Message);
    }

    [Fact]
    public void ResolvingControllerBeforeUseDistributedMongoDbReactiveLockAsync_ThrowsHelpfulError()
    {
        var mongoDb = new FakeMongoDbClientAdapter();
        var services = new ServiceCollection();
        services.AddSingleton<IReactiveLockMongoDbClientAdapter>(mongoDb);
        services.InitializeDistributedMongoDbReactiveLock("instance-1");
        services.AddDistributedMongoDbReactiveLock("orders");
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IReactiveLockTrackerFactory>();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            factory.GetTrackerController("orders"));

        Assert.Contains("UseDistributedMongoDbReactiveLockAsync", exception.Message);
    }

    [Fact]
    public async Task UseDistributedMongoDbReactiveLockAsync_InitializesWatcherAndIdleLease()
    {
        var mongoDb = new FakeMongoDbClientAdapter();
        var app = await BuildInstanceAsync(mongoDb);

        Assert.True(mongoDb.InfrastructureEnsured);
        Assert.Contains("orders", mongoDb.WatchedLockKeys!);
        Assert.Contains(mongoDb.Documents.Values, document =>
            document.LockKey == "orders" && !document.IsBusy);
        Assert.NotNull(app.ApplicationServices.GetRequiredService<IReactiveLockTrackerFactory>());
    }

    [Fact]
    public async Task StoreChange_UpdatesLocalReactiveState()
    {
        var mongoDb = new FakeMongoDbClientAdapter();
        var app = await BuildInstanceAsync(mongoDb);
        var factory = app.ApplicationServices.GetRequiredService<IReactiveLockTrackerFactory>();
        var controller = factory.GetTrackerController("orders");
        var state = factory.GetTrackerState("orders");

        await controller.IncrementAsync("order-42");

        Assert.True(await state.IsBlockedAsync());
        Assert.Equal("order-42", await state.GetLockDataIfBlockedAsync());

        await controller.DecrementAsync();
        Assert.False(await state.IsBlockedAsync());
    }

    [Fact]
    public async Task UseDistributedMongoDbReactiveLockAsync_WatchesEveryDistinctRegisteredLock()
    {
        var mongoDb = new FakeMongoDbClientAdapter();
        var services = new ServiceCollection();
        services.AddSingleton<IReactiveLockMongoDbClientAdapter>(mongoDb);
        services.InitializeDistributedMongoDbReactiveLock("instance-1");
        services.AddDistributedMongoDbReactiveLock("orders");
        services.AddDistributedMongoDbReactiveLock("payments");
        var app = new ApplicationBuilder(services.BuildServiceProvider());

        await app.UseDistributedMongoDbReactiveLockAsync();

        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal) { "orders", "payments" },
            mongoDb.WatchedLockKeys);
        Assert.Contains(mongoDb.Documents.Values, document =>
            document.LockKey == "orders" && !document.IsBusy);
        Assert.Contains(mongoDb.Documents.Values, document =>
            document.LockKey == "payments" && !document.IsBusy);
    }

    [Fact]
    public async Task ChangeForUnregisteredLock_IsIgnored()
    {
        var mongoDb = new FakeMongoDbClientAdapter();
        _ = await BuildInstanceAsync(mongoDb);

        await mongoDb.NotifyAsync("not-registered");
    }

    private static async Task<ApplicationBuilder> BuildInstanceAsync(
        FakeMongoDbClientAdapter mongoDb)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IReactiveLockMongoDbClientAdapter>(mongoDb);
        services.InitializeDistributedMongoDbReactiveLock("instance-1");
        services.AddDistributedMongoDbReactiveLock("orders");

        var app = new ApplicationBuilder(services.BuildServiceProvider());
        await app.UseDistributedMongoDbReactiveLockAsync();
        return app;
    }
}
