namespace ReactiveLock.Tests;

using MichelOliveira.Com.ReactiveLock.Core;
using Moq;
using System.Threading.Tasks;
using Xunit;

public class InMemoryReactiveLockTrackerStoreTests
{
    [Fact]
    public async Task SetStatusAsync_WhenIsBusy_CallsSetLocalStateBlockedAsync()
    {
        var stateMock = new Mock<IReactiveLockTrackerState>();
        stateMock
            .Setup(s => s.SetLocalStateBlockedAsync(It.IsAny<string?>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var store = new InMemoryReactiveLockTrackerStore(stateMock.Object);

        await store.SetStatusAsync(true);

        stateMock.Verify(s => s.SetLocalStateBlockedAsync(It.IsAny<string?>()), Times.Once);
        stateMock.Verify(s => s.SetLocalStateUnblockedAsync(), Times.Never);
    }

    [Fact]
    public async Task SetStatusAsync_WhenNotBusy_CallsSetLocalStateUnblockedAsync()
    {
        var stateMock = new Mock<IReactiveLockTrackerState>();
        stateMock
            .Setup(s => s.SetLocalStateUnblockedAsync())
            .Returns(Task.CompletedTask)
            .Verifiable();

        var store = new InMemoryReactiveLockTrackerStore(stateMock.Object);

        await store.SetStatusAsync(false);

        stateMock.Verify(s => s.SetLocalStateUnblockedAsync(), Times.Once);
        stateMock.Verify(s => s.SetLocalStateBlockedAsync(It.IsAny<string?>()), Times.Never);
    }
}
