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
        var now = DateTimeOffset.UtcNow;
        var update = new LockStatusNotification();

        // inst1 busy and valid
        update.InstancesStatus.Add("inst1", new InstanceLockStatus
        {
            IsBusy = true,
            LockData = "abc",
            ValidUntil = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(now.AddSeconds(10))
        });

        // inst2 idle
        update.InstancesStatus.Add("inst2", new InstanceLockStatus
        {
            IsBusy = false,
            ValidUntil = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(now.AddSeconds(10))
        });

        var (allIdle, lockData) = ReactiveLockGrpcTrackerStore.AreAllIdle(update);

        Assert.False(allIdle);
        Assert.Equal("abc", lockData);

        // Make inst1 expired
        update.InstancesStatus["inst1"].ValidUntil = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(now.AddSeconds(-10));

        var (allIdleExpired, lockDataExpired) = ReactiveLockGrpcTrackerStore.AreAllIdle(update);

        Assert.True(allIdleExpired);      // all locks are either idle or expired
        Assert.Null(lockDataExpired);      // no active lock data remains
    }


    [Fact]
    public void AreAllIdle_MultipleBusy_ReturnsCombinedLockData()
    {
        var now = DateTimeOffset.UtcNow;
        var update = new LockStatusNotification();

        // inst1 busy and valid
        update.InstancesStatus.Add("inst1", new InstanceLockStatus
        {
            IsBusy = true,
            LockData = "abc",
            ValidUntil = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(now.AddSeconds(10)) // valid
        });

        // inst2 busy and valid
        update.InstancesStatus.Add("inst2", new InstanceLockStatus
        {
            IsBusy = true,
            LockData = "def",
            ValidUntil = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(now.AddSeconds(10)) // valid
        });

        var (allIdle, lockData) = ReactiveLockGrpcTrackerStore.AreAllIdle(update);

        Assert.False(allIdle);
        Assert.Equal($"abc{IReactiveLockTrackerState.LOCK_DATA_SEPARATOR}def", lockData);

        // Make inst2 expired
        update.InstancesStatus["inst2"].ValidUntil = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(now.AddSeconds(-10));

        var (allIdleExpired, lockDataExpired) = ReactiveLockGrpcTrackerStore.AreAllIdle(update);

        Assert.False(allIdleExpired);
        Assert.Equal("abc", lockDataExpired); // only inst1 counts
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

        var clients = new List<IReactiveLockGrpcClientAdapter> { clientMock.Object };

        var store = new ReactiveLockGrpcTrackerStore(
            clients,
            "instance1",
            ReactiveLockPollyPolicies.UseOrCreateDefaultRetryPolicy(default),
            default,
            "test-lock"
        );

        // Act
        await store.SetStatusAsync(true, "mydata");

        // Assert
        clientMock.Verify(c =>
            c.SetStatusAsync(
                It.Is<LockStatusRequest>(r =>
                    r.LockKey == "test-lock" &&
                    r.InstanceId == "instance1" &&   // <- note: in your code, this is *not prefixed*
                    r.IsBusy == true &&
                    r.LockData == "mydata"
                ),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
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

        var clients = new List<IReactiveLockGrpcClientAdapter> { clientMock.Object };

        var store = new ReactiveLockGrpcTrackerStore(
            clients,
            "instance2",
            ReactiveLockPollyPolicies.UseOrCreateDefaultRetryPolicy(default),
            default,
            "another-lock"
        );

        // Act
        await store.SetStatusAsync(false);

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
            ),
            Times.Once
        );
    }

}