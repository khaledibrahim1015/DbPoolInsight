

using EFCore.Observability.Core.Models;

namespace EFCore.Observability.Internal;


/// <summary>
/// Mutable, thread-safe counters for a single non-pooled DbContext type.
/// All mutations use Interlocked to prevent race conditions.
/// </summary>
internal sealed class StandardMetricsState
{
    public string ContextName { get; }

    private long _totalCreations;
    private long _totalDisposals;
    private long _totalLifetimeMs;
    private long _minLifetimeMs = long.MaxValue;
    private long _maxLifetimeMs;

    private DateTime _lastUpdated = DateTime.UtcNow;

    public StandardMetricsState(string contextName) => ContextName = contextName;

    public long IncrementTotalCreations() =>
        Interlocked.Increment(ref _totalCreations);

    public long IncrementTotalDisposals() =>
        Interlocked.Increment(ref _totalDisposals);

    public void RecordLifetime(long lifetimeMs)
    {
        Interlocked.Add(ref _totalLifetimeMs, lifetimeMs);

        long currentMin;
        do
        {
            currentMin = Interlocked.Read(ref _minLifetimeMs);
            if (lifetimeMs >= currentMin) break;
        } while (Interlocked.CompareExchange(ref _minLifetimeMs, lifetimeMs, currentMin) != currentMin);

        long currentMax;
        do
        {
            currentMax = Interlocked.Read(ref _maxLifetimeMs);
            if (lifetimeMs <= currentMax) break;
        } while (Interlocked.CompareExchange(ref _maxLifetimeMs, lifetimeMs, currentMax) != currentMax);

        _lastUpdated = DateTime.UtcNow;
    }

    public void Touch() => _lastUpdated = DateTime.UtcNow;

    public StandardContextMetrics Snapshot()
    {
        long minMs = Interlocked.Read(ref _minLifetimeMs);
        return new StandardContextMetrics
        {
            ContextName = ContextName,
            LastUpdated = _lastUpdated,
            TotalCreations = Interlocked.Read(ref _totalCreations),
            TotalDisposals = Interlocked.Read(ref _totalDisposals),
            TotalLifetimeMs = Interlocked.Read(ref _totalLifetimeMs),
            MinLifetimeMs = minMs == long.MaxValue ? 0 : minMs,
            MaxLifetimeMs = Interlocked.Read(ref _maxLifetimeMs),
        };
    }
}
