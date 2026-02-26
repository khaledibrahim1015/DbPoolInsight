using EFCore.Observability.Core.Enums;

namespace EFCore.Observability.Core.Models;


/// <summary>
/// Immutable snapshot of metrics for a pooled DbContext type.
/// All counters use long to avoid overflow in high-throughput scenarios.
/// </summary>
public sealed record PooledContextMetrics
{
    // ----- Identity -----------
    public string ContextName { get; init; } = default!;
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;

    // ── Pool configuration ────────────────────────────────────────────────
    public int MaxPoolSize { get; init; }

    // ── Physical instance lifecycle ───────────────────────────────────────
    /// <summary>Total DbContext instances ever physically created.</summary>
    public long PhysicalCreations { get; init; }

    /// <summary>Total DbContext instances physically destroyed.</summary>
    public long PhysicalDisposals { get; init; }

    /// <summary>Instances currently held in the pool (created but not destroyed).</summary>
    public long PhysicalInPool => PhysicalCreations - PhysicalDisposals;



    // ── Rent / return activity ────────────────────────────────────────────
    /// <summary>Total times a context was borrowed from the pool.</summary>
    public long TotalRents { get; init; }

    /// <summary>Total times a context was successfully returned to the pool.</summary>
    public long TotalReturns { get; init; }

    /// <summary>Contexts currently in use (rented but not yet returned).</summary>
    public long ActiveRents => TotalRents - TotalReturns - OverflowDisposals;

    // ── Health ────────────────────────────────────────────────────────────
    /// <summary>
    /// Contexts disposed because the pool was full when they tried to return.
    /// This is expected behaviour under load; it is NOT a leak.
    /// </summary>
    public long OverflowDisposals { get; init; }

    /// <summary>
    /// Contexts that were physically created beyond the pool's MaxPoolSize,
    /// indicating pool exhaustion.
    /// </summary>
    public long OverflowCreations { get; init; }

    /// <summary>Contexts rented but never returned — genuine resource leaks.</summary>
    public long LeakedContexts { get; init; }

    // ── Derived efficiency ────────────────────────────────────────────────
    /// <summary>Average number of requests handled per physical instance.</summary>
    public double ReuseRatio => PhysicalCreations > 0
        ? Math.Round((double)TotalRents / PhysicalCreations, 2)
        : 0;

    /// <summary>Percentage of rents that resulted in a clean return.</summary>
    public double ReturnRate => TotalRents > 0
        ? Math.Round((double)TotalReturns / TotalRents * 100, 2)
        : 100;

    /// <summary>Percentage of pool capacity currently filled with physical instances.</summary>
    public double PoolUtilization => MaxPoolSize > 0
        ? Math.Round((double)PhysicalInPool / MaxPoolSize * 100, 2)
        : 0;

    /// <summary>Instances sitting in the pool idle and ready to be rented.</summary>
    public long AvailableInPool => Math.Max(0, PhysicalInPool - ActiveRents);

    /// <summary>How many more physical instances could be created before hitting MaxPoolSize.</summary>
    public long RoomToGrow => Math.Max(0, MaxPoolSize - PhysicalInPool);

    // ── Rent duration ─────────────────────────────────────────────────────
    public long TotalRentDurationMs { get; init; }
    public long MinRentDurationMs { get; init; }
    public long MaxRentDurationMs { get; init; }

    public double AvgRentDurationMs => TotalRents > 0
        ? Math.Round((double)TotalRentDurationMs / TotalRents, 2)
        : 0;



    // ── Computed health ───────────────────────────────────────────────────
    public ContextHealthStatus HealthStatus => LeakedContexts switch
    {
        0 => ContextHealthStatus.Healthy,
        <= 5 => ContextHealthStatus.Warning,
        _ => ContextHealthStatus.Leaking
    };

    public ReuseQuality ReuseQualityRating => ReuseRatio switch
    {
        >= 5.0 => ReuseQuality.Excellent,
        >= 3.0 => ReuseQuality.VeryGood,
        >= 2.0 => ReuseQuality.Good,
        >= 1.0 => ReuseQuality.Fair,
        _ => ReuseQuality.Poor
    };


}
