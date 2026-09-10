using MongoDB.Driver;

public sealed class MongoDbIntegrationStore
{
    private readonly IMongoCollection<WorkItemDocument> _workItems;
    private readonly IMongoCollection<PaymentDocument> _payments;

    public MongoDbIntegrationStore(IMongoClient mongoClient)
    {
        var database = mongoClient.GetDatabase(Constant.INTEGRATION_DATABASE);
        _workItems = database.GetCollection<WorkItemDocument>(Constant.WORK_COLLECTION);
        _payments = database.GetCollection<PaymentDocument>(Constant.PAYMENTS_COLLECTION);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var workIndex = new CreateIndexModel<WorkItemDocument>(
            Builders<WorkItemDocument>.IndexKeys.Ascending(item => item.CreatedAtUtc),
            new CreateIndexOptions { Name = "work_created_at" });
        var paymentsIndex = new CreateIndexModel<PaymentDocument>(
            Builders<PaymentDocument>.IndexKeys.Ascending(payment => payment.RequestedAtUtc),
            new CreateIndexOptions { Name = "payment_requested_at" });

        await _workItems.Indexes.CreateOneAsync(workIndex, cancellationToken: cancellationToken);
        await _payments.Indexes.CreateOneAsync(paymentsIndex, cancellationToken: cancellationToken);
    }

    public Task EnqueueWorkAsync(string body, CancellationToken cancellationToken = default) =>
        _workItems.InsertOneAsync(new WorkItemDocument { Body = body }, cancellationToken: cancellationToken);

    public async Task<string?> DequeueWorkAsync(CancellationToken cancellationToken = default)
    {
        var item = await _workItems.FindOneAndDeleteAsync(
            Builders<WorkItemDocument>.Filter.Empty,
            new FindOneAndDeleteOptions<WorkItemDocument>
            {
                Sort = Builders<WorkItemDocument>.Sort.Ascending(work => work.CreatedAtUtc)
            },
            cancellationToken);
        return item?.Body;
    }

    public Task InsertPaymentAsync(
        PaymentInsertParameters payment,
        CancellationToken cancellationToken = default) =>
        _payments.InsertOneAsync(PaymentDocument.FromPayment(payment), cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<PaymentInsertParameters>> GetPaymentsAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<PaymentDocument>.Filter.Empty;
        if (from.HasValue)
            filter &= Builders<PaymentDocument>.Filter.Gte(payment => payment.RequestedAtUtc, from.Value.UtcDateTime);
        if (to.HasValue)
            filter &= Builders<PaymentDocument>.Filter.Lte(payment => payment.RequestedAtUtc, to.Value.UtcDateTime);

        var documents = await _payments.Find(filter).ToListAsync(cancellationToken);
        return documents.Select(document => document.ToPayment()).ToArray();
    }

    public async Task PurgePaymentsAsync(CancellationToken cancellationToken = default)
    {
        await _payments.DeleteManyAsync(
            Builders<PaymentDocument>.Filter.Empty,
            cancellationToken);
    }
}
