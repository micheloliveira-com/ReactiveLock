namespace MichelOliveira.Com.ReactiveLock.Distributed.Grpc;

using global::Grpc.Core;
using global::ReactiveLock.Distributed.Grpc;
using Google.Protobuf.WellKnownTypes;

/// <summary>
/// Provides a concrete adapter around the generated gRPC <see cref="ReactiveLockGrpc.ReactiveLockGrpcClient"/>.
/// 
/// This adapter implements <see cref="IReactiveLockGrpcClientAdapter"/> and exposes
/// strongly-typed methods for subscribing to lock status updates and sending lock status
/// changes to the distributed ReactiveLock service.
/// 
/// By using this adapter, the ReactiveLock system can abstract away the raw gRPC client,
/// allowing for cleaner architecture, easier dependency injection, and simplified unit testing
/// (since the adapter can be replaced with a mock or fake).
/// 
/// <para>
/// ⚠️ Notice: This file is part of the ReactiveLock library and is licensed under the MIT License.
/// You must follow license, preserve the copyright notice, and comply with all legal terms
/// when using any part of this software.
/// See the LICENSE file in the project root for full license details.
/// © Michel Oliveira
/// </para>
/// </summary>
public class ReactiveLockGrpcClientAdapter : IReactiveLockGrpcClientAdapter
{
    private ReactiveLockGrpc.ReactiveLockGrpcClient ReactiveLockGrpcClient { get; }

    public ReactiveLockGrpcClientAdapter(ReactiveLockGrpc.ReactiveLockGrpcClient inner)
    {
        ReactiveLockGrpcClient = inner;
    }

    public AsyncDuplexStreamingCall<LockStatusRequest, LockStatusNotification> SubscribeLockStatus(
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
        => ReactiveLockGrpcClient.SubscribeLockStatus(headers, deadline, cancellationToken);

    public async Task<Empty> SetStatusAsync(LockStatusRequest request, CancellationToken cancellationToken = default)
    {
        var call = ReactiveLockGrpcClient.SetStatusAsync(request, cancellationToken: cancellationToken);
        return await call.ResponseAsync.ConfigureAwait(false);
    }
}
