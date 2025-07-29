namespace ReactiveLock.Tests;

using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

public class ReactiveLockConventionsTests
{
    [Fact]
    public void GetControllerKey_ReturnsExpectedFormat()
    {
        var key = ReactiveLockConventions.GetControllerKey("abc");
        Assert.Equal("ReactiveLock:Controller:abc", key);
    }

    [Fact]
    public void GetStateKey_ReturnsExpectedFormat()
    {
        var key = ReactiveLockConventions.GetStateKey("abc");
        Assert.Equal("ReactiveLock:State:abc", key);
    }

    [Fact]
    public void RegisterFactory_AddsFactorySingleton()
    {
        var services = new ServiceCollection();
        ReactiveLockConventions.RegisterFactory(services);

        var provider = services.BuildServiceProvider();
        var factory = provider.GetService<IReactiveLockTrackerFactory>();

        Assert.NotNull(factory);
        Assert.IsType<ReactiveLockTrackerFactory>(factory);
    }

    [Fact]
    public void RegisterState_RegistersKeyedStateInstance()
    {
        var services = new ServiceCollection();
        var lockKey = "abc";

        ReactiveLockConventions.RegisterState(services, lockKey);

        var provider = services.BuildServiceProvider();
        var state = provider.GetKeyedService<IReactiveLockTrackerState>(
            ReactiveLockConventions.GetStateKey(lockKey)
        );

        Assert.NotNull(state);
        Assert.IsType<ReactiveLockTrackerState>(state);
    }

    [Fact]
    public async Task RegisterState_PassesHandlers()
    {
        var called = false;

        var services = new ServiceCollection();
        var lockKey = "custom";
        var handlers = new List<Func<IServiceProvider, Task>>
        {
            _ => {
                called = true;
                return Task.CompletedTask;
            }
        };

        ReactiveLockConventions.RegisterState(services, lockKey,
            onLockedHandlers: handlers
        );

        var provider = services.BuildServiceProvider();
        var state = (ReactiveLockTrackerState)provider.GetKeyedService<IReactiveLockTrackerState>(
            ReactiveLockConventions.GetStateKey(lockKey)
        )!;

        await state.SetLocalStateBlockedAsync();
        await Task.Delay(50); // wait for background handler execution

        Assert.True(called);
    }

    [Fact]
    public void RegisterController_RegistersControllerCorrectly()
    {
        var services = new ServiceCollection();
        var lockKey = "ctrl";
        var testController = new DummyController();

        ReactiveLockConventions.RegisterController(services, lockKey, sp => testController);

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetKeyedService<IReactiveLockTrackerController>(
            ReactiveLockConventions.GetControllerKey(lockKey)
        );

        Assert.Same(testController, resolved);
    }

    private class DummyController : IReactiveLockTrackerController
    {
        public Task DecrementAsync(int amount = 1) => Task.CompletedTask;
        public Task IncrementAsync() => Task.CompletedTask;
    }
}
