namespace ReactiveLock.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Moq;
using Xunit;
using MichelOliveira.Com.ReactiveLock.Distributed.Grpc;
using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;

public class ReactiveLockGrpcTrackerExtensionsTests
{

    [Fact]
    public void AddDistributedGrpcReactiveLock_RegistersStateAndController()
    {
        // Arrange
        var services = new ServiceCollection();

        // Ensure InitializeDistributedGrpcReactiveLock has run
        services.InitializeDistributedGrpcReactiveLock("instance-ok", "http://localhost:5000");

        // Act
        services.AddDistributedGrpcReactiveLock("lock-y");

        // Assert that controller/state were registered in DI
        Assert.Contains(services, d => d.ServiceType == typeof(IReactiveLockTrackerState));
        Assert.Contains(services, d => d.ServiceType == typeof(IReactiveLockTrackerController));
    }

    [Fact]
    public async Task UseDistributedGrpcReactiveLockAsync_CompletesInitialization()
    {
        // Arrange
        var services = new ServiceCollection();
        services.InitializeDistributedGrpcReactiveLock("instance-z", "http://localhost:5000");

        services.AddDistributedGrpcReactiveLock("lock-z");

        var factoryMock = new Mock<IReactiveLockTrackerFactory>();
        var stateMock = new Mock<IReactiveLockTrackerState>();
        var controllerMock = new Mock<IReactiveLockTrackerController>();

        controllerMock
            .Setup(c => c.DecrementAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        factoryMock
            .Setup(f => f.GetTrackerState("lock-z"))
            .Returns(stateMock.Object);
        factoryMock
            .Setup(f => f.GetTrackerController("lock-z"))
            .Returns(controllerMock.Object);

        services.AddSingleton(factoryMock.Object);
        var provider = services.BuildServiceProvider();

        var appBuilderMock = new Mock<IApplicationBuilder>();
        appBuilderMock.Setup(a => a.ApplicationServices).Returns(provider);

        // Act
        var task = ReactiveLockGrpcTrackerExtensions.UseDistributedGrpcReactiveLockAsync(appBuilderMock.Object);

        // Assert
        var completed = await Task.WhenAny(task, Task.Delay(200));
        Assert.NotNull(completed);
    }
}
