

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
public class DbContextLifeCycleTracker : IContextMetricsCollector  , IContextMetricsProvider
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



    // ── IContextMetricsCollector ─────────────────────────────────────────────

    /// <inheritdoc/>
    public void OnContextInitialized(string contextName, Guid instanceId, int lease, bool isPooled)
    {

        if (!isPooled)
            HandleStandardCreated(contextName, instanceId , lease );

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

        _instanceStore.AddOrUpdateState(instanceId, new InstanceState
        {
            ContextName = contextName,
            IsPooled = true,
            CurrentLease = lease,
            CreatedAt = DateTime.UtcNow,
            LastRented = DateTime.UtcNow,
            IsOverflow = isOverflow

        }); 
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
        var state = GetOrAddPooledState(contextName);
        long rents = state.IncrementTotalRents();
        state.Touch();  

        _instanceStore.TryAddRented(contextName, instanceId);  // Track that this instance is currently rented

        _instanceStore.UpdateState(instanceId , s =>  
        {
            s.CurrentLease = lease;
            s.LastRented = DateTime.UtcNow;
            s.WasReturnedToPool =  false; // Clear the flag on rent
        });

        if (_options.EnableDiagnosticLogging)
            _logger.LogInformation(
                "[EFObservability] Pool rented: {Context} Instance={Id} Lease={Lease} TotalRents={Rents}",
                contextName, instanceId.ToString()[..8], lease, rents);
    }

    /// <inheritdoc/>
    public void OnContextReturnedToPool(string contextName, Guid instanceId, int lease)
    {
        if (!_pooledStates.TryGetValue(contextName, out var state)) return;

        state.IncrementTotalReturns();
        state.Touch();
        _instanceStore.TryRemoveRented(contextName, instanceId);  //  i think usless but good to keep the store clean of currently rented instances

        if (_instanceStore.TryGetState(instanceId, out var instanceState) && instanceState != null)
        {
            _instanceStore.TryUpdateState(instanceId, s =>
            {
                 s.WasReturnedToPool = true;
                 s.CurrentLease = lease;
                 s.LastReturned = DateTime.UtcNow;
            });

            if (_options.TrackRentDurations)
            {
                var durationMs = (long)(DateTime.UtcNow - instanceState.LastRented).TotalMilliseconds;
                state.RecordRentDuration(durationMs);
                RecordActivity(contextName, instanceId, lease, instanceState.LastRented, DateTime.UtcNow, durationMs);
            }
        }

        if (_options.EnableDiagnosticLogging)
            _logger.LogInformation(
                "[EFObservability] Pool returned: {Context} Instance={Id} Lease={Lease}",
                contextName, instanceId.ToString()[..8], lease);

    }
    /// <inheritdoc/>
    public void OnPooledContextDisposed(string contextName, Guid instanceId, int lease)
    {
        if (!_pooledStates.TryGetValue(contextName, out var state)) return;

        long creations = state.ReadPhysicalCreations();
        long disposals = state.ReadPhysicalDisposals();
        state.IncrementPhysicalDisposals();
        state.Touch();

        _instanceStore.TryRemoveRented(contextName, instanceId);
        _instanceStore.TryRemoveSeen(contextName, instanceId);

        if (_instanceStore.TryRemoveState(instanceId, out var instanceState) && instanceState != null)
        {
            var classification = PoolOverflowDetector.Classify(
                instanceState, creations, disposals, state.MaxPoolSize);

            switch (classification)
            {
                case DisposalClassification.OverflowAfterReturn:
                case DisposalClassification.OverflowCreation:
                case DisposalClassification.OverflowCapacity:
                    state.IncrementOverflowDisposals();
                    if (_options.EnableDiagnosticLogging)
                        _logger.LogInformation(
                            "[EFObservability] Pool overflow dispose ({Reason}): {Context} Instance={Id}",
                            classification, contextName, instanceId.ToString()[..8]);
                    break;

                case DisposalClassification.Leaked:
                    state.IncrementLeakedContexts();
                    if (_options.EnableDiagnosticLogging)
                        _logger.LogWarning(
                        "[EFObservability] LEAKED context: {Context} Instance={Id} HeldFor={Duration}ms",
                        contextName, instanceId.ToString()[..8],
                        (long)(DateTime.UtcNow - instanceState.LastRented).TotalMilliseconds);
                    break;
            }

            if (_options.TrackRentDurations)
            {
                var durationMs = (long)(DateTime.UtcNow - instanceState.LastRented).TotalMilliseconds;
                state.RecordRentDuration(durationMs);
                RecordActivity(contextName, instanceId, lease, instanceState.LastRented, DateTime.UtcNow, durationMs);
            }
        }
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
    // ── Private helpers ───────────────────────────────────────────────────
    private void HandleStandardCreated(string contextName, Guid instanceId , int lease)
    {
        var state = _standardStates.GetOrAdd(contextName,
                                        _ => new StandardMetricsState(contextName));

        state.IncrementTotalCreations();
        state.Touch();


        _instanceStore.AddOrUpdateState(instanceId, new InstanceState
        {
            ContextName = contextName,
            IsPooled = false,
            CurrentLease = lease,
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


    // ── IContextMetricsProvider ───────────────────────────────────────────

    public PooledContextMetrics? GetPooledMetrics(string contextName) =>
        _pooledStates.TryGetValue(contextName, out var s) ? s.Snapshot() : null;

    public StandardContextMetrics? GetStandardMetrics(string contextName) =>
        _standardStates.TryGetValue(contextName, out var s) ? s.Snapshot() : null;

    public IReadOnlyDictionary<string, PooledContextMetrics> GetAllPooledMetrics() =>
        _pooledStates.ToDictionary(kv => kv.Key, kv => kv.Value.Snapshot());

    public IReadOnlyDictionary<string, StandardContextMetrics> GetAllStandardMetrics() =>
        _standardStates.ToDictionary(kv => kv.Key, kv => kv.Value.Snapshot());

    // ── Activity access (for query service) ──────────────────────────────

    public IReadOnlyList<InstanceActivity> GetRecentActivity(string contextName, int take = 20) =>
        _activityStore.GetRecent(contextName, take);

    public IReadOnlyList<InstanceActivity> GetAllActivity(string contextName) =>
        _activityStore.GetAll(contextName);





}
