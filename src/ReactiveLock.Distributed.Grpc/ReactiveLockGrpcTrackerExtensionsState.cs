namespace MichelOliveira.Com.ReactiveLock.Distributed.Grpc;

using System.Collections.Concurrent;

/// <summary>
/// Holds internal bootstrap and runtime state for gRPC-based ReactiveLock extensions.
///
/// This class encapsulates mutable state required during the initialization and
/// lifecycle of distributed gRPC reactive locks, including:
/// <list type="bullet">
///   <item><description>The current instance identifier.</description></item>
///   <item><description>Initialization lifecycle tracking.</description></item>
///   <item><description>Registered distributed lock keys.</description></item>
///   <item><description>Configured remote gRPC client adapters.</description></item>
/// </list>
///
/// This type is intended for internal use only and is not part of the public API surface.
/// </summary>
/// 
/// <para>
/// ⚠️ Notice: This file is part of the ReactiveLock library and is licensed under the MIT License.
/// You must follow license, preserve the copyright notice, and comply with all legal terms
/// when using any part of this software.
/// See the LICENSE file in the project root for full license details.
/// © Michel Oliveira
/// </para>
internal sealed class ReactiveLockGrpcTrackerExtensionsState(string instanceName)
{
    public string InstanceName { get; } = instanceName;
    public bool IsInitializing { get; set; }

    public ConcurrentQueue<string> RegisteredLocks { get; } = new();
    public List<IReactiveLockGrpcClientAdapter> RemoteClients { get; } = new();
}
