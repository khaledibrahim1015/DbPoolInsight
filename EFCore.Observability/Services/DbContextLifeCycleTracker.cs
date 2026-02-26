

using EFCore.Observability.Core.Abstractions;
using EFCore.Observability.Core.Models;
using EFCore.Observability.Internal;
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






    /// <inheritdoc/>
    public void OnContextInitialized(string contextName, Guid instanceId, int lease, bool isPooled)
    {

        if (!isPooled)
            HandleStandardCreated(contextName, instanceId);


        // if pooled 


        throw new NotImplementedException();
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
