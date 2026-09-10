namespace MichelOliveira.Com.ReactiveLock.Distributed.RabbitMQ;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Message exchanged between RabbitMQ-backed ReactiveLock instances.
/// </summary>
public sealed record ReactiveLockRabbitMqMessage(
    string Kind,
    string LockKey,
    string InstanceId,
    bool IsBusy,
    string? LockData,
    long ValidUntilUtcTicks,
    long Revision)
{
    public const string StatusKind = "status";
    public const string SnapshotRequestKind = "snapshot-request";

    public DateTimeOffset ValidUntil => new(ValidUntilUtcTicks, TimeSpan.Zero);

    public static ReactiveLockRabbitMqMessage CreateSnapshotRequest(string lockKey, string instanceId) =>
        new(SnapshotRequestKind, lockKey, instanceId, false, null, 0, 0);

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(
        this,
        ReactiveLockRabbitMqJsonContext.Default.ReactiveLockRabbitMqMessage);

    public static ReactiveLockRabbitMqMessage? Deserialize(ReadOnlySpan<byte> body) =>
        JsonSerializer.Deserialize(
            body,
            ReactiveLockRabbitMqJsonContext.Default.ReactiveLockRabbitMqMessage);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ReactiveLockRabbitMqMessage))]
internal sealed partial class ReactiveLockRabbitMqJsonContext : JsonSerializerContext;
