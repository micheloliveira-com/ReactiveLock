namespace MichelOliveira.Com.ReactiveLock.Distributed.MongoDB;

using global::MongoDB.Bson;
using global::MongoDB.Driver;

/// <summary>
/// Native MongoDB driver implementation for lock documents and change streams.
/// </summary>
public sealed class ReactiveLockMongoDbClientAdapter : IReactiveLockMongoDbClientAdapter
{
    private readonly IMongoCollection<ReactiveLockMongoDbDocument> _collection;

    public ReactiveLockMongoDbClientAdapter(
        IMongoClient mongoClient,
        string databaseName,
        string collectionName)
    {
        _collection = mongoClient
            .GetDatabase(databaseName)
            .GetCollection<ReactiveLockMongoDbDocument>(collectionName)
            .WithWriteConcern(WriteConcern.WMajority);
    }

    public async Task EnsureInfrastructureAsync(CancellationToken cancellationToken = default)
    {
        var activeLookup = new CreateIndexModel<ReactiveLockMongoDbDocument>(
            Builders<ReactiveLockMongoDbDocument>.IndexKeys
                .Ascending(document => document.LockKey)
                .Ascending(document => document.IsBusy)
                .Ascending(document => document.ValidUntilUtc),
            new CreateIndexOptions { Name = "reactivelock_active_lookup" });

        var expiration = new CreateIndexModel<ReactiveLockMongoDbDocument>(
            Builders<ReactiveLockMongoDbDocument>.IndexKeys
                .Ascending(document => document.ValidUntilUtc),
            new CreateIndexOptions
            {
                Name = "reactivelock_expiration_ttl",
                ExpireAfter = TimeSpan.Zero
            });

        await _collection.Indexes.CreateManyAsync(
            [activeLookup, expiration],
            cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertStatusAsync(
        ReactiveLockMongoDbDocument document,
        CancellationToken cancellationToken = default)
    {
        await _collection.ReplaceOneAsync(
            existing => existing.Id == document.Id,
            document,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ReactiveLockMongoDbDocument>> GetActiveBusyInstancesAsync(
        string lockKey,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        return await _collection
            .Find(document => document.LockKey == lockKey
                              && document.IsBusy
                              && document.ValidUntilUtc > now.UtcDateTime)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task WatchAsync(
        IReadOnlySet<string> lockKeys,
        Func<string, Task> onLockChanged,
        Action onReady,
        CancellationToken cancellationToken = default)
    {
        var options = new ChangeStreamOptions
        {
            FullDocument = ChangeStreamFullDocumentOption.UpdateLookup,
            MaxAwaitTime = TimeSpan.FromSeconds(1)
        };

        using var cursor = await _collection
            .WatchAsync(options: options, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        onReady();

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var change in cursor.Current)
            {
                var lockKey = change.FullDocument?.LockKey;
                if (lockKey is null
                    && change.DocumentKey is not null
                    && change.DocumentKey.TryGetValue("_id", out var idValue)
                    && idValue.IsString)
                {
                    ReactiveLockMongoDbDocument.TryGetLockKeyFromId(
                        idValue.AsString,
                        out lockKey);
                }

                if (lockKey is not null && lockKeys.Contains(lockKey))
                    await onLockChanged(lockKey).ConfigureAwait(false);
            }
        }
    }
}
