namespace ReactiveLock.Shared.Distributed;

using Polly;

/// <summary>
/// Provides default Polly retry policies used across ReactiveLock for resiliency.
///
/// This utility class allows consumers to supply a custom <see cref="IAsyncPolicy"/>
/// or fall back to a built-in default policy. The default policy retries failed
/// operations indefinitely with a one-second backoff and logs retry attempts.
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
