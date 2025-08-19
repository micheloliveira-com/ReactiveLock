namespace ReactiveLock.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Moq;
using Xunit;
using MichelOliveira.Com.ReactiveLock.Distributed.Grpc;
using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;
using ReactiveLock.Distributed.Grpc;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using System.Reflection;
using System.Collections.Concurrent;

public class ReactiveLockGrpcTrackerExtensionsTests
{
    // Helper to wrap ChannelReader into IAsyncStreamReader
    
    private class TestAsyncStreamReader<T> : IAsyncStreamReader<T>
    {
        private readonly ChannelReader<T> _reader;
        public T Current { get; private set; } = default!;

        public TestAsyncStreamReader(ChannelReader<T> reader) => _reader = reader;

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            try
            {
                while (await _reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (_reader.TryRead(out var item))
                    {
                        Current = item;
                        return true;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Respect cancellation
                return false;
            }
            return false;
        }
    }


    // Helper to mock IClientStreamWriter
    private class TestClientStreamWriter<T> : IClientStreamWriter<T>
    {
        public WriteOptions? WriteOptions { get; set; }
        public Task CompleteAsync() => Task.CompletedTask;
        public Task WriteAsync(T message) => Task.CompletedTask;
    }

    // Helper to create mock AsyncDuplexStreamingCall
    private AsyncDuplexStreamingCall<LockStatusRequest, LockStatusNotification> CreateMockDuplexCall(out Channel<LockStatusNotification> responseChannel)
    {
        responseChannel = Channel.CreateUnbounded<LockStatusNotification>();

        var reader = new TestAsyncStreamReader<LockStatusNotification>(responseChannel.Reader);
        var writer = new TestClientStreamWriter<LockStatusRequest>();

        return new AsyncDuplexStreamingCall<LockStatusRequest, LockStatusNotification>(
            writer,
            reader,
            Task.FromResult(new Metadata()),
            () => new Status(),
            () => new Metadata(),
            () => { }
        );
    }


    [Fact]
    public void AddDistributedGrpcReactiveLock_ThrowsWhenControllerAccessed()
    {
        // Arrange: set static properties
        typeof(ReactiveLockGrpcTrackerExtensions)
            .GetProperty("LocalClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, new Mock<IReactiveLockGrpcClientAdapter>().Object);

        typeof(ReactiveLockGrpcTrackerExtensions)
            .GetProperty("StoredInstanceName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, "instance-x");

        // Enqueue a pending lock to simulate uninitialized state
        var queue = (ConcurrentQueue<string>)typeof(ReactiveLockGrpcTrackerExtensions)
            .GetProperty("RegisteredLocks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;

        queue.Enqueue("lock-y");

        var services = new ServiceCollection();

        ReactiveLockConventions.RegisterFactory(services);
        services.AddDistributedGrpcReactiveLock("lock-y");

        var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<IReactiveLockTrackerFactory>();

        // Act & Assert: resolving the controller triggers the exception
        var ex = Assert.Throws<InvalidOperationException>(() =>
            factory.GetTrackerController("lock-y"));

        Assert.Contains("Distributed Grpc reactive locks are not initialized", ex.Message);

        // Cleanup
        queue.TryDequeue(out _);
    }



    [Fact]
    public void AddDistributedGrpcReactiveLock_WhenNotInitialized_Throws()
    {
        var services = new ServiceCollection();
        typeof(ReactiveLockGrpcTrackerExtensions)
            .GetProperty("LocalClient", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, null);
        typeof(ReactiveLockGrpcTrackerExtensions)
            .GetProperty("StoredInstanceName", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, null);

        Assert.Throws<InvalidOperationException>(() =>
            services.AddDistributedGrpcReactiveLock("lock-x"));
    }

    [Fact]
    public async Task UseDistributedGrpcReactiveLockAsync_ProcessesUpdates()
    {
        // Arrange
        var services = new ServiceCollection();
        services.InitializeDistributedGrpcReactiveLock("instance-x", "http://localhost:5000");
        services.AddDistributedGrpcReactiveLock("lock-x");

        // Mock state and controller
        var stateMock = new Mock<IReactiveLockTrackerState>();
        var tcsBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        stateMock
            .Setup(s => s.SetLocalStateBlockedAsync("data1"))
            .Returns(Task.CompletedTask)
            .Callback(() => tcsBlocked.TrySetResult());

        stateMock
            .Setup(s => s.SetLocalStateUnblockedAsync())
            .Returns(Task.CompletedTask);

        var controllerMock = new Mock<IReactiveLockTrackerController>();
        controllerMock.Setup(c => c.DecrementAsync(It.IsAny<int>())).Returns(Task.CompletedTask);

        // Mock factory
        var factoryMock = new Mock<IReactiveLockTrackerFactory>();
        factoryMock.Setup(f => f.GetTrackerState("lock-x")).Returns(stateMock.Object);
        factoryMock.Setup(f => f.GetTrackerController("lock-x")).Returns(controllerMock.Object);

        services.AddSingleton(factoryMock.Object);
        var provider = services.BuildServiceProvider();

        var appMock = new Mock<IApplicationBuilder>();
        appMock.Setup(a => a.ApplicationServices).Returns(provider);

        // Mock gRPC duplex call using Channels
        var duplexCall = CreateMockDuplexCall(out var responseChannel);

        var clientMock = new Mock<IReactiveLockGrpcClientAdapter>();
        clientMock
            .Setup(c => c.SubscribeLockStatus(It.IsAny<Metadata?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(duplexCall);

        // Set static properties so the method under test has valid references
        typeof(ReactiveLockGrpcTrackerExtensions)
            .GetProperty("LocalClient", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, clientMock.Object);

        typeof(ReactiveLockGrpcTrackerExtensions)
            .GetProperty("StoredInstanceName", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, "instance-x");

        // Act
        var task = ReactiveLockGrpcTrackerExtensions.UseDistributedGrpcReactiveLockAsync(appMock.Object);

        // Trigger a blocked state notification
        await responseChannel.Writer.WriteAsync(new LockStatusNotification
        {
            InstancesStatus = { { "instance-x", new InstanceLockStatus { IsBusy = true, LockData = "data1" } } }
        });

        // Complete the channel to signal no more notifications
        responseChannel.Writer.Complete();

        // Wait until the blocked callback is invoked
        await tcsBlocked.Task;

        // Wait for the main async method to finish
        await task;

        // Assert that blocked/unblocked methods were called
        stateMock.Verify(s => s.SetLocalStateBlockedAsync("data1"), Times.Once);
    }

    [Fact]
    public void ReactiveLockGrpcTrackerStore_AreAllIdleTests()
    {
        var update = new LockStatusNotification();
        var (allIdle1, lockData1) = ReactiveLockGrpcTrackerStore.AreAllIdle(update);
        Assert.True(allIdle1);
        Assert.Null(lockData1);

        update.InstancesStatus.Add("i1", new InstanceLockStatus { IsBusy = false });
        update.InstancesStatus.Add("i2", new InstanceLockStatus { IsBusy = false });
        var (allIdle2, lockData2) = ReactiveLockGrpcTrackerStore.AreAllIdle(update);
        Assert.True(allIdle2);
        Assert.Null(lockData2);

        update.InstancesStatus["i1"].IsBusy = true;
        update.InstancesStatus["i1"].LockData = "abc";
        var (allIdle3, lockData3) = ReactiveLockGrpcTrackerStore.AreAllIdle(update);
        Assert.False(allIdle3);
        Assert.Equal("abc", lockData3);

        update.InstancesStatus.Add("i3", new InstanceLockStatus { IsBusy = true, LockData = "def" });
        var (allIdle4, lockData4) = ReactiveLockGrpcTrackerStore.AreAllIdle(update);
        Assert.False(allIdle4);
        Assert.Equal($"abc{IReactiveLockTrackerState.LOCK_DATA_SEPARATOR}def", lockData4);
    }

    [Fact]
    public async Task ReactiveLockGrpcTrackerStore_SetStatusAsync_CallsClient()
    {
        var clientMock = new Mock<IReactiveLockGrpcClientAdapter>();
        clientMock.Setup(c => c.SetStatusAsync(It.IsAny<LockStatusRequest>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new Empty())
                  .Verifiable();

        var store = new ReactiveLockGrpcTrackerStore(clientMock.Object, "lock-x");
        await store.SetStatusAsync("instance-x", true, "data-x");

        clientMock.Verify(c => c.SetStatusAsync(It.Is<LockStatusRequest>(
            r => r.LockKey == "lock-x" && r.InstanceId == "instance-x" && r.IsBusy && r.LockData == "data-x"
        ), It.IsAny<CancellationToken>()), Times.Once);
    }
}
