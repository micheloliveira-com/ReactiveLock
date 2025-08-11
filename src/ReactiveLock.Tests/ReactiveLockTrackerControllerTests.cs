namespace ReactiveLock.Tests;

using MichelOliveira.Com.ReactiveLock.Core;
using Xunit;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;

public class ReactiveLockTrackerControllerTests
{
    private static readonly bool[] ExpectedIncrementDecrementCalls = [true, false];
    private static readonly bool[] ExpectedIncrementCalls = [true];
    private static readonly bool[] ExpectedDecrementCalls = [false];

    [Fact]
    public async Task IncrementAsync_FirstCall_CallsStoreWithBlocked()
    {
        var mockStore = new MockStore();
        var controller = new ReactiveLockTrackerController(mockStore, "test-instance");

        await controller.IncrementAsync();

        Assert.Equal(ExpectedIncrementCalls, mockStore.Calls);
        Assert.Equal("test-instance", mockStore.LastInstanceName);
    }

    [Fact]
    public async Task IncrementAsync_MultipleCalls_OnlyFirstCallsStore()
    {
        var mockStore = new MockStore();
        var controller = new ReactiveLockTrackerController(mockStore, "test-instance");

        await controller.IncrementAsync();
        await controller.IncrementAsync();
        await controller.IncrementAsync();

        Assert.Single(mockStore.Calls);
        Assert.True(mockStore.Calls[0]);
    }

    [Fact]
    public async Task DecrementAsync_MultipleIncrements_OnlyLastCallsUnblocked()
    {
        var mockStore = new MockStore();
        var controller = new ReactiveLockTrackerController(mockStore, "test-instance");

        await controller.IncrementAsync();
        await controller.IncrementAsync();
        await controller.DecrementAsync();

        // Should not unblock yet
        Assert.Single(mockStore.Calls);

        await controller.DecrementAsync();

        // Now should unblock
        Assert.Equal(2, mockStore.Calls.Count);
        Assert.False(mockStore.Calls[1]);
    }

    [Fact]
    public async Task DecrementAsync_NegativeCount_DoesNotCrash()
    {
        var mockStore = new MockStore();
        var controller = new ReactiveLockTrackerController(mockStore, "test-instance");

        await controller.DecrementAsync();

        Assert.Equal(ExpectedDecrementCalls, mockStore.Calls);
    }

    [Fact]
    public async Task Increment_Decrement_StoreReceivesExpectedSequence()
    {
        var mockStore = new MockStore();
        var controller = new ReactiveLockTrackerController(mockStore, "node1");

        await controller.IncrementAsync();
        await controller.DecrementAsync();

        Assert.Equal(ExpectedIncrementDecrementCalls, mockStore.Calls);
        Assert.Equal("node1", mockStore.LastInstanceName);
    }

    [Fact]
    public async Task Constructor_WithoutInstanceName_UsesDefaultInstanceName()
    {
        var mockStore = new MockStore();
        var controller = new ReactiveLockTrackerController(mockStore);

        await controller.IncrementAsync();

        Assert.Equal([true], mockStore.Calls);
        Assert.Equal("default", mockStore.LastInstanceName);
    }

    [Fact]
    public void Constructor_WithNullStore_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ReactiveLockTrackerController(null!));
    }

    [Fact]
    public void Constructor_WithNullInstanceName_ThrowsArgumentNullException()
    {
        var mockStore = new MockStore();
        Assert.Throws<ArgumentNullException>(() => new ReactiveLockTrackerController(mockStore, null!));
    }

    [Fact]
    public void Constructor_WithNullStoreAndInstanceName_ThrowsArgumentNullExceptionOnStore()
    {
        // Order matters: store is checked before instanceName
        Assert.Throws<ArgumentNullException>(() => new ReactiveLockTrackerController(null!, null!));
    }
    private class MockStore : IReactiveLockTrackerStore
    {
        public List<bool> Calls { get; } = new();
        public string? LastInstanceName { get; private set; }
        public string? LastLockData { get; private set; }

        public Task SetStatusAsync(string instanceName, bool isBlocked, string? lockData = null)
        {
            Calls.Add(isBlocked);
            LastInstanceName = instanceName;
            LastLockData = lockData;
            return Task.CompletedTask;
        }
    }
}
