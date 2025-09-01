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
    private Task RecoveryTask { get; }

    private TimeSpan InstanceRenewalPeriodTimeSpan { get; set; }
    private TimeSpan InstanceExpirationPeriodTimeSpan { get; set; }
    private TimeSpan InstanceRecoverPeriodTimeSpan { get; set; }

    private IAsyncPolicy AsyncPolicy { get; }

    private ConcurrentDictionary<string, (Func<DateTimeOffset, Task> action, CancellationTokenSource cts)> Pending { get; } = new();
    private ConcurrentDictionary<string, Func<DateTimeOffset, Task>> Current { get; } = new();

    /// <summary>
    /// Semaphore gate to ensure Renewal/Recovery loops do not run
    /// while ExecuteAsync is already executing actions.
    /// </summary>
    public SemaphoreSlim ExecutionGate { get; } = new(1, 1);

    public ReactiveLockResilientReplicator(
        IAsyncPolicy? asyncPolicy,
        (TimeSpan instanceRenewalPeriodTimeSpan,
        TimeSpan instanceExpirationPeriodTimeSpan,
        TimeSpan instanceRecoverPeriodTimeSpan) resiliencyParameters)
    {
        var policy = ReactiveLockPollyPolicies.UseOrCreateDefaultRetryPolicy(asyncPolicy);
        AsyncPolicy = policy;

        InstanceRenewalPeriodTimeSpan = resiliencyParameters.instanceRenewalPeriodTimeSpan != default ? resiliencyParameters.instanceRenewalPeriodTimeSpan : TimeSpan.FromSeconds(5);
        InstanceExpirationPeriodTimeSpan = resiliencyParameters.instanceExpirationPeriodTimeSpan != default ? resiliencyParameters.instanceExpirationPeriodTimeSpan : TimeSpan.FromSeconds(10);
        InstanceRecoverPeriodTimeSpan = resiliencyParameters.instanceRecoverPeriodTimeSpan != default ? resiliencyParameters.instanceRecoverPeriodTimeSpan : TimeSpan.FromSeconds(15);

        // Start renewal loop in background
        RenewalTask = Task.Run(() => RenewalLoopAsync(Cancellation.Token));

        // Start recovery loop in background
        RecoveryTask = Task.Run(() => RecoveryLoopAsync(Cancellation.Token));
    }

    public async Task ExecuteAsync(string instanceName, Func<DateTimeOffset, Task> persistenceAction)
    {
        if (Pending.TryRemove(instanceName, out var existing))
        {
            await existing.cts.CancelAsync();
            existing.cts.Dispose();
        }

        var cts = new CancellationTokenSource();
        Pending[instanceName] = (persistenceAction, cts);
        Current[instanceName] = persistenceAction;

        await ExecutionGate.WaitAsync(); // ensure exclusivity against Renewal/Recovery loops
        try
        {
            await AsyncPolicy.ExecuteAsync(async ct =>
            {
                ct.ThrowIfCancellationRequested();
                await persistenceAction(GetNextExpiration()).ConfigureAwait(false);

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
        finally
        {
            ExecutionGate.Release();
        }
    }


    private DateTimeOffset GetNextExpiration()
    {
        return DateTimeOffset.UtcNow + InstanceExpirationPeriodTimeSpan;
    }

    public async Task FlushPendingAsync()
    {
        foreach (var kvp in Pending.ToArray())
        {
            await ExecuteAsync(kvp.Key, kvp.Value.action);
        }
    }

    private async Task RenewalLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(InstanceRenewalPeriodTimeSpan);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await ExecutionGate.WaitAsync(0, cancellationToken)) // skip if ExecuteAsync is running
                    continue;

                try
                {
                    foreach (var kvp in Current.ToArray())
                    {
                        var instanceName = kvp.Key;
                        var action = kvp.Value;

                        try
                        {
                            await AsyncPolicy.ExecuteAsync(() => action(GetNextExpiration())).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ReactiveLock] Renewal failed for '{instanceName}': {ex.Message}");
                        }
                    }
                }
                finally
                {
                    ExecutionGate.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }

    private async Task RecoveryLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(InstanceRecoverPeriodTimeSpan);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await ExecutionGate.WaitAsync(0, cancellationToken)) // skip if ExecuteAsync is running
                    continue;

                try
                {
                    await FlushPendingAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ReactiveLock] Recovery flush failed: {ex.Message}");
                }
                finally
                {
                    ExecutionGate.Release();
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
        // Request cancellation of background loops
        await Cancellation.CancelAsync();
        try
        {
            await Task.WhenAll(RenewalTask, RecoveryTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // ignore expected cancellation
        }
        finally
        {
            Cancellation.Dispose();
            GC.SuppressFinalize(this); // prevent finalizer overhead if added in derived classes
        }
    }

}
