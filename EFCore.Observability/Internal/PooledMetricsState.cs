

using EFCore.Observability.Core.Models;

namespace EFCore.Observability.Internal;


/// <summary>
/// Mutable, thread-safe counters for a single pooled DbContext type.
/// All mutations use Interlocked to prevent race conditions under high concurrency.
/// Snapshots are produced as immutable <see cref="PooledContextMetrics"/> records.
/// </summary>
internal sealed class PooledMetricsState
{
    public string ContextName { get; }
    public int MaxPoolSize { get; set; }

    // Physical lifecycle — use Interlocked for all mutations
    private long _physicalCreations;
    private long _physicalDisposals;

    // Rent/return activity
    private long _totalRents;
    private long _totalReturns;

    // Health
    private long _overflowDisposals;
    private long _overflowCreations;
    private long _leakedContexts;

    // Duration tracking
    private long _totalRentDurationMs;
    private long _minRentDurationMs = long.MaxValue;
    private long _maxRentDurationMs;

    private DateTime _lastUpdated = DateTime.UtcNow;

    public PooledMetricsState(string contextName, int maxPoolSize = 0)
    {
        ContextName = contextName;
        MaxPoolSize = maxPoolSize;
    }

    // ── Mutators (all thread-safe via Interlocked) ────────────────────────

    public long IncrementPhysicalCreations() =>
        Interlocked.Increment(ref _physicalCreations);

    public long ReadPhysicalCreations() =>
        Interlocked.Read(ref _physicalCreations);

    public long IncrementPhysicalDisposals() =>
        Interlocked.Increment(ref _physicalDisposals);

    public long ReadPhysicalDisposals() =>
        Interlocked.Read(ref _physicalDisposals);

    public long IncrementTotalRents() =>
        Interlocked.Increment(ref _totalRents);

    public long IncrementTotalReturns() =>
        Interlocked.Increment(ref _totalReturns);

    public long IncrementOverflowDisposals() =>
        Interlocked.Increment(ref _overflowDisposals);

    public long IncrementOverflowCreations() =>
        Interlocked.Increment(ref _overflowCreations);

    public long IncrementLeakedContexts() =>
        Interlocked.Increment(ref _leakedContexts);

    public void RecordRentDuration(long durationMs)
    {
        Interlocked.Add(ref _totalRentDurationMs, durationMs);

        // Atomic min update
        long currentMin;
        do
        {
            currentMin = Interlocked.Read(ref _minRentDurationMs);
            if (durationMs >= currentMin) break;
        } while (Interlocked.CompareExchange(ref _minRentDurationMs, durationMs, currentMin) != currentMin);

        // Atomic max update
        long currentMax;
        do
        {
            currentMax = Interlocked.Read(ref _maxRentDurationMs);
            if (durationMs <= currentMax) break;
        } while (Interlocked.CompareExchange(ref _maxRentDurationMs, durationMs, currentMax) != currentMax);

        _lastUpdated = DateTime.UtcNow;
    }

    public void Touch() => _lastUpdated = DateTime.UtcNow;

    // ── Snapshot ──────────────────────────────────────────────────────────

    public PooledContextMetrics Snapshot()
    {
        long minMs = Interlocked.Read(ref _minRentDurationMs);
        return new PooledContextMetrics
        {
            ContextName = ContextName,
            MaxPoolSize = MaxPoolSize,
            LastUpdated = _lastUpdated,
            PhysicalCreations = Interlocked.Read(ref _physicalCreations),
            PhysicalDisposals = Interlocked.Read(ref _physicalDisposals),
            TotalRents = Interlocked.Read(ref _totalRents),
            TotalReturns = Interlocked.Read(ref _totalReturns),
            OverflowDisposals = Interlocked.Read(ref _overflowDisposals),
            OverflowCreations = Interlocked.Read(ref _overflowCreations),
            LeakedContexts = Interlocked.Read(ref _leakedContexts),
            TotalRentDurationMs = Interlocked.Read(ref _totalRentDurationMs),
            MinRentDurationMs = minMs == long.MaxValue ? 0 : minMs,
            MaxRentDurationMs = Interlocked.Read(ref _maxRentDurationMs),
        };
    }
}
