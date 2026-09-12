using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using ReactiveLock.Integration.Shared;

public sealed class WorkItemDocument
{
    [BsonId]
    public ObjectId Id { get; init; } = ObjectId.GenerateNewId();
    public required string Body { get; init; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class PaymentDocument
{
    [BsonId]
    public ObjectId Id { get; init; } = ObjectId.GenerateNewId();

    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public required Guid CorrelationId { get; init; }
    public required string Processor { get; init; }
    public decimal Amount { get; init; }
    public DateTime RequestedAtUtc { get; init; }

    public PaymentInsertParameters ToPayment() =>
        new(CorrelationId, Processor, Amount, new DateTimeOffset(RequestedAtUtc, TimeSpan.Zero));

    public static PaymentDocument FromPayment(PaymentInsertParameters payment) => new()
    {
        CorrelationId = payment.CorrelationId,
        Processor = payment.Processor,
        Amount = payment.Amount,
        RequestedAtUtc = payment.RequestedAt.UtcDateTime
    };
}
