using System.Text.Json.Serialization;

namespace ReactiveLock.Integration.Shared;

public sealed record ProcessorPaymentRequest(
    decimal Amount,
    DateTimeOffset RequestedAt,
    Guid CorrelationId);

public sealed record PaymentInsertParameters(
    Guid CorrelationId,
    string Processor,
    decimal Amount,
    DateTimeOffset RequestedAt);

public sealed record PaymentSummaryResult(
    string Processor,
    long TotalRequests,
    decimal TotalAmount);

public sealed record PaymentSummary(long TotalRequests, decimal TotalAmount);

public sealed record PaymentSummaryResponse(PaymentSummary Default, PaymentSummary Fallback);

public sealed record PaymentRequest(Guid CorrelationId, decimal Amount);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProcessorPaymentRequest))]
[JsonSerializable(typeof(PaymentRequest))]
[JsonSerializable(typeof(PaymentSummaryResponse))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(PaymentInsertParameters))]
public partial class IntegrationJsonContext : JsonSerializerContext;
