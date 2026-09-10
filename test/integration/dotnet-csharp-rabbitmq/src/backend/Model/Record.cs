
using System.Text.Json.Serialization;

public sealed record ProcessorPaymentRequest(
    decimal Amount,
    DateTimeOffset RequestedAt,
    Guid CorrelationId
);
public record PaymentInsertParameters(
    Guid CorrelationId,
    string Processor,
    decimal Amount,
    DateTimeOffset RequestedAt
);

public record PaymentSummaryResult(
    string Processor,
    long TotalRequests,
    decimal TotalAmount
);
public record PaymentSummary(
    long TotalRequests,
    decimal TotalAmount
);

public record PaymentSummaryResponse(
    PaymentSummary Default,
    PaymentSummary Fallback
);

public record PaymentRequest(
    Guid CorrelationId,
    decimal Amount
);

public sealed record PaymentReplicationMessage(string Kind, PaymentInsertParameters? Payment)
{
    public const string PaymentKind = "payment";
    public const string PurgeKind = "purge";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProcessorPaymentRequest))]
[JsonSerializable(typeof(PaymentRequest))]
[JsonSerializable(typeof(PaymentSummaryResponse))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(PaymentInsertParameters))]
[JsonSerializable(typeof(PaymentReplicationMessage))]
internal partial class JsonContext : JsonSerializerContext
{

}
