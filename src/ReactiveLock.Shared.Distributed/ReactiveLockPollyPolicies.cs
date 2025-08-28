namespace ReactiveLock.Shared.Distributed;

using Polly;
/// <summary>
/// Implements a Redis-based tracker for distributed reactive locks.
///
/// Provides methods to check if all tracked locks are idle and to set/update
/// the status of a lock instance in Redis. Supports storing optional lock metadata
/// and notifying subscribers of status changes.
/// 
/// <para>
/// ⚠️ Notice: This file is part of the ReactiveLock library and is licensed under the MIT License.
/// You must follow license, preserve the copyright notice, and comply with all legal terms
/// when using any part of this software.
/// See the LICENSE file in the project root for full license details.
/// © Michel Oliveira
/// </para>
/// </summary>
public static class ReactiveLockPollyPolicies
{

    public static IAsyncPolicy UseOrCreateDefaultRetryPolicy(IAsyncPolicy? customAsyncPolicy)
    {
        if (customAsyncPolicy != default)
        {
            return customAsyncPolicy;
        }
        return Policy
            .Handle<Exception>()
            .WaitAndRetryForeverAsync(
                _ => TimeSpan.FromSeconds(1),
                (ex, ts) =>
                {
                    Console.WriteLine($"[ReactiveLock] Retry due to {ex.GetType().Name}: {ex.Message}. Waiting {ts.TotalSeconds}s...");
                });
    }


}
