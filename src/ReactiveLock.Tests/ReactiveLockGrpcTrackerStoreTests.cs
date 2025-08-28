namespace ReactiveLock.Tests;

using System.Threading.Tasks;
using Xunit;
using Grpc.Core;
using ReactiveLock.Distributed.Grpc;
using MichelOliveira.Com.ReactiveLock.Distributed.Grpc;
using static ReactiveLock.Distributed.Grpc.ReactiveLockGrpc;
using MichelOliveira.Com.ReactiveLock.Core;
using Moq;
using ReactiveLock.Shared.Distributed;

public class ReactiveLockGrpcTrackerStoreTests
{
    [Fact]
    public void AreAllIdle_NoInstances_ReturnsTrue()
    {
        var update = new LockStatusNotification(); // InstancesStatus is empty by default
        var (allIdle, lockData) = ReactiveLockGrpcTrackerStore.AreAllIdle(update);

        Assert.True(allIdle);
        Assert.Null(lockData);
    }

    [Fact]
    public void AreAllIdle_AllIdle_ReturnsTrue()
    {
        var update = new LockStatusNotification();
        update.InstancesStatus.Add("inst1", new InstanceLockStatus { IsBusy = false });
        update.InstancesStatus.Add("inst2", new InstanceLockStatus { IsBusy = false });

        var (allIdle, lockData) = ReactiveLockGrpcTrackerStore.AreAllIdle(update);

        Assert.True(allIdle);
        Assert.Null(lockData);
    }

    [Fact]
    public void AreAllIdle_SomeBusy_ReturnsFalseWithLockData()
    {
        var update = new LockStatusNotification();
        update.InstancesStatus.Add("inst1", new InstanceLockStatus { IsBusy = true, LockData = "abc" });
        update.InstancesStatus.Add("inst2", new InstanceLockStatus { IsBusy = false });

        var (allIdle, lockData) = ReactiveLockGrpcTrackerStore.AreAllIdle(update);

        Assert.False(allIdle);
        Assert.Equal("abc", lockData);
    }

    [Fact]
    public void AreAllIdle_MultipleBusy_ReturnsCombinedLockData()
    {
        var update = new LockStatusNotification();
        update.InstancesStatus.Add("inst1", new InstanceLockStatus { IsBusy = true, LockData = "abc" });
        update.InstancesStatus.Add("inst2", new InstanceLockStatus { IsBusy = true, LockData = "def" });

        var (allIdle, lockData) = ReactiveLockGrpcTrackerStore.AreAllIdle(update);

        Assert.False(allIdle);
        Assert.Equal($"abc{IReactiveLockTrackerState.LOCK_DATA_SEPARATOR}def", lockData);
    }


    [Fact]
    public async Task SetStatusAsync_WhenBusy_CallsGrpcClientWithCorrectRequest()
    {
        // Arrange
        var clientMock = new Mock<IReactiveLockGrpcClientAdapter>();
        clientMock
            .Setup(c => c.SetStatusAsync(It.IsAny<LockStatusRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Google.Protobuf.WellKnownTypes.Empty())
            .Verifiable();

        var store = new ReactiveLockGrpcTrackerStore(clientMock.Object, ReactiveLockPollyPolicies.UseOrCreateDefaultRetryPolicy(default), "test-lock");

        // Act
        await store.SetStatusAsync("instance1", true, "mydata");

        // Assert
        clientMock.Verify(c =>
            c.SetStatusAsync(
                It.Is<LockStatusRequest>(r =>
                    r.LockKey == "test-lock" &&
                    r.InstanceId == "instance1" &&
                    r.IsBusy == true &&
                    r.LockData == "mydata"
                ),
                It.IsAny<CancellationToken>()
            ), Times.Once);
    }

    [Fact]
    public async Task SetStatusAsync_WhenIdle_CallsGrpcClientWithCorrectRequest()
    {
        // Arrange
        var clientMock = new Mock<IReactiveLockGrpcClientAdapter>();
        clientMock
            .Setup(c => c.SetStatusAsync(It.IsAny<LockStatusRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Google.Protobuf.WellKnownTypes.Empty())
            .Verifiable();

        var store = new ReactiveLockGrpcTrackerStore(clientMock.Object, ReactiveLockPollyPolicies.UseOrCreateDefaultRetryPolicy(default), "another-lock");

        // Act
        await store.SetStatusAsync("instance2", false);

        // Assert
        clientMock.Verify(c =>
            c.SetStatusAsync(
                It.Is<LockStatusRequest>(r =>
                    r.LockKey == "another-lock" &&
                    r.InstanceId == "instance2" &&
                    r.IsBusy == false &&
                    r.LockData == null
                ),
                It.IsAny<CancellationToken>()
            ), Times.Once);
    }
}