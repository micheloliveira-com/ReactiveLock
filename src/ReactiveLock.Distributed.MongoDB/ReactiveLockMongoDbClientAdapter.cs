namespace MichelOliveira.Com.ReactiveLock.Distributed.MongoDB;

using global::MongoDB.Bson;
using global::MongoDB.Bson.Serialization;
using global::MongoDB.Driver;

/// <summary>
/// Native MongoDB driver implementation for lock documents and change streams.
///
/// Creates the indexes required for active lease lookup and expiration, persists
/// per-instance state with majority write concern, and dispatches collection changes
/// to the locally registered ReactiveLock trackers.
///
/// <para>
/// ⚠️ Notice: This file is part of the ReactiveLock library and is licensed under the MIT License.
/// You must follow the license, preserve the copyright notice, and comply with all legal terms
/// when using any part of this software.
/// See the LICENSE file in the project root for full license details.
/// © Michel Oliveira
/// </para>
/// </summary>
public sealed class ReactiveLockMongoDbClientAdapter : IReactiveLockMongoDbClientAdapter
{
    private IMongoCollection<ReactiveLockMongoDbDocument> MongoDbCollection { get; }

    public ReactiveLockMongoDbClientAdapter(
        IMongoClient mongoClient,
        string databaseName,
        string collectionName)
    {
        BsonSerializer.TryRegisterSerializer(ReactiveLockMongoDbDocumentSerializer.Instance);
        MongoDbCollection = mongoClient
            .GetDatabase(databaseName)
            .GetCollection<ReactiveLockMongoDbDocument>(collectionName)
            .WithWriteConcern(WriteConcern.WMajority);
    }

    public async Task EnsureInfrastructureAsync(CancellationToken cancellationToken = default)
    {
        var activeLookup = new CreateIndexModel<ReactiveLockMongoDbDocument>(
            Builders<ReactiveLockMongoDbDocument>.IndexKeys
                .Ascending(nameof(ReactiveLockMongoDbDocument.LockKey))
                .Ascending(nameof(ReactiveLockMongoDbDocument.IsBusy))
                .Ascending(nameof(ReactiveLockMongoDbDocument.ValidUntilUtc)),
            new CreateIndexOptions { Name = "reactivelock_active_lookup" });

        var expiration = new CreateIndexModel<ReactiveLockMongoDbDocument>(
            Builders<ReactiveLockMongoDbDocument>.IndexKeys
                .Ascending(nameof(ReactiveLockMongoDbDocument.ValidUntilUtc)),
            new CreateIndexOptions
            {
                Name = "reactivelock_expiration_ttl",
                ExpireAfter = TimeSpan.Zero
            });

        await MongoDbCollection.Indexes.CreateManyAsync(
            [activeLookup, expiration],
            cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertStatusAsync(
        ReactiveLockMongoDbDocument document,
        CancellationToken cancellationToken = default)
    {
        await MongoDbCollection.ReplaceOneAsync(
            new BsonDocument("_id", document.Id),
            document,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ReactiveLockMongoDbDocument>> GetActiveBusyInstancesAsync(
        string lockKey,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        FilterDefinition<ReactiveLockMongoDbDocument> filter = new BsonDocument
        {
            { nameof(ReactiveLockMongoDbDocument.LockKey), lockKey },
            { nameof(ReactiveLockMongoDbDocument.IsBusy), true },
            {
                nameof(ReactiveLockMongoDbDocument.ValidUntilUtc),
                new BsonDocument("$gt", new BsonDateTime(now.UtcDateTime))
            }
        };

        return await MongoDbCollection
            .Find(filter)
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

        using var cursor = await MongoDbCollection
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
