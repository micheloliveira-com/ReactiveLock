namespace ReactiveLock.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Moq;
using StackExchange.Redis;
using Xunit;
using MichelOliveira.Com.ReactiveLock.Distributed.Redis;
using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;

public class ReactiveLockRedisTrackerExtensionsTests
{
    private static void ResetStaticState()
    {
        typeof(ReactiveLockRedisTrackerExtensions)
            .GetProperty("StoredInstanceName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, null);
    }

    [Fact]
    public void AddDistributedRedisReactiveLock_Throws_IfNotInitialized()
    {
        ResetStaticState();

        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddDistributedRedisReactiveLock("some-lock")
        );

        Assert.Contains("InstanceName not initialized", ex.Message);
    }

    [Fact]
    public void AddDistributedRedisReactiveLock_RegistersDependencies()
    {
        var services = new ServiceCollection();

        services.AddSingleton(Mock.Of<IConnectionMultiplexer>());
        services.AddSingleton(Mock.Of<IReactiveLockTrackerFactory>());

        services.InitializeDistributedRedisReactiveLock("instance-x");

        services.AddDistributedRedisReactiveLock("lock-x");

        var provider = services.BuildServiceProvider();

        // assert no registration failure
        Assert.NotNull(provider.GetService<IConnectionMultiplexer>());
    }

    [Fact]
    public async Task UseDistributedRedisReactiveLockAsync_SubscribesAndUpdatesState()
    {
        // Arrange
        var dbMock = new Mock<IDatabase>();
        dbMock.Setup(db => db.HashGetAllAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<HashEntry>()); // Simulate idle state

        var subMock = new Mock<ISubscriber>();

        var multiplexerMock = new Mock<IConnectionMultiplexer>();
        multiplexerMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                    .Returns(dbMock.Object);
        multiplexerMock.Setup(m => m.GetSubscriber(It.IsAny<object>()))
                    .Returns(subMock.Object);

        var controllerMock = new Mock<IReactiveLockTrackerController>();

        var stateMock = new Mock<IReactiveLockTrackerState>();
        stateMock.Setup(s => s.SetLocalStateUnblockedAsync()).Returns(Task.CompletedTask);
        stateMock.Setup(s => s.SetLocalStateBlockedAsync()).Returns(Task.CompletedTask);

        var factoryMock = new Mock<IReactiveLockTrackerFactory>();
        factoryMock.Setup(f => f.GetTrackerController(It.IsAny<string>())).Returns(controllerMock.Object);
        factoryMock.Setup(f => f.GetTrackerState(It.IsAny<string>())).Returns(stateMock.Object);

        var services = new ServiceCollection();
        services.AddSingleton<IConnectionMultiplexer>(multiplexerMock.Object);
        services.AddSingleton<IReactiveLockTrackerFactory>(factoryMock.Object);

        services.InitializeDistributedRedisReactiveLock("instance-test");
        services.AddDistributedRedisReactiveLock("lock-test");

        var appMock = new Mock<IApplicationBuilder>();
        appMock.Setup(a => a.ApplicationServices)
            .Returns(services.BuildServiceProvider());

        // Act
        await appMock.Object.UseDistributedRedisReactiveLockAsync();

        // Assert
        subMock.Verify(s => s.Subscribe(
            It.IsAny<RedisChannel>(),
            It.IsAny<Action<RedisChannel, RedisValue>>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

}

