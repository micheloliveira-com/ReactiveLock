namespace ReactiveLock.Tests;

using MichelOliveira.Com.ReactiveLock.Distributed.MongoDB;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;

public class ReactiveLockMongoDbClientAdapterTests
{
    private const string DatabaseName = "ReactiveLockTests";
    private const string CollectionName = "LockStatus";

    [Fact]
    public async Task EnsureInfrastructureAsync_CreatesLookupAndExpirationIndexes()
    {
        var fixture = new AdapterFixture();
        var models = new List<CreateIndexModel<ReactiveLockMongoDbDocument>>();
        fixture.Indexes
            .Setup(indexes => indexes.CreateManyAsync(
                It.IsAny<IEnumerable<CreateIndexModel<ReactiveLockMongoDbDocument>>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<CreateIndexModel<ReactiveLockMongoDbDocument>>, CancellationToken>(
                (created, _) => models.AddRange(created))
            .ReturnsAsync(["reactivelock_active_lookup", "reactivelock_expiration_ttl"]);

        await fixture.Adapter.EnsureInfrastructureAsync();

        Assert.Collection(
            models,
            lookup =>
            {
                Assert.Equal("reactivelock_active_lookup", lookup.Options.Name);
                Assert.Null(lookup.Options.ExpireAfter);
            },
            expiration =>
            {
                Assert.Equal("reactivelock_expiration_ttl", expiration.Options.Name);
                Assert.Equal(TimeSpan.Zero, expiration.Options.ExpireAfter);
            });
    }

    [Fact]
    public async Task UpsertStatusAsync_ReplacesDocumentByIdWithUpsertEnabled()
    {
        var fixture = new AdapterFixture();
        FilterDefinition<ReactiveLockMongoDbDocument>? capturedFilter = null;
        ReactiveLockMongoDbDocument? capturedDocument = null;
        ReplaceOptions? capturedOptions = null;
        fixture.Collection
            .Setup(collection => collection.ReplaceOneAsync(
                It.IsAny<FilterDefinition<ReactiveLockMongoDbDocument>>(),
                It.IsAny<ReactiveLockMongoDbDocument>(),
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<ReactiveLockMongoDbDocument>, ReactiveLockMongoDbDocument, ReplaceOptions, CancellationToken>(
                (filter, document, options, _) =>
                {
                    capturedFilter = filter;
                    capturedDocument = document;
                    capturedOptions = options;
                })
            .ReturnsAsync(Mock.Of<ReplaceOneResult>());
        var document = CreateDocument("orders", "instance-1", true);

        await fixture.Adapter.UpsertStatusAsync(document);

        Assert.Same(document, capturedDocument);
        Assert.True(capturedOptions!.IsUpsert);
        var bsonFilter = Assert.IsType<BsonDocumentFilterDefinition<ReactiveLockMongoDbDocument>>(capturedFilter);
        Assert.Equal(document.Id, bsonFilter.Document["_id"].AsString);
    }

    [Fact]
    public async Task GetActiveBusyInstancesAsync_UsesLockBusyAndExpirationFilter()
    {
        var fixture = new AdapterFixture();
        var expected = new[] { CreateDocument("orders", "instance-1", true) };
        var cursor = CreateCursor(expected);
        FilterDefinition<ReactiveLockMongoDbDocument>? capturedFilter = null;
        fixture.Collection
            .Setup(collection => collection.FindAsync(
                It.IsAny<FilterDefinition<ReactiveLockMongoDbDocument>>(),
                It.IsAny<FindOptions<ReactiveLockMongoDbDocument, ReactiveLockMongoDbDocument>>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<ReactiveLockMongoDbDocument>, FindOptions<ReactiveLockMongoDbDocument, ReactiveLockMongoDbDocument>, CancellationToken>(
                (filter, _, _) => capturedFilter = filter)
            .ReturnsAsync(cursor.Object);
        var now = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

        var result = await fixture.Adapter.GetActiveBusyInstancesAsync("orders", now);

        Assert.Same(expected[0], Assert.Single(result));
        var document = Assert.IsType<BsonDocumentFilterDefinition<ReactiveLockMongoDbDocument>>(capturedFilter).Document;
        Assert.Equal("orders", document[nameof(ReactiveLockMongoDbDocument.LockKey)].AsString);
        Assert.True(document[nameof(ReactiveLockMongoDbDocument.IsBusy)].AsBoolean);
        Assert.Equal(
            now.UtcDateTime,
            document[nameof(ReactiveLockMongoDbDocument.ValidUntilUtc)]["$gt"].ToUniversalTime());
    }

    [Fact]
    public async Task WatchAsync_DispatchesFullDocumentsAndDeleteDocumentKeysForWatchedLocks()
    {
        var fixture = new AdapterFixture();
        var watchedDocument = CreateDocument("orders", "instance-1", true);
        var ignoredDocument = CreateDocument("payments", "instance-1", true);
        var changes = new[]
        {
            CreateChange("replace", watchedDocument, watchedDocument.Id),
            CreateChange("replace", ignoredDocument, ignoredDocument.Id),
            CreateChange("delete", null, ReactiveLockMongoDbDocument.CreateId("orders", "instance-2")),
            CreateChange("delete", null, new BsonInt32(42))
        };
        var cursor = new Mock<IChangeStreamCursor<ChangeStreamDocument<ReactiveLockMongoDbDocument>>>();
        cursor.SetupGet(value => value.Current).Returns(changes);
        cursor.SetupSequence(value => value.MoveNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);
        fixture.Collection
            .Setup(collection => collection.WatchAsync(
                It.IsAny<PipelineDefinition<ChangeStreamDocument<ReactiveLockMongoDbDocument>, ChangeStreamDocument<ReactiveLockMongoDbDocument>>>(),
                It.IsAny<ChangeStreamOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cursor.Object);
        var ready = false;
        var changedLocks = new List<string>();

        await fixture.Adapter.WatchAsync(
            new HashSet<string>(StringComparer.Ordinal) { "orders" },
            lockKey =>
            {
                changedLocks.Add(lockKey);
                return Task.CompletedTask;
            },
            () => ready = true);

        Assert.True(ready);
        Assert.Equal(["orders", "orders"], changedLocks);
        fixture.Collection.Verify(collection => collection.WatchAsync(
            It.IsAny<PipelineDefinition<ChangeStreamDocument<ReactiveLockMongoDbDocument>, ChangeStreamDocument<ReactiveLockMongoDbDocument>>>(),
            It.Is<ChangeStreamOptions>(options =>
                options.FullDocument == ChangeStreamFullDocumentOption.UpdateLookup
                && options.MaxAwaitTime == TimeSpan.FromSeconds(1)),
            It.IsAny<CancellationToken>()), Times.Once);
        cursor.Verify(value => value.Dispose(), Times.Once);
    }

    private static Mock<IAsyncCursor<ReactiveLockMongoDbDocument>> CreateCursor(
        IEnumerable<ReactiveLockMongoDbDocument> documents)
    {
        var cursor = new Mock<IAsyncCursor<ReactiveLockMongoDbDocument>>();
        cursor.SetupGet(value => value.Current).Returns(documents);
        cursor.SetupSequence(value => value.MoveNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);
        return cursor;
    }

    private static ChangeStreamDocument<ReactiveLockMongoDbDocument> CreateChange(
        string operationType,
        ReactiveLockMongoDbDocument? fullDocument,
        BsonValue documentId)
    {
        var bson = new BsonDocument
        {
            { "_id", new BsonDocument("_data", Guid.NewGuid().ToString("N")) },
            { "operationType", operationType },
            { "clusterTime", new BsonTimestamp(1) },
            { "ns", new BsonDocument { { "db", DatabaseName }, { "coll", CollectionName } } },
            { "documentKey", new BsonDocument("_id", documentId) }
        };
        if (fullDocument is not null)
            bson.Add("fullDocument", fullDocument.ToBsonDocument());

        return new ChangeStreamDocument<ReactiveLockMongoDbDocument>(
            bson,
            BsonSerializer.LookupSerializer<ReactiveLockMongoDbDocument>());
    }

    private static ReactiveLockMongoDbDocument CreateDocument(
        string lockKey,
        string instanceId,
        bool isBusy) => new()
    {
        Id = ReactiveLockMongoDbDocument.CreateId(lockKey, instanceId),
        LockKey = lockKey,
        InstanceId = instanceId,
        IsBusy = isBusy,
        ValidUntilUtc = DateTime.UtcNow.AddMinutes(1),
        Revision = 1
    };

    private sealed class AdapterFixture
    {
        public Mock<IMongoCollection<ReactiveLockMongoDbDocument>> Collection { get; } = new();
        public Mock<IMongoIndexManager<ReactiveLockMongoDbDocument>> Indexes { get; } = new();
        public ReactiveLockMongoDbClientAdapter Adapter { get; }

        public AdapterFixture()
        {
            var client = new Mock<IMongoClient>();
            var database = new Mock<IMongoDatabase>();
            client.Setup(value => value.GetDatabase(DatabaseName, It.IsAny<MongoDatabaseSettings>()))
                .Returns(database.Object);
            database.Setup(value => value.GetCollection<ReactiveLockMongoDbDocument>(
                    CollectionName,
                    It.IsAny<MongoCollectionSettings>()))
                .Returns(Collection.Object);
            Collection.Setup(value => value.WithWriteConcern(It.IsAny<WriteConcern>()))
                .Returns(Collection.Object);
            Collection.SetupGet(value => value.Indexes).Returns(Indexes.Object);

            Adapter = new ReactiveLockMongoDbClientAdapter(client.Object, DatabaseName, CollectionName);

            Collection.Verify(value => value.WithWriteConcern(
                It.Is<WriteConcern>(writeConcern => writeConcern.Equals(WriteConcern.WMajority))),
                Times.Once);
        }
    }
}
