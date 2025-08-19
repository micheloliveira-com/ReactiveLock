namespace MichelOliveira.Com.ReactiveLock.Distributed.Grpc;

using global::Grpc.Core;
using global::ReactiveLock.Distributed.Grpc;
using Google.Protobuf.WellKnownTypes;

/// <summary>
/// Defines an abstraction for gRPC-based communication with the ReactiveLock service.
///
/// Provides methods to subscribe to distributed lock status notifications
/// and to update lock status across remote application instances using gRPC.
/// This interface allows the ReactiveLock system to depend on a stable contract,
/// while enabling flexible implementations and easier testing (via mocks or wrappers).
///
/// <para>
/// ⚠️ Notice: This file is part of the ReactiveLock library and is licensed under the MIT License.
/// You must follow license, preserve the copyright notice, and comply with all legal terms
/// when using any part of this software.
/// See the LICENSE file in the project root for full license details.
/// © Michel Oliveira
/// </para>
/// </summary>
public interface IReactiveLockGrpcClientAdapter
{
    AsyncDuplexStreamingCall<LockStatusRequest, LockStatusNotification> SubscribeLockStatus(
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default);

    Task<Empty> SetStatusAsync(LockStatusRequest request, CancellationToken cancellationToken = default);
}