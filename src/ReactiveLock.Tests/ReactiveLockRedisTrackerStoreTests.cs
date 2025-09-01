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

        var store = new ReactiveLockRedisTrackerStore(
            mockConnection.Object,
            ReactiveLockPollyPolicies.UseOrCreateDefaultRetryPolicy(default),
            (default, TimeSpan.FromSeconds(30), default), // provide some expiration timespan
            redisHashSetKey, redisHashSetNotifierKey);

        // Act
        await store.SetStatusAsync(instanceName, isBusy);

        // Assert
        mockDatabase.Verify(db => db.HashSetAsync(
            redisHashSetKey,
            instanceName,
            It.Is<RedisValue>(v => v.ToString()!.StartsWith("1;")), // starts with "1;" for busy
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()),
            Times.Once);

        mockSubscriber.Verify(sub => sub.PublishAsync(
            RedisChannel.Literal(redisHashSetNotifierKey),
            It.Is<RedisValue>(v => v.ToString()!.StartsWith("1;")), // same for published message
            It.IsAny<CommandFlags>()),
            Times.Once);
    }


    [Fact]
    public async Task SetStatusAsync_ShouldSetRedisHash_AndPublishMessage_WithLockDataAndValidUntil()
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

        // Provide some reasonable TimeSpan for expiration
        var instanceExpiration = TimeSpan.FromSeconds(30);

        var store = new ReactiveLockRedisTrackerStore(
            mockConnection.Object,
            ReactiveLockPollyPolicies.UseOrCreateDefaultRetryPolicy(default),
            (default, instanceExpiration, default),
            redisHashSetKey, redisHashSetNotifierKey);

        // Act
        await store.SetStatusAsync(instanceName, isBusy, lockData);

        // Assert
        // The store appends validUntil as ticks after a semicolon
        var expectedValidUntil = DateTimeOffset.UtcNow + instanceExpiration;
        var expectedValueStart = $"1;{lockData};";
        mockDatabase.Verify(db => db.HashSetAsync(
            redisHashSetKey,
            instanceName,
            It.Is<RedisValue>(v => v.ToString()!.Contains(lockData)),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()),
            Times.Once);

        mockSubscriber.Verify(sub => sub.PublishAsync(
            RedisChannel.Literal(redisHashSetNotifierKey),
            It.Is<RedisValue>(v => v.ToString()!.Contains(lockData)),
            It.IsAny<CommandFlags>()),
            Times.Once);

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
        var validUntilTicks = DateTimeOffset.UtcNow.AddSeconds(30).Ticks;

        var entries = new[]
        {
            new HashEntry("instance1", "0"),
            new HashEntry("instance2", $"1;{validUntilTicks}"), // busy without extra lock data
            new HashEntry("instance3", $"0;extra data")
        };

        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(db => db.HashGetAllAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(entries);

        var (allIdle, lockData) = await ReactiveLockRedisTrackerStore.AreAllIdleAsync("key", mockDb.Object);

        Assert.False(allIdle);
        Assert.Null(lockData); // no meaningful extra data present
    }


    [Fact]
    public async Task AreAllIdleAsync_ReturnsFalseAndConcatenatedLockData_WhenBusyWithExtraData()
    {
        var validUntil1 = DateTimeOffset.UtcNow.AddMinutes(1).Ticks;
        var validUntil2 = DateTimeOffset.UtcNow.AddMinutes(2).Ticks;

        var entries = new[]
        {
            new HashEntry("instance1", $"1;{validUntil1};lockdata1"),
            new HashEntry("instance2", $"1;{validUntil2};lockdata2"),
            new HashEntry("instance3", $"0;{DateTimeOffset.UtcNow.AddMinutes(1).Ticks};ignored")
        };

        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(db => db.HashGetAllAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(entries);

        var (allIdle, lockData) = await ReactiveLockRedisTrackerStore.AreAllIdleAsync("key", mockDb.Object);

        Assert.False(allIdle);
        Assert.Equal($"lockdata1{IReactiveLockTrackerState.LOCK_DATA_SEPARATOR}lockdata2", lockData);
    }

}
