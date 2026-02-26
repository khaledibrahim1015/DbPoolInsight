

using EFCore.Observability.Core.Abstractions;
using EFCore.Observability.Core.Models;
using EFCore.Observability.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace EFCore.Observability.Services;

/// <summary>
/// Central singleton that coordinates all lifecycle tracking.
/// Implements both <see cref="IContextMetricsCollector"/> (write) and
/// <see cref="IContextMetricsProvider"/> (read).
/// </summary>
public class DbContextLifeCycleTracker : IContextMetricsCollector 
{
    private readonly ILogger<DbContextLifeCycleTracker> _logger;
    private readonly ObservabilityOptions _options;
    private readonly IInstanceActivityStore _activityStore;

     private readonly  InstanceStateStore _instanceStore =  new();

    private readonly ConcurrentDictionary<string, PooledMetricsState> _pooledStates = new();

    private readonly ConcurrentDictionary<string, StandardMetricsState> _standardStates = new();

    public DbContextLifeCycleTracker(
        ILogger<DbContextLifeCycleTracker> logger,
        IOptions<ObservabilityOptions> options,
        IInstanceActivityStore activityStore)
    {
        _logger = logger;
        _options = options.Value;
        _activityStore = activityStore;
    }
    // ── Pool size registration ────────────────────────────────────────────
    public void RegisterPoolSize<TContext>(int poolSize) where TContext : DbContext
    {
        // Use the type name as the key
        string contextName = typeof(TContext).Name;
        RegisterPoolSize(contextName, poolSize);
    }
    public void RegisterPoolSize(string contextName, int poolSize)
    {
        var state = _pooledStates.GetOrAdd(contextName,
            _ => new PooledMetricsState(contextName, poolSize));
        state.MaxPoolSize = poolSize;
    }





    /// <inheritdoc/>
    public void OnContextInitialized(string contextName, Guid instanceId, int lease, bool isPooled)
    {

        if (!isPooled)
            HandleStandardCreated(contextName, instanceId);

        // Pooled: track physical creations by unique instance ID
        // For pooled contexts, DON'T increment TotalRents here
        // It will be tracked via OnContextRented instead
        // For pooled contexts, only count physical creations (first time we see this instanceId).
        if (!_instanceStore.TryAddSeen(contextName, instanceId))
            return; // Already seen — this is a reuse, handled by OnContextRented

        var state =  GetOrAddPooledState(contextName);
        long creationCount = state.IncrementPhysicalCreations();
        state.Touch();

        // Determine at creation time whether this instance is overflow.
        bool isOverflow  = state.MaxPoolSize > 0 && creationCount > state.MaxPoolSize;
        if(isOverflow)
            state.IncrementOverflowCreations();

        if (_options.EnableDiagnosticLogging)
        {
            if (isOverflow)
                _logger.LogWarning(
                    "[EFObservability] Pool OVERFLOW physical create: {Context} Instance={Id} Physical={Count} MaxPool={Max}",
                    contextName, instanceId.ToString()[..8], creationCount, state.MaxPoolSize);
            else
                _logger.LogInformation(
                    "[EFObservability] Pool physical create: {Context} Instance={Id} Physical={Count}",
                    contextName, instanceId.ToString()[..8], creationCount);
        }

    }

    /// <inheritdoc/>
    public void OnContextRented(string contextName, Guid instanceId, int lease)
    {
        throw new NotImplementedException();
    }
    /// <inheritdoc/>
    public void OnContextReturnedToPool(string contextName, Guid instanceId, int lease)
    {
        throw new NotImplementedException();
    }
    /// <inheritdoc/>
    public void OnPooledContextDisposed(string contextName, Guid instanceId, int lease)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public void OnStandardContextDisposed(string contextName, Guid instanceId)
    {
        if (!_standardStates.TryGetValue(contextName, out var state))
            return;

        state.IncrementTotalDisposals();
        state.Touch();

        if(_instanceStore.TryRemoveState(instanceId , out var instanceState) )
        {
            var lifetimeMs = (long)(DateTime.UtcNow - instanceState.CreatedAt).TotalMilliseconds;
            state.RecordLifetime(lifetimeMs);
            RecordActivity(contextName, instanceId, instanceState.CurrentLease,
                instanceState.CreatedAt, DateTime.UtcNow, lifetimeMs);

        }

        if (_options.EnableDiagnosticLogging)
            _logger.LogDebug(
                "[EFObservability] Standard disposed: {Context} Instance={Id}",
                contextName, instanceId.ToString()[..8]);

    }






    private void HandleStandardCreated(string contextName, Guid instanceId)
    {
        var state = _standardStates.GetOrAdd(contextName,
                                        _ => new StandardMetricsState(contextName));

        state.IncrementTotalCreations();
        state.Touch();


        _instanceStore.AddOrUpdateState(instanceId, new InstanceState
        {
            ContextName = contextName,
            IsPooled = false,
            CreatedAt = DateTime.UtcNow,
            LastRented = DateTime.UtcNow
        });

        if (_options.EnableDiagnosticLogging)
            _logger.LogDebug(
                "[EFObservability] Standard created: {Context} Instance={Id}",
                contextName, instanceId.ToString()[..8]);
    }


    private PooledMetricsState GetOrAddPooledState (string contextName )
            =>   _pooledStates.GetOrAdd(contextName,
                                        _ => new PooledMetricsState(contextName));
    private void RecordActivity(
        string contextName, Guid instanceId, int lease,
        DateTime startedAt, DateTime endedAt, long durationMs)
    {
        _activityStore.Record(contextName, new InstanceActivity
        {
            InstanceId = instanceId.ToString()[..8],
            Lease = lease,
            StartedAt = startedAt,
            EndedAt = endedAt,
            DurationMs = durationMs
        });
    }




}
