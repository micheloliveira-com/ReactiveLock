namespace ReactiveLock.Tests;

using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Grpc.Core;
using Google.Protobuf.WellKnownTypes;
using MichelOliveira.Com.ReactiveLock.Distributed.Grpc;
using Moq;
using ReactiveLock.Distributed.Grpc;
using Xunit;

public class ReactiveLockGrpcClientAdapterTests
{
    // Helper for mocking AsyncDuplexStreamingCall
    private static AsyncDuplexStreamingCall<LockStatusRequest, LockStatusNotification> CreateMockDuplexCall(out Channel<LockStatusNotification> channel)
    {
        channel = Channel.CreateUnbounded<LockStatusNotification>();
        var reader = new AsyncStreamReaderMock<LockStatusNotification>(channel.Reader);
        var writer = new ClientStreamWriterMock<LockStatusRequest>();
        return new AsyncDuplexStreamingCall<LockStatusRequest, LockStatusNotification>(
            writer, reader,
            Task.FromResult(new Metadata()),
            () => new Status(),
            () => new Metadata(),
            () => { });
    }

    private class AsyncStreamReaderMock<T> : IAsyncStreamReader<T>
    {
        private readonly ChannelReader<T> _reader;
        public T Current { get; private set; } = default!;
        public AsyncStreamReaderMock(ChannelReader<T> reader) => _reader = reader;
        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (await _reader.WaitToReadAsync(cancellationToken))
            {
                if (_reader.TryRead(out var item))
                {
                    Current = item;
                    return true;
                }
            }
            return false;
        }
    }

    private class ClientStreamWriterMock<T> : IClientStreamWriter<T>
    {
        public WriteOptions? WriteOptions { get; set; }
        public Task CompleteAsync() => Task.CompletedTask;
        public Task WriteAsync(T message) => Task.CompletedTask;
    }

    [Fact]
    public void SubscribeLockStatus_ReturnsDuplexCall()
    {
        // Arrange
        var grpcClientMock = new Mock<ReactiveLockGrpc.ReactiveLockGrpcClient>(MockBehavior.Strict)
        {
            CallBase = false
        };

        var duplexCall = CreateMockDuplexCall(out _);

        grpcClientMock
            .Setup(c => c.SubscribeLockStatus(It.IsAny<Metadata?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(duplexCall);

        var adapter = new ReactiveLockGrpcClientAdapter(grpcClientMock.Object);

        // Act
        var result = adapter.SubscribeLockStatus();

        // Assert
        Assert.NotNull(result);
        grpcClientMock.Verify(c => c.SubscribeLockStatus(It.IsAny<Metadata?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetStatusAsync_CallsGrpcClientAndReturnsResponse()
    {
        // Arrange
        var grpcClientMock = new Mock<ReactiveLockGrpc.ReactiveLockGrpcClient>(MockBehavior.Strict)
        {
            CallBase = false
        };

        var expectedResponse = new Empty();
        var asyncUnaryCall = new AsyncUnaryCall<Empty>(
            Task.FromResult(expectedResponse),
            Task.FromResult(new Metadata()),
            () => new Status(),
            () => new Metadata(),
            () => { });

        // Match the exact overload used by the adapter
        grpcClientMock
            .Setup(c => c.SetStatusAsync(
                It.IsAny<LockStatusRequest>(),
                It.IsAny<Metadata?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns(asyncUnaryCall);

        var adapter = new ReactiveLockGrpcClientAdapter(grpcClientMock.Object);

        // Act
        var result = await adapter.SetStatusAsync(new LockStatusRequest());

        // Assert
        Assert.NotNull(result);
        Assert.IsType<Empty>(result);
        grpcClientMock.Verify(c => c.SetStatusAsync(
            It.IsAny<LockStatusRequest>(),
            It.IsAny<Metadata?>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }
}
