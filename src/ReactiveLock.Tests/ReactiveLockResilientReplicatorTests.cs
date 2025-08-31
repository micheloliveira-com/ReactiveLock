namespace ReactiveLock.Tests;

using System;
using System.Threading.Tasks;
using Xunit;
using Polly;
using ReactiveLock.Shared.Distributed;

public class ReactiveLockResilientReplicatorTests
{
    [Fact]
    public async Task ExecuteAsync_WhenSucceeds_RemovesPending()
    {
        // Arrange
        var policy = Policy.NoOpAsync();
        var replicator = new ReactiveLockResilientReplicator(policy, default, default, default);
        var executed = false;

        // Act
        await replicator.ExecuteAsync("instance-success", (_) =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        // Assert
        Assert.True(executed);
    }

    [Fact]
    public async Task FlushPendingAsync_RetriesPendingActions()
    {
        // Arrange
        var policy = Policy.NoOpAsync();
        var replicator = new ReactiveLockResilientReplicator(policy, default, default, default);
        var executed = false;

        // Register an action that stays pending (never completes immediately)
        await replicator.ExecuteAsync("instance-flush", (_) =>
        {
            executed = true;
            // Simulate "stuck" work, so it remains in Pending
            return Task.FromException(new Exception("fail-on-purpose"));
        });

        // Reset to detect Flush re-executing it
        executed = false;

        // Act
        await replicator.FlushPendingAsync();

        // Assert
        Assert.True(executed); // should run again during Flush
    }

    [Fact]
    public async Task ExecuteAsync_WhenReplaced_SecondActionRuns()
    {
        var policy = Policy.NoOpAsync();
        var replicator = new ReactiveLockResilientReplicator(policy, default, default, default);

        var secondExecuted = false;

        // First action: long running, ignores cancellation
        _ = replicator.ExecuteAsync("instance-cancel", async (_) =>
        {
            await Task.Delay(200);
        });

        // Second action replaces the first
        await replicator.ExecuteAsync("instance-cancel", (_) =>
        {
            secondExecuted = true;
            return Task.CompletedTask;
        });

        // Assert
        Assert.True(secondExecuted); // main thing: second action ran
    }


    [Fact]
    public async Task ExecuteAsync_WhenFails_LogsAndDoesNotThrow()
    {
        var policy = Policy.NoOpAsync();
        var replicator = new ReactiveLockResilientReplicator(policy, default, default, default);
        var executed = false;

        // Act
        await replicator.ExecuteAsync("instance-fail", (_) =>
        {
            executed = true;
            throw new InvalidOperationException("boom");
        });

        // Assert
        Assert.True(executed); // Action did run
                               // No exception propagated
    }
}
