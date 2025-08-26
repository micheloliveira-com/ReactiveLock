using System.Collections.Concurrent;

public class RunningPaymentsSummaryData
{
    public readonly ConcurrentBag<(DateTimeOffset? from, DateTimeOffset? to)> CurrentRanges 
        = new();
}