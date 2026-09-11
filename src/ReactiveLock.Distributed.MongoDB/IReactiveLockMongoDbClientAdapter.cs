namespace MichelOliveira.Com.ReactiveLock.Distributed.MongoDB;

/// <summary>
/// Abstracts MongoDB persistence and change-stream operations used by ReactiveLock.
///
/// This adapter boundary keeps the tracker store independent from the native MongoDB
/// driver implementation and allows the persistence layer to be replaced in tests.
///
/// <para>
/// ⚠️ Notice: This file is part of the ReactiveLock library and is licensed under the MIT License.
/// You must follow the license, preserve the copyright notice, and comply with all legal terms
/// when using any part of this software.
/// See the LICENSE file in the project root for full license details.
/// © Michel Oliveira
/// </para>
/// </summary>
public interface IReactiveLockMongoDbClientAdapter
{
    Task EnsureInfrastructureAsync(CancellationToken cancellationToken = default);

    Task UpsertStatusAsync(
        ReactiveLockMongoDbDocument document,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReactiveLockMongoDbDocument>> GetActiveBusyInstancesAsync(
        string lockKey,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task WatchAsync(
        IReadOnlySet<string> lockKeys,
        Func<string, Task> onLockChanged,
        Action onReady,
        CancellationToken cancellationToken = default);
}
