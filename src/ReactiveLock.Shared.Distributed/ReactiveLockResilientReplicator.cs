namespace ReactiveLock.Shared.Distributed;

using Polly;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Provides resiliency for distributed lock status replication.
/// Each instanceName keeps only the latest persistence attempt.
/// Old retries are canceled if a new state arrives.
/// 
/// <para>
/// ⚠️ Notice: This file is part of the ReactiveLock library and is licensed under the MIT License.
/// You must follow license, preserve the copyright notice, and comply with all legal terms
/// when using any part of this software.
/// See the LICENSE file in the project root for full license details.
/// © Michel Oliveira
/// </para>
/// </summary>
public class ReactiveLockResilientReplicator : IAsyncDisposable
{
    private CancellationTokenSource Cancellation { get; } = new();
    private Task RenewalTask { get; }

    private TimeSpan InstanceRenewalPeriodTimeSpan { get; set; }
    private TimeSpan InstanceExpirationPeriodTimeSpan { get; set; }

    private ConcurrentDictionary<string, (Func<TimeSpan, Task> action, CancellationTokenSource cts)> Pending { get; } = new();
    private ConcurrentDictionary<string, Func<TimeSpan, Task>> Current { get; } = new();

    public ReactiveLockResilientReplicator(
        TimeSpan instanceRenewalPeriodTimeSpan,
        TimeSpan instanceExpirationPeriodTimeSpan)
    {
        InstanceRenewalPeriodTimeSpan = instanceRenewalPeriodTimeSpan;
        InstanceExpirationPeriodTimeSpan = instanceExpirationPeriodTimeSpan;

        // Start renewal loop in background
        RenewalTask = Task.Run(() => RenewalLoopAsync(Cancellation.Token));
    }

    /// <summary>
    /// Executes the provided persistence action with resiliency.
    /// Uses Polly for retries. If a new update for the same instance arrives,
    /// previous retries are canceled and replaced.
    /// </summary>
    public async Task ExecuteAsync(string instanceName, IAsyncPolicy asyncPolicy, Func<TimeSpan, Task> persistenceAction)
    {
        if (Pending.TryRemove(instanceName, out var existing))
        {
            existing.cts.Cancel();
            existing.cts.Dispose();
        }

        var cts = new CancellationTokenSource();
        Pending[instanceName] = (persistenceAction, cts);
        Current[instanceName] = persistenceAction;

        try
        {
            await asyncPolicy.ExecuteAsync(async ct =>
            {
                ct.ThrowIfCancellationRequested();
                await persistenceAction(InstanceExpirationPeriodTimeSpan).ConfigureAwait(false);

                if (Pending.TryGetValue(instanceName, out var current) && current.cts == cts)
                {
                    Pending.TryRemove(instanceName, out _);
                    cts.Dispose();
                }
            }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // expected if a new state replaced this one
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ReactiveLock] Final failure for '{instanceName}': {ex.Message}");
        }
    }

    /// <summary>
    /// Forces a retry of all latest failed persistence actions.
    /// </summary>
    public async Task FlushPendingAsync(IAsyncPolicy asyncPolicy)
    {
        foreach (var kvp in Pending.ToArray())
        {
            await ExecuteAsync(kvp.Key, asyncPolicy, kvp.Value.action);
        }
    }

    /// <summary>
    /// Periodically renews all current persistence actions by re-executing them with expiration.
    /// </summary>
    private async Task RenewalLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(InstanceRenewalPeriodTimeSpan);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var kvp in Current.ToArray())
                {
                    var instanceName = kvp.Key;
                    var action = kvp.Value;

                    try
                    {
                        await action(InstanceExpirationPeriodTimeSpan).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ReactiveLock] Renewal failed for '{instanceName}': {ex.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }

    public async ValueTask DisposeAsync()
    {
        Cancellation.Cancel();
        try
        {
            await RenewalTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        Cancellation.Dispose();
    }
}