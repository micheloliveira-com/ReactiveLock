namespace ReactiveLock.Tests;

using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System;

public class ReactiveLockTrackerFactoryTests
{
    [Fact]
    public void GetTrackerState_ReturnsExpectedInstance()
    {
        var services = new ServiceCollection();
        var lockKey = "test-lock";

        var expectedState = new DummyState();
        services.AddKeyedSingleton<IReactiveLockTrackerState>(ReactiveLockConventions.GetStateKey(lockKey), expectedState);
        services.AddSingleton<ReactiveLockTrackerFactory>();

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ReactiveLockTrackerFactory>();

        var resolved = factory.GetTrackerState(lockKey);

        Assert.Same(expectedState, resolved);
    }

    [Fact]
    public void GetTrackerController_ReturnsExpectedInstance()
    {
        var services = new ServiceCollection();
        var lockKey = "ctrl-lock";

        var expectedController = new DummyController();
        services.AddKeyedSingleton<IReactiveLockTrackerController>(ReactiveLockConventions.GetControllerKey(lockKey), expectedController);
        services.AddSingleton<ReactiveLockTrackerFactory>();

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ReactiveLockTrackerFactory>();

        var resolved = factory.GetTrackerController(lockKey);

        Assert.Same(expectedController, resolved);
    }

    [Fact]
    public void GetTrackerState_Throws_WhenNotFound()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ReactiveLockTrackerFactory>();
        var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<ReactiveLockTrackerFactory>();

        var ex = Assert.Throws<InvalidOperationException>(() => factory.GetTrackerState("missing-lock"));
        Assert.Contains("No state keyed service provider", ex.Message);
    }

    [Fact]
    public void GetTrackerController_Throws_WhenNotFound()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ReactiveLockTrackerFactory>();
        var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<ReactiveLockTrackerFactory>();

        var ex = Assert.Throws<InvalidOperationException>(() => factory.GetTrackerController("missing-lock"));
        Assert.Contains("No controller keyed service provider", ex.Message);
    }
    private class DummyState : IReactiveLockTrackerState
    {
        public Task<string[]> GetLockDataEntriesIfBlockedAsync() => Task.FromResult(Array.Empty<string>());
        public Task<string?> GetLockDataIfBlockedAsync() => Task.FromResult<string?>(null);
        public static Task<bool> IsBlockedAsync() => Task.FromResult(false);

        public Task<bool> WaitIfBlockedAsync(
            Func<Task>? onBlockedAsync = null,
            TimeSpan? whileBlockedLoopDelay = null,
            Func<Task>? whileBlockedAsync = null) =>
            Task.FromResult(false);

        public Task SetLocalStateBlockedAsync(string? lockData = null) => Task.CompletedTask;

        public Task SetLocalStateUnblockedAsync() => Task.CompletedTask;
    }

    private class DummyController : IReactiveLockTrackerController
    {
        public Task IncrementAsync() => Task.CompletedTask;
        public Task DecrementAsync(int amount = 1) => Task.CompletedTask;
    }
}
