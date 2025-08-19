namespace ReactiveLock.Tests;

using System.Threading.Tasks;
using Xunit;
using Grpc.Core;
using ReactiveLock.Distributed.Grpc;
using MichelOliveira.Com.ReactiveLock.Distributed.Grpc;
using static ReactiveLock.Distributed.Grpc.ReactiveLockGrpc;
using MichelOliveira.Com.ReactiveLock.Core;

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
}