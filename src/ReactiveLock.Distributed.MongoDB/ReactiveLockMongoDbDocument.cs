namespace MichelOliveira.Com.ReactiveLock.Distributed.MongoDB;

using global::MongoDB.Bson.Serialization.Attributes;
using System.Text;

/// <summary>
/// Persistent state of one ReactiveLock instance for one lock key.
///
/// The deterministic identifier isolates the state of each application instance and
/// logical lock, while the validity timestamp provides renewable lease expiration.
///
/// <para>
/// ⚠️ Notice: This file is part of the ReactiveLock library and is licensed under the MIT License.
/// You must follow the license, preserve the copyright notice, and comply with all legal terms
/// when using any part of this software.
/// See the LICENSE file in the project root for full license details.
/// © Michel Oliveira
/// </para>
/// </summary>
public sealed class ReactiveLockMongoDbDocument
{
    private const char IdSeparator = '.';

    [BsonId]
    public required string Id { get; init; }

    public required string LockKey { get; init; }
    public required string InstanceId { get; init; }
    public bool IsBusy { get; init; }
    public string? LockData { get; init; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime ValidUntilUtc { get; init; }

    public long Revision { get; init; }

    public static string CreateId(string lockKey, string instanceId) =>
        $"{Encode(lockKey)}{IdSeparator}{Encode(instanceId)}";

    public static bool TryGetLockKeyFromId(string? id, out string lockKey)
    {
        lockKey = string.Empty;
        if (string.IsNullOrEmpty(id))
            return false;

        var separatorIndex = id.IndexOf(IdSeparator);
        if (separatorIndex <= 0)
            return false;

        try
        {
            lockKey = Encoding.UTF8.GetString(Convert.FromBase64String(
                RestoreBase64Padding(id[..separatorIndex])));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string RestoreBase64Padding(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');
        return value.PadRight(value.Length + ((4 - value.Length % 4) % 4), '=');
    }
}
