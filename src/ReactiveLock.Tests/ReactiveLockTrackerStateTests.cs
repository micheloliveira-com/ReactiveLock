namespace ReactiveLock.Tests;

using MichelOliveira.Com.ReactiveLock.Core;
using Xunit;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

public class ReactiveLockTrackerStateTests
{
    [Fact]
    public async Task IsBlockedAsync_ShouldReturnFalse_Initially()
    {
        var tracker = new ReactiveLockTrackerState();
        var isBlocked = await tracker.IsBlockedAsync();
        Assert.False(isBlocked);
    }

    [Fact]
    public async Task SetLocalStateBlockedAsync_ShouldBlock()
    {
        var tracker = new ReactiveLockTrackerState();
        await tracker.SetLocalStateBlockedAsync();

        var isBlocked = await tracker.IsBlockedAsync();
        Assert.True(isBlocked);
    }

    [Fact]
    public async Task SetLocalStateUnblockedAsync_ShouldUnblock()
    {
        var tracker = new ReactiveLockTrackerState();
        await tracker.SetLocalStateBlockedAsync();

        await tracker.SetLocalStateUnblockedAsync();

        var isBlocked = await tracker.IsBlockedAsync();
        Assert.False(isBlocked);
    }

    [Fact]
    public async Task WaitIfBlockedAsync_ShouldWait_WhenBlocked()
    {
        var tracker = new ReactiveLockTrackerState();
        await tracker.SetLocalStateBlockedAsync();

        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            await tracker.SetLocalStateUnblockedAsync();
        });

        var wasBlocked = await tracker.WaitIfBlockedAsync();
        Assert.True(wasBlocked);
    }


    [Fact]
    public async Task WaitIfBlockedAsync_ShouldNotWait_WhenNotBlocked()
    {
        var tracker = new ReactiveLockTrackerState();

        var wasBlocked = await tracker.WaitIfBlockedAsync();
        Assert.False(wasBlocked);
    }

    [Fact]
    public async Task OnLockedHandlers_ShouldBeCalled()
    {
        bool called = false;
        Func<IServiceProvider, Task> handler = _ =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var tracker = new ReactiveLockTrackerState(
            handlerServiceProvider: null!,
            onLockedHandlers: new[] { handler }
        );

        await tracker.SetLocalStateBlockedAsync();
        await Task.Delay(100);

        Assert.True(called);
    }

    [Fact]
    public async Task OnUnlockedHandlers_ShouldBeCalled()
    {
        bool called = false;
        Func<IServiceProvider, Task> handler = _ =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var tracker = new ReactiveLockTrackerState(
            handlerServiceProvider: null!,
            onUnlockedHandlers: new[] { handler }
        );

        await tracker.SetLocalStateBlockedAsync();
        await tracker.SetLocalStateUnblockedAsync();
        await Task.Delay(100);

        Assert.True(called);
    }

    [Fact]
    public async Task WaitIfBlockedAsync_ShouldCallWhileBlockedLoop()
    {
        var tracker = new ReactiveLockTrackerState();
        await tracker.SetLocalStateBlockedAsync();

        int loopCalls = 0;
        var task = Task.Run(async () =>
        {
            await tracker.WaitIfBlockedAsync(
                whileBlockedLoopDelay: TimeSpan.FromMilliseconds(10),
                whileBlockedAsync: () =>
                {
                    loopCalls++;
                    return Task.CompletedTask;
                });
        });

        await Task.Delay(50);
        await tracker.SetLocalStateUnblockedAsync();
        await task;

        Assert.True(loopCalls > 0);
    }

    [Fact]
    public async Task GetLockDataIfBlockedAsync_ReturnsLockData_WhenBlocked()
    {
        var tracker = new ReactiveLockTrackerState();

        // Setup blocked state with some lock data (assuming SetLocalStateBlockedAsync can accept data)
        await tracker.SetLocalStateBlockedAsync("test data");

        var data = await tracker.GetLockDataIfBlockedAsync();
        Assert.Equal("test data", data);
    }

    [Fact]
    public async Task GetLockDataIfBlockedAsync_ReturnsNull_WhenNotBlocked()
    {
        var tracker = new ReactiveLockTrackerState();

        var data = await tracker.GetLockDataIfBlockedAsync();
        Assert.Null(data);
    }

}
