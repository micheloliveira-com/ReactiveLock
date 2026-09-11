namespace MichelOliveira.Com.ReactiveLock.Distributed.MongoDB;

using System.Collections.Concurrent;

/// <summary>
/// Holds temporary bootstrap state for MongoDB-backed ReactiveLock extensions.
///
/// This type retains the application instance name, initialization status, and
/// locally registered lock keys only until change-stream initialization completes.
/// Distributed busy and idle state is stored in MongoDB, not in this object.
///
/// <para>
/// ⚠️ Notice: This file is part of the ReactiveLock library and is licensed under the MIT License.
/// You must follow the license, preserve the copyright notice, and comply with all legal terms
/// when using any part of this software.
/// See the LICENSE file in the project root for full license details.
/// © Michel Oliveira
/// </para>
/// </summary>
internal sealed class ReactiveLockMongoDbTrackerExtensionsState(string instanceName)
{
    public string InstanceName { get; } = instanceName;
    public bool IsInitializing { get; set; }
    public ConcurrentQueue<string> RegisteredLocks { get; } = new();
}
