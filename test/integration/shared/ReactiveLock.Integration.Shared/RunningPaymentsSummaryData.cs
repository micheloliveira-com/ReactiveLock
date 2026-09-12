using System.Collections.Concurrent;

namespace ReactiveLock.Integration.Shared;

public sealed class RunningPaymentsSummaryData
{
    public ConcurrentBag<(DateTimeOffset? from, DateTimeOffset? to)> CurrentRanges { get; } = [];
}
