using EFCore.Observability.Core.Enums;

namespace EFCore.Observability.Core.Models;


/// <summary>
/// Immutable snapshot of metrics for a non-pooled (traditional) DbContext type.
/// </summary>
public sealed record StandardContextMetrics
{
    public string ContextName { get; init; } = string.Empty;
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;

    /// <summary>Total DbContext instances created.</summary>
    public long TotalCreations { get; init; }

    /// <summary>Total DbContext instances disposed.</summary>
    public long TotalDisposals { get; init; }

    /// <summary>Instances currently alive (created but not yet disposed).</summary>
    public long ActiveContexts => TotalCreations - TotalDisposals;

    /// <summary>Whether every created context has been disposed.</summary>
    public bool AllDisposed => TotalCreations == TotalDisposals;

    /// <summary>Contexts that were created but not yet disposed — potential leaks.</summary>
    public long PotentialLeaks => Math.Max(0, TotalCreations - TotalDisposals);

    // ── Lifetime ──────────────────────────────────────────────────────────
    public long TotalLifetimeMs { get; init; }
    public long MinLifetimeMs { get; init; }
    public long MaxLifetimeMs { get; init; }

    public double AvgLifetimeMs => TotalDisposals > 0
        ? Math.Round((double)TotalLifetimeMs / TotalDisposals, 2)
        : 0;

    // ── Health ────────────────────────────────────────────────────────────
    public ContextHealthStatus HealthStatus => PotentialLeaks switch
    {
        0 => ContextHealthStatus.Healthy,
        <= 5 => ContextHealthStatus.Warning,
        _ => ContextHealthStatus.Leaking
    };
}
