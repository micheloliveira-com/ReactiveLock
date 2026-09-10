namespace ReactiveLock.Tests;

using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.Distributed.MongoDB;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using System.Collections.Concurrent;

public class ReactiveLockMongoDbTrackerStoreTests
{
    [Fact]
    public async Task SetStatusAsync_UpsertsBusyDocumentWithLeaseAndLockData()
    {
        var mongoDb = new FakeMongoDbClientAdapter();
        var store = new ReactiveLockMongoDbTrackerStore(
            mongoDb,
            "instance-1",
            null,
            (default, TimeSpan.FromSeconds(30), default),
            "orders");

        var before = DateTime.UtcNow;
        await store.SetStatusAsync(true, "order-42");

        var document = Assert.Single(mongoDb.Documents.Values);
        Assert.Equal("orders", document.LockKey);
        Assert.Equal("instance-1", document.InstanceId);
        Assert.True(document.IsBusy);
        Assert.Equal("order-42", document.LockData);
        Assert.True(document.ValidUntilUtc > before.AddSeconds(25));
        Assert.True(document.Revision > 0);
    }

    [Fact]
    public async Task SetStatusAsync_ReplacesExistingInstanceState()
    {
        var mongoDb = new FakeMongoDbClientAdapter();
        var store = new ReactiveLockMongoDbTrackerStore(
            mongoDb, "instance-1", null, default, "orders");

        await store.SetStatusAsync(true, "data");
        var busyRevision = Assert.Single(mongoDb.Documents.Values).Revision;
        await store.SetStatusAsync(false);

        var document = Assert.Single(mongoDb.Documents.Values);
        Assert.False(document.IsBusy);
        Assert.Null(document.LockData);
        Assert.True(document.Revision > busyRevision);
    }

    [Fact]
    public async Task AreAllIdleAsync_ReturnsTrueWhenNoActiveBusyDocuments()
    {
        var mongoDb = new FakeMongoDbClientAdapter();
        mongoDb.Documents["expired"] = Document(
            "expired", true, "ignored", DateTime.UtcNow.AddSeconds(-1));

        var result = await ReactiveLockMongoDbTrackerStore.AreAllIdleAsync("orders", mongoDb);

        Assert.True(result.allIdle);
        Assert.Null(result.lockData);
    }

    [Fact]
    public async Task AreAllIdleAsync_CombinesDataFromActiveBusyDocuments()
    {
        var mongoDb = new FakeMongoDbClientAdapter();
        mongoDb.Documents["one"] = Document("one", true, "first", DateTime.UtcNow.AddMinutes(1));
        mongoDb.Documents["two"] = Document("two", false, "ignored", DateTime.UtcNow.AddMinutes(1));
        mongoDb.Documents["three"] = Document("three", true, "second", DateTime.UtcNow.AddMinutes(1));

        var result = await ReactiveLockMongoDbTrackerStore.AreAllIdleAsync("orders", mongoDb);

        Assert.False(result.allIdle);
        Assert.Equal($"first{IReactiveLockTrackerState.LOCK_DATA_SEPARATOR}second", result.lockData);
    }

    [Theory]
    [InlineData("orders", "instance-1")]
    [InlineData("lock.with.dots/and unicode-ç", "backend/α")]
    public void DocumentId_RoundTripsLockKey(string lockKey, string instanceId)
    {
        var id = ReactiveLockMongoDbDocument.CreateId(lockKey, instanceId);

        Assert.True(ReactiveLockMongoDbDocument.TryGetLockKeyFromId(id, out var parsed));
        Assert.Equal(lockKey, parsed);
    }

    [Fact]
    public void NativeAdapter_RegistersReflectionFreeDocumentSerializer()
    {
        _ = new ReactiveLockMongoDbClientAdapter(
            new MongoClient("mongodb://localhost:27017/?directConnection=true"),
            "ReactiveLockTests",
            "LockStatus");
        var expected = Document(
            "instance-1",
            true,
            "payload",
            DateTime.UtcNow.AddMinutes(1));

        var bson = expected.ToBson();
        var actual = BsonSerializer.Deserialize<ReactiveLockMongoDbDocument>(bson);

        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.LockKey, actual.LockKey);
        Assert.Equal(expected.InstanceId, actual.InstanceId);
        Assert.Equal(expected.IsBusy, actual.IsBusy);
        Assert.Equal(expected.LockData, actual.LockData);
        Assert.Equal(expected.ValidUntilUtc, actual.ValidUntilUtc, TimeSpan.FromMilliseconds(1));
        Assert.Equal(expected.Revision, actual.Revision);
    }

    private static ReactiveLockMongoDbDocument Document(
        string instanceId,
        bool busy,
        string? lockData,
        DateTime validUntilUtc) => new()
    {
        Id = ReactiveLockMongoDbDocument.CreateId("orders", instanceId),
        LockKey = "orders",
        InstanceId = instanceId,
        IsBusy = busy,
        LockData = lockData,
        ValidUntilUtc = validUntilUtc,
        Revision = 1
    };

    internal sealed class FakeMongoDbClientAdapter : IReactiveLockMongoDbClientAdapter
    {
        private Func<string, Task>? _onLockChanged;

        public ConcurrentDictionary<string, ReactiveLockMongoDbDocument> Documents { get; } = [];
        public bool InfrastructureEnsured { get; private set; }
        public IReadOnlySet<string>? WatchedLockKeys { get; private set; }

        public Task EnsureInfrastructureAsync(CancellationToken cancellationToken = default)
        {
            InfrastructureEnsured = true;
            return Task.CompletedTask;
        }

        public async Task UpsertStatusAsync(
            ReactiveLockMongoDbDocument document,
            CancellationToken cancellationToken = default)
        {
            Documents[document.Id] = document;
            if (_onLockChanged is not null)
                await _onLockChanged(document.LockKey);
        }

        public Task<IReadOnlyList<ReactiveLockMongoDbDocument>> GetActiveBusyInstancesAsync(
            string lockKey,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ReactiveLockMongoDbDocument> result = Documents.Values
                .Where(document => document.LockKey == lockKey
                                   && document.IsBusy
                                   && document.ValidUntilUtc > now.UtcDateTime)
                .ToArray();
            return Task.FromResult(result);
        }

        public Task WatchAsync(
            IReadOnlySet<string> lockKeys,
            Func<string, Task> onLockChanged,
            Action onReady,
            CancellationToken cancellationToken = default)
        {
            WatchedLockKeys = lockKeys;
            _onLockChanged = onLockChanged;
            onReady();
            return Task.CompletedTask;
        }
    }
}
