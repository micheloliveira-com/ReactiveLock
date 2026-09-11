namespace MichelOliveira.Com.ReactiveLock.Distributed.MongoDB;

using global::MongoDB.Bson;
using global::MongoDB.Bson.IO;
using global::MongoDB.Bson.Serialization;
using global::MongoDB.Bson.Serialization.IdGenerators;
using global::MongoDB.Bson.Serialization.Serializers;

/// <summary>
/// Serializes lock documents without reflection so the provider works in Native AOT applications.
///
/// The explicit BSON reader and writer avoid runtime member discovery and preserve the
/// provider's document contract in trimmed and Native AOT applications.
///
/// <para>
/// ⚠️ Notice: This file is part of the ReactiveLock library and is licensed under the MIT License.
/// You must follow the license, preserve the copyright notice, and comply with all legal terms
/// when using any part of this software.
/// See the LICENSE file in the project root for full license details.
/// © Michel Oliveira
/// </para>
/// </summary>
internal sealed class ReactiveLockMongoDbDocumentSerializer : SerializerBase<ReactiveLockMongoDbDocument>, IBsonIdProvider
{
    public static ReactiveLockMongoDbDocumentSerializer Instance { get; } = new();

    public override ReactiveLockMongoDbDocument Deserialize(
        BsonDeserializationContext context,
        BsonDeserializationArgs args)
    {
        var reader = context.Reader;
        string? id = null;
        string? lockKey = null;
        string? instanceId = null;
        var isBusy = false;
        string? lockData = null;
        var validUntilUtc = default(DateTime);
        var revision = 0L;

        reader.ReadStartDocument();
        while (true)
        {
            var bsonType = reader.ReadBsonType();
            if (bsonType == BsonType.EndOfDocument)
                break;

            var elementName = reader.ReadName(Utf8NameDecoder.Instance);
            switch (elementName)
            {
                case "_id":
                    id = reader.ReadString();
                    break;
                case nameof(ReactiveLockMongoDbDocument.LockKey):
                    lockKey = reader.ReadString();
                    break;
                case nameof(ReactiveLockMongoDbDocument.InstanceId):
                    instanceId = reader.ReadString();
                    break;
                case nameof(ReactiveLockMongoDbDocument.IsBusy):
                    isBusy = reader.ReadBoolean();
                    break;
                case nameof(ReactiveLockMongoDbDocument.LockData):
                    if (bsonType == BsonType.Null)
                        reader.ReadNull();
                    else
                        lockData = reader.ReadString();
                    break;
                case nameof(ReactiveLockMongoDbDocument.ValidUntilUtc):
                    validUntilUtc = BsonUtils.ToDateTimeFromMillisecondsSinceEpoch(reader.ReadDateTime());
                    break;
                case nameof(ReactiveLockMongoDbDocument.Revision):
                    revision = reader.ReadInt64();
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }
        reader.ReadEndDocument();

        return new ReactiveLockMongoDbDocument
        {
            Id = RequireValue(id, "_id"),
            LockKey = RequireValue(lockKey, nameof(ReactiveLockMongoDbDocument.LockKey)),
            InstanceId = RequireValue(instanceId, nameof(ReactiveLockMongoDbDocument.InstanceId)),
            IsBusy = isBusy,
            LockData = lockData,
            ValidUntilUtc = validUntilUtc,
            Revision = revision
        };
    }

    private static string RequireValue(string? value, string fieldName)
    {
        if (value is null)
            throw new BsonSerializationException($"ReactiveLock document is missing {fieldName}.");

        return value;
    }

    public override void Serialize(
        BsonSerializationContext context,
        BsonSerializationArgs args,
        ReactiveLockMongoDbDocument value)
    {
        var writer = context.Writer;
        writer.WriteStartDocument();
        writer.WriteName("_id");
        writer.WriteString(value.Id);
        writer.WriteName(nameof(ReactiveLockMongoDbDocument.LockKey));
        writer.WriteString(value.LockKey);
        writer.WriteName(nameof(ReactiveLockMongoDbDocument.InstanceId));
        writer.WriteString(value.InstanceId);
        writer.WriteName(nameof(ReactiveLockMongoDbDocument.IsBusy));
        writer.WriteBoolean(value.IsBusy);
        writer.WriteName(nameof(ReactiveLockMongoDbDocument.LockData));
        if (value.LockData is null)
            writer.WriteNull();
        else
            writer.WriteString(value.LockData);
        writer.WriteName(nameof(ReactiveLockMongoDbDocument.ValidUntilUtc));
        writer.WriteDateTime(BsonUtils.ToMillisecondsSinceEpoch(value.ValidUntilUtc));
        writer.WriteName(nameof(ReactiveLockMongoDbDocument.Revision));
        writer.WriteInt64(value.Revision);
        writer.WriteEndDocument();
    }

    public bool GetDocumentId(
        object document,
        out object id,
        out Type idNominalType,
        out IIdGenerator idGenerator)
    {
        id = ((ReactiveLockMongoDbDocument)document).Id;
        idNominalType = typeof(string);
        idGenerator = StringObjectIdGenerator.Instance;
        return true;
    }

    public void SetDocumentId(object document, object id) =>
        throw new NotSupportedException("ReactiveLock MongoDB document IDs are deterministic and immutable.");
}
