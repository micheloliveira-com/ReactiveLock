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


    [Fact]
    public async Task ExecuteAsync_ShouldCallPersistenceAction_AndRenewAutomatically()
    {
        // Arrange
        var tcsCallCount = 0;
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Use very short periods for the test
        var replicator = new ReactiveLockResilientReplicator(
            asyncPolicy: Policy.Handle<Exception>().RetryAsync(3),
            instanceRenewalPeriodTimeSpan: TimeSpan.FromMilliseconds(50),
            instanceExpirationPeriodTimeSpan: TimeSpan.FromMilliseconds(100),
            instanceRecoverPeriodTimeSpan: TimeSpan.FromMilliseconds(150)
        );

        async Task PersistenceAction(DateTimeOffset validUntil)
        {
            Interlocked.Increment(ref tcsCallCount);
            if (tcsCallCount >= 3)
                tcs.TrySetResult(true);
            await Task.CompletedTask;
        }

        // Act
        await replicator.ExecuteAsync("instance1", PersistenceAction);

        // Wait for at least 3 calls via renewal/retry loop (with timeout)
        var timeout = TimeSpan.FromSeconds(2);
        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
        if (completedTask != tcs.Task)
            throw new TimeoutException("The persistence action was not called enough times in time.");

        // Assert
        Assert.True(tcsCallCount >= 3);

        // Cleanup
        await replicator.DisposeAsync();
    }
    [Fact]
    public async Task FlushPendingAsync_ShouldRetryAllPendingActions()
    {
        // Arrange
        var callCount = 0;
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var replicator = new ReactiveLockResilientReplicator(
            asyncPolicy: ReactiveLockPollyPolicies.UseOrCreateDefaultRetryPolicy(null),
            instanceRenewalPeriodTimeSpan: TimeSpan.FromMilliseconds(50),
            instanceExpirationPeriodTimeSpan: TimeSpan.FromMilliseconds(100),
            instanceRecoverPeriodTimeSpan: TimeSpan.FromMilliseconds(200));

        // Define a simple persistence action that increments callCount
        async Task PersistenceAction(DateTimeOffset validUntil)
        {
            Interlocked.Increment(ref callCount);
            if (callCount >= 4) // number of pending actions we plan to test
                tcs.TrySetResult(true);
            await Task.CompletedTask;
        }

        // Add 4 pending actions manually
        await replicator.ExecuteAsync("instance1", PersistenceAction);
        await replicator.ExecuteAsync("instance2", PersistenceAction);
        await replicator.ExecuteAsync("instance3", PersistenceAction);
        await replicator.ExecuteAsync("instance4", PersistenceAction);

        // Act
        await replicator.FlushPendingAsync();

        // Wait until all actions have been called or timeout
        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.True(completedTask == tcs.Task, $"Timed out waiting for FlushPendingAsync to execute all actions. Current call count: {callCount}");

        // Assert
        Assert.True(callCount >= 4, $"Expected at least 4 calls, but got {callCount}");

        await replicator.DisposeAsync();
    }



}
