using EFCore.Observability.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EFCore.Observability.API.Utils;

public class EFCorePoolHealthCheck : IHealthCheck
{
    private readonly DiagnosticsQueryService _query;
    public EFCorePoolHealthCheck(DiagnosticsQueryService query) => _query = query;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var summary = _query.GetSummary();

        foreach (var pool in summary.Pooled)
        {
            if (pool.LeakedContexts > 0)
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"{pool.ContextName}: {pool.LeakedContexts} leaked contexts"));

            if (pool.ReturnRate < 95)
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"{pool.ContextName}: return rate {pool.ReturnRate:F1}%"));
        }

        return Task.FromResult(HealthCheckResult.Healthy("All pools healthy"));
    }
}
