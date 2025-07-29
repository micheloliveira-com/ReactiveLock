namespace ReactiveLock.Tests;

using MichelOliveira.Com.ReactiveLock.Core;
using Xunit;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;

public class ReactiveLockTrackerControllerTests
{
    [Fact]
    public async Task IncrementAsync_WithState_CallsBlocked()
    {
        var mockState = new MockState();
        var controller = new ReactiveLockTrackerController(mockState);

        await controller.IncrementAsync();

        Assert.True(mockState.BlockedCalled);
        Assert.False(mockState.UnblockedCalled);
    }

    [Fact]
    public async Task DecrementAsync_WithState_CallsUnblocked()
    {
        var mockState = new MockState();
        var controller = new ReactiveLockTrackerController(mockState);

        await controller.IncrementAsync();
        await controller.DecrementAsync();

        Assert.True(mockState.UnblockedCalled);
    }

    [Fact]
    public async Task MultipleIncrements_OnlyFirstBlocks()
    {
        var mockState = new MockState();
        var controller = new ReactiveLockTrackerController(mockState);

        await controller.IncrementAsync();
        await controller.IncrementAsync();
        await controller.IncrementAsync();

        Assert.Equal(1, mockState.BlockedCount);
    }

    [Fact]
    public async Task MultipleDecrements_OnlyLastUnblocks()
    {
        var mockState = new MockState();
        var controller = new ReactiveLockTrackerController(mockState);

        await controller.IncrementAsync();
        await controller.IncrementAsync();
        await controller.DecrementAsync();
        Assert.False(mockState.UnblockedCalled);

        await controller.DecrementAsync();
        Assert.True(mockState.UnblockedCalled);
    }

    [Fact]
    public async Task Increment_Decrement_StoreIsCalled()
    {
        var mockStore = new MockStore();
        var controller = new ReactiveLockTrackerController(mockStore, "node1");

        await controller.IncrementAsync();
        await controller.DecrementAsync();

        Assert.Equal(new[] { true, false }, mockStore.Calls);
        Assert.Equal("node1", mockStore.LastInstanceName);
    }

    private class MockState : IReactiveLockTrackerState
    {
        public bool BlockedCalled { get; private set; }
        public bool UnblockedCalled { get; private set; }
        public int BlockedCount { get; private set; }

        public Task SetLocalStateBlockedAsync()
        {
            BlockedCalled = true;
            BlockedCount++;
            return Task.CompletedTask;
        }

        public Task SetLocalStateUnblockedAsync()
        {
            UnblockedCalled = true;
            return Task.CompletedTask;
        }

        public Task<bool> IsBlockedAsync() => Task.FromResult(BlockedCalled && !UnblockedCalled);
        public Task<bool> WaitIfBlockedAsync(Func<Task>? onBlockedAsync = null, TimeSpan? whileBlockedLoopDelay = null, Func<Task>? whileBlockedAsync = null) => Task.FromResult(false);
    }

    private class MockStore : IReactiveLockTrackerStore
    {
        public List<bool> Calls { get; } = new();
        public string? LastInstanceName { get; private set; }

        public Task SetStatusAsync(string instanceName, bool isBlocked)
        {
            Calls.Add(isBlocked);
            LastInstanceName = instanceName;
            return Task.CompletedTask;
        }
    }
}
