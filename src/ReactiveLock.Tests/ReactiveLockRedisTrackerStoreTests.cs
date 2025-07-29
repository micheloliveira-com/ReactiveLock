namespace ReactiveLock.Tests;

using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.Distributed.Redis;
using Moq;
using StackExchange.Redis;
using System.Threading.Tasks;
using Xunit;

public class ReactiveLockRedisTrackerStoreTests
{
    [Fact]
    public async Task SetStatusAsync_ShouldSetRedisHash_AndPublishMessage()
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

        var store = new ReactiveLockRedisTrackerStore(mockConnection.Object, redisHashSetKey, redisHashSetNotifierKey);

        // Act
        await store.SetStatusAsync(instanceName, isBusy);

        // Assert
        mockDatabase.Verify(db => db.HashSetAsync(
            redisHashSetKey, instanceName, "1", It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);

        mockSubscriber.Verify(sub => sub.PublishAsync(
            RedisChannel.Literal(redisHashSetNotifierKey), "1", It.IsAny<CommandFlags>()), Times.Once);
    }
}
