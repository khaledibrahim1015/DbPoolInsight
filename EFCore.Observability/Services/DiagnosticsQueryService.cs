
using EFCore.Observability.Core.Models;

namespace EFCore.Observability.Services;
/// <summary>
/// High-level read service for consuming metrics and activity.
/// Use this in diagnostic endpoints, health checks, or dashboards.
/// </summary>
public sealed class DiagnosticsQueryService
{
    private readonly DbContextLifeCycleTracker _tracker;

    public DiagnosticsQueryService(DbContextLifeCycleTracker tracker)
    {
        _tracker = tracker;
    }

    // ── Raw metrics ───────────────────────────────────────────────────────

    public PooledContextMetrics? GetPooledMetrics(string contextName) =>
        _tracker.GetPooledMetrics(contextName);

    public StandardContextMetrics? GetStandardMetrics(string contextName) =>
        _tracker.GetStandardMetrics(contextName);

    public IReadOnlyDictionary<string, PooledContextMetrics> GetAllPooledMetrics() =>
        _tracker.GetAllPooledMetrics();

    public IReadOnlyDictionary<string, StandardContextMetrics> GetAllStandardMetrics() =>
        _tracker.GetAllStandardMetrics();

    // ── Activity ──────────────────────────────────────────────────────────

    public IReadOnlyList<InstanceActivity> GetRecentActivity(string contextName, int take = 20) =>
        _tracker.GetRecentActivity(contextName, take);

    public IReadOnlyList<InstanceActivity> GetAllActivity(string contextName) =>
        _tracker.GetAllActivity(contextName);

    // ── Shaped responses ──────────────────────────────────────────────────

    /// <summary>
    /// Returns a combined summary object suitable for a diagnostics endpoint.
    /// </summary>

    public object GetAllDetails() => new
    {
        Pooled = _tracker.GetAllPooledMetrics().Values
                    .Select(m => new
                    {
                        Summary = BuildPooledSummary(m),
                        Activity = _tracker.GetAllActivity(m.ContextName)
                    }).ToList(),

        Standard = _tracker.GetAllStandardMetrics().Values
                .Select(m => new
                {
                    Summary = BuildStandardSummary(m),
                    Activity = _tracker.GetAllActivity(m.ContextName)
                }).ToList(),
    };


    /// <summary>
    /// Returns a combined summary object suitable for a diagnostics endpoint.
    /// </summary>
    public DiagnosticsSummary GetSummary() => new()
    {
        Pooled = _tracker.GetAllPooledMetrics().Values
            .Select(BuildPooledSummary)
            .ToList(),
        Standard = _tracker.GetAllStandardMetrics().Values
            .Select(BuildStandardSummary)
            .ToList()
    };

    // ── Helpers ───────────────────────────────────────────────────────────

    private static PooledContextSummary BuildPooledSummary(PooledContextMetrics m) => new()
    {
        Summary  = $"Pool Size: {m.MaxPoolSize}, Active: {m.ActiveRents}/{m.PhysicalInPool}",
        ContextName = m.ContextName,
        MaxPoolSize = m.MaxPoolSize,

        //Lifecycle
        PhysicalCreations = m.PhysicalCreations,
        PhysicalDisposals = m.PhysicalDisposals,
        PhysicalInPool = m.PhysicalInPool,
        AvailableInPool = m.AvailableInPool,
        RoomToGrow = m.RoomToGrow,

        //Activity
        TotalRents = m.TotalRents,
        TotalReturns = m.TotalReturns,
        ActiveRents = m.ActiveRents,

        OverflowDisposals = m.OverflowDisposals,
        LeakedContexts = m.LeakedContexts,

        //Efficiency
        PoolUtilization = m.PoolUtilization,//%
        ReuseRatio = m.ReuseRatio,//x
        ReturnRate = m.ReturnRate,//%
        //RentDuration
        AvgRentDurationMs = m.AvgRentDurationMs,
        MinRentDurationMs = m.MinRentDurationMs,
        MaxRentDurationMs = m.MaxRentDurationMs,

        HealthStatus = m.HealthStatus.ToString(),
        ReuseQuality = m.ReuseQualityRating.ToString(),

        LastUpdated = m.LastUpdated
    };

    private static StandardContextSummary BuildStandardSummary(StandardContextMetrics m) => new()
    {
        Summary = $"Created: {m.TotalCreations}, Active: {m.ActiveContexts}",
        ContextName = m.ContextName,

        TotalCreations = m.TotalCreations,
        TotalDisposals = m.TotalDisposals,
        ActiveContexts = m.ActiveContexts,

        PotentialLeaks = m.PotentialLeaks,

        AvgLifetimeMs = m.AvgLifetimeMs,
        MinLifetimeMs = m.MinLifetimeMs,
        MaxLifetimeMs = m.MaxLifetimeMs,

        HealthStatus = m.HealthStatus.ToString(),
        LastUpdated = m.LastUpdated
    };
}

// ── Response DTOs ─────────────────────────────────────────────────────────────

public sealed record DiagnosticsSummary
{
    public IReadOnlyList<PooledContextSummary> Pooled { get; init; } = [];
    public IReadOnlyList<StandardContextSummary> Standard { get; init; } = [];
}

public sealed record PooledContextSummary
{
    public string Summary { get; init; } = string.Empty;

    public string ContextName { get; init; } = string.Empty;
    public int MaxPoolSize { get; init; }
    public long PhysicalCreations { get; init; }
    public long PhysicalDisposals { get; init; }
    public long PhysicalInPool { get; init; }
    public long AvailableInPool { get; init; }
    public long RoomToGrow { get; init; }
    public long ActiveRents { get; init; }
    public long TotalRents { get; init; }
    public long TotalReturns { get; init; }
    public long OverflowDisposals { get; init; }
    public long LeakedContexts { get; init; }
    public double PoolUtilization { get; init; }
    public double ReuseRatio { get; init; }
    public double ReturnRate { get; init; }
    public double AvgRentDurationMs { get; init; }
    public long MinRentDurationMs { get; init; }
    public long MaxRentDurationMs { get; init; }
    public string HealthStatus { get; init; } = string.Empty;
    public string ReuseQuality { get; init; } = string.Empty;
    public DateTime LastUpdated { get; init; }
}

public sealed record StandardContextSummary
{
    public string Summary { get; init; } = string.Empty;

    public string ContextName { get; init; } = string.Empty;
    public long TotalCreations { get; init; }
    public long TotalDisposals { get; init; }
    public long ActiveContexts { get; init; }
    public long PotentialLeaks { get; init; }
    public double AvgLifetimeMs { get; init; }
    public long MinLifetimeMs { get; init; }
    public long MaxLifetimeMs { get; init; }
    public string HealthStatus { get; init; } = string.Empty;
    public DateTime LastUpdated { get; init; }
}