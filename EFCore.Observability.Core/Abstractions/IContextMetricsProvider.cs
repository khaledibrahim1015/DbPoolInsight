using EFCore.Observability.Core.Models;

namespace EFCore.Observability.Core.Abstractions;

/// <summary>
/// Read-side interface: query collected metrics for any tracked DbContext type.
/// </summary>
public interface IContextMetricsProvider
{
    /// <summary>Returns pooled metrics for the given context type name, or null if not tracked.</summary>
    PooledContextMetrics? GetPooledMetrics(string contextName);

    /// <summary>Returns standard metrics for the given context type name, or null if not tracked.</summary>
    StandardContextMetrics? GetStandardMetrics(string contextName);

    /// <summary>Returns pooled metrics for all tracked pooled context types.</summary>
    IReadOnlyDictionary<string, PooledContextMetrics> GetAllPooledMetrics();

    /// <summary>Returns standard metrics for all tracked non-pooled context types.</summary>
    IReadOnlyDictionary<string, StandardContextMetrics> GetAllStandardMetrics();
}
