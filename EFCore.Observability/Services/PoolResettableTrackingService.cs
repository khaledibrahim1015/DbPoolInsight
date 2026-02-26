using EFCore.Observability.Core.Abstractions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace EFCore.Observability.Services;


/// <summary>
/// Registered as <see cref="IResettableService"/> inside EF Core's internal service provider.
/// EF Core calls <see cref="ResetState"/> when a pooled context is returned to the pool.
/// This is the only reliable hook for detecting pool returns.
/// </summary>
public sealed class PoolResettableTrackingService : IResettableService
{
    private readonly IContextMetricsCollector _collector;
    private readonly ILogger<PoolResettableTrackingService>? _logger;

    // Per-physical-instance state, set once then updated on reuse
    private string? _contextName;
    private Guid _instanceId;
    private int _currentLease;
    private bool _isInitialized;

    public PoolResettableTrackingService(
        IContextMetricsCollector collector,
        ILogger<PoolResettableTrackingService>? logger = null)
    {
        _collector = collector;
        _logger = logger;
    }

    /// <summary>
    /// Called once on physical creation (lease=0) and again on every reuse.
    /// Keeps _currentLease in sync so ResetState uses the right lease value.
    /// </summary>
    internal void Configure(string contextName, Guid instanceId, int lease)
    {
        if (!_isInitialized)
        {
            _contextName = contextName;
            _instanceId = instanceId;
            _isInitialized = true;
        }
        _currentLease = lease;
    }

    /// <summary>
    /// Called by EF Core when the pooled context is being returned to the pool.
    /// This fires BEFORE physical disposal for normal returns.
    /// </summary>
    public void ResetState()
    {
        if (!_isInitialized || _contextName is null)
        {
            _logger?.LogWarning("[EFObservability] ResetState called on uninitialized tracking service");
            return;
        }

        _collector.OnContextReturnedToPool(_contextName, _instanceId, _currentLease);
        _currentLease++;
    }

    public Task ResetStateAsync(CancellationToken cancellationToken = default)
    {
        ResetState();
        return Task.CompletedTask;
    }
}
