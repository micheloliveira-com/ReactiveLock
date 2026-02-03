namespace MichelOliveira.Com.ReactiveLock.Distributed.Redis;

using System.Collections.Concurrent;

/// <summary>
/// Holds internal bootstrap and runtime state for Redis-based ReactiveLock extensions.
///
/// This class encapsulates mutable state required during the initialization and
/// lifecycle of distributed Redis reactive locks, including:
/// <list type="bullet">
///   <item><description>The current instance identifier.</description></item>
///   <item><description>Initialization lifecycle tracking.</description></item>
///   <item><description>Registered distributed lock metadata.</description></item>
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
internal sealed class ReactiveLockRedisTrackerExtensionsState(string instanceName)
{
    public string InstanceName { get; } = instanceName;
    public bool IsInitializing { get; set; }

    public ConcurrentQueue<(
        string lockKey, 
        string redisHashSetKey, 
        string redisHashSetNotifierSubscriptionKey)> RegisteredLocks { get; } = new();
}