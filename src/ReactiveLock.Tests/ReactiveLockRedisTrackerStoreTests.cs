namespace ReactiveLock.Tests;

using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.Distributed.Redis;
using Moq;
using ReactiveLock.Shared.Distributed;
using StackExchange.Redis;
using System.Threading.Tasks;
using Xunit;

public class ReactiveLockRedisTrackerStoreTests
{
    [Fact]
    public async Task SetStatusAsync_ShouldSetRedisHash_AndPublishMessage_WithoutLockData()
    {
        // Arrange
        var mockDatabase = new Mock<IDatabase>();
        var mockSubscriber = new Mock<ISubscriber>();
        var mockConnection = new Mock<IConnectionMultiplexer>();

        var redisHashSetKey = "test:hash";
        var redisHashSetNotifierKey = "test:notifier";
        var instanceName = "instance1";
        var isBusy = true;

        mockConnection.Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                      .Returns(mockDatabase.Object);
        mockConnection.Setup(c => c.GetSubscriber(It.IsAny<object>()))
                      .Returns(mockSubscriber.Object);

        var store = new ReactiveLockRedisTrackerStore(mockConnection.Object, ReactiveLockPollyPolicies.UseOrCreateDefaultRetryPolicy(default), redisHashSetKey, redisHashSetNotifierKey);

        // Act
        await store.SetStatusAsync(instanceName, isBusy);

        // Assert
        mockDatabase.Verify(db => db.HashSetAsync(
            redisHashSetKey, instanceName, "1", It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);

        mockSubscriber.Verify(sub => sub.PublishAsync(
            RedisChannel.Literal(redisHashSetNotifierKey), "1", It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task SetStatusAsync_ShouldSetRedisHash_AndPublishMessage_WithLockData()
    {
        // Arrange
        var mockDatabase = new Mock<IDatabase>();
        var mockSubscriber = new Mock<ISubscriber>();
        var mockConnection = new Mock<IConnectionMultiplexer>();

        var redisHashSetKey = "test:hash";
        var redisHashSetNotifierKey = "test:notifier";
        var instanceName = "instance1";
        var isBusy = true;
        var lockData = "some lock metadata";

        mockConnection.Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                      .Returns(mockDatabase.Object);
        mockConnection.Setup(c => c.GetSubscriber(It.IsAny<object>()))
                      .Returns(mockSubscriber.Object);

        var store = new ReactiveLockRedisTrackerStore(mockConnection.Object, ReactiveLockPollyPolicies.UseOrCreateDefaultRetryPolicy(default), redisHashSetKey, redisHashSetNotifierKey);

        // Act
        await store.SetStatusAsync(instanceName, isBusy, lockData);

        // Assert
        var expectedValue = "1;" + lockData;

        mockDatabase.Verify(db => db.HashSetAsync(
            redisHashSetKey, instanceName, expectedValue, It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);

        mockSubscriber.Verify(sub => sub.PublishAsync(
            RedisChannel.Literal(redisHashSetNotifierKey), expectedValue, It.IsAny<CommandFlags>()), Times.Once);
    }


    [Fact]
    public async Task AreAllIdleAsync_ReturnsTrueAndNull_WhenNoEntries()
    {
        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(db => db.HashGetAllAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync(Array.Empty<HashEntry>());

        var (allIdle, lockData) = await ReactiveLockRedisTrackerStore.AreAllIdleAsync("key", mockDb.Object);

        Assert.True(allIdle);
        Assert.Null(lockData);
    }

    [Fact]
    public async Task AreAllIdleAsync_ReturnsTrueAndNull_WhenAllIdle()
    {
        var entries = new[]
        {
            new HashEntry("instance1", "0"),
            new HashEntry("instance2", ""),
            new HashEntry("instance3", "0;extra data here") // idle but with extra data
        };

        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(db => db.HashGetAllAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync(entries);

        var (allIdle, lockData) = await ReactiveLockRedisTrackerStore.AreAllIdleAsync("key", mockDb.Object);

        Assert.True(allIdle);
        Assert.Null(lockData);
    }

    [Fact]
    public async Task AreAllIdleAsync_ReturnsFalseAndNull_WhenBusyWithoutExtraData()
    {
        var entries = new[]
        {
            new HashEntry("instance1", "0"),
            new HashEntry("instance2", "1"), // busy without extra
            new HashEntry("instance3", "0;extra data")
        };

        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(db => db.HashGetAllAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync(entries);

        var (allIdle, lockData) = await ReactiveLockRedisTrackerStore.AreAllIdleAsync("key", mockDb.Object);

        Assert.False(allIdle);
        Assert.Null(lockData); // no extra data present
    }

    [Fact]
    public async Task AreAllIdleAsync_ReturnsFalseAndConcatenatedLockData_WhenBusyWithExtraData()
    {
        var entries = new[]
        {
            new HashEntry("instance1", "1;lockdata1"),
            new HashEntry("instance2", "1;lockdata2"),
            new HashEntry("instance3", "0;ignored")
        };

        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(db => db.HashGetAllAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync(entries);

        var (allIdle, lockData) = await ReactiveLockRedisTrackerStore.AreAllIdleAsync("key", mockDb.Object);

        Assert.False(allIdle);
        Assert.Equal($"lockdata1{IReactiveLockTrackerState.LOCK_DATA_SEPARATOR}lockdata2", lockData);
    }
}
