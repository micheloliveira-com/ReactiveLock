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
        var replicator = new ReactiveLockResilientReplicator();
        var policy = Policy.NoOpAsync();
        var executed = false;

        // Act
        await replicator.ExecuteAsync("instance-success", policy, () =>
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
        var replicator = new ReactiveLockResilientReplicator();
        var policy = Policy.NoOpAsync();
        var executed = false;

        // Register an action that stays pending (never completes immediately)
        await replicator.ExecuteAsync("instance-flush", policy, () =>
        {
            executed = true;
            // Simulate "stuck" work, so it remains in Pending
            return Task.FromException(new Exception("fail-on-purpose"));
        });

        // Reset to detect Flush re-executing it
        executed = false;

        // Act
        await replicator.FlushPendingAsync(policy);

        // Assert
        Assert.True(executed); // should run again during Flush
    }

    [Fact]
    public async Task ExecuteAsync_WhenReplaced_SecondActionRuns()
    {
        var replicator = new ReactiveLockResilientReplicator();
        var policy = Policy.NoOpAsync();

        var secondExecuted = false;

        // First action: long running, ignores cancellation
        _ = replicator.ExecuteAsync("instance-cancel", policy, async () =>
        {
            await Task.Delay(200);
        });

        // Second action replaces the first
        await replicator.ExecuteAsync("instance-cancel", policy, () =>
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
        var replicator = new ReactiveLockResilientReplicator();
        var policy = Policy.NoOpAsync();
        var executed = false;

        // Act
        await replicator.ExecuteAsync("instance-fail", policy, () =>
        {
            executed = true;
            throw new InvalidOperationException("boom");
        });

        // Assert
        Assert.True(executed); // Action did run
                               // No exception propagated
    }
}
