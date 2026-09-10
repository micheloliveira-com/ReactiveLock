namespace MichelOliveira.Com.ReactiveLock.Distributed.MongoDB;

/// <summary>
/// Abstracts MongoDB persistence and change-stream operations used by ReactiveLock.
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
