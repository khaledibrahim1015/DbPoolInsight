using EFCore.Observability.Core.Abstractions;
using EFCore.Observability.Core.Consts;
using EFCore.Observability.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace EFCore.Observability.Observers;


/// <summary>
/// Subscribes to EF Core's <see cref="DiagnosticListener"/> to intercept
/// <c>ContextInitialized</c> and <c>ContextDisposed</c> events.
/// 
/// This is the entry point for all physical lifecycle events.
/// Rent tracking is handled separately by <see cref="RentTrackingInterceptor"/>.
/// </summary>

public sealed class EFCoreDiagnosticObserver : IObserver<DiagnosticListener>
{
    private readonly ILogger<EFCoreDiagnosticObserver> _logger;
    private readonly IContextMetricsCollector _collector;

    public EFCoreDiagnosticObserver(
        IContextMetricsCollector collector,
        ILogger<EFCoreDiagnosticObserver> logger
        )
    {
        _collector = collector;
        _logger = logger;
    }

    public void OnNext(DiagnosticListener listener)
    {
        if (listener.Name == "Microsoft.EntityFrameworkCore")
        {
            listener.Subscribe(new EFCoreEventObserver(this));
            _logger.LogInformation("[EFObservability] Subscribed to EF Core DiagnosticListener");
        }
    }


    public void OnError(Exception error) =>
     _logger.LogError(error, "[EFObservability] DiagnosticListener error");
    public void OnCompleted() {}



    private sealed class EFCoreEventObserver : IObserver<KeyValuePair<string, object>>
    {
        private readonly EFCoreDiagnosticObserver _parent;
        public EFCoreEventObserver(
            EFCoreDiagnosticObserver? parent
            )
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        }
        public void OnNext(KeyValuePair<string, object> evt)
        {
            switch (evt.Key)
            {
                case EfCoreDiagnosticConstants.ContextInitialized:
                    _parent.HandleContextInitialized(evt.Value);
                    break;
                case EfCoreDiagnosticConstants.ContextDisposed:
                    _parent.HandleContextDisposed(evt.Value);
                    break;
                default:
                    // Ignore other events for now
                    break;
            }
        }
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }

    private void HandleContextInitialized(object payload)
    {

        if (payload is not ContextInitializedEventData eventData || eventData.Context is null)
        {
            _logger.LogWarning("[EFObservability] ContextInitialized: unexpected event data type");
            return;
        }

        var context = eventData.Context;
        var name = context.GetType().Name;
        var instanceId = context.ContextId.InstanceId;
        var lease = context.ContextId.Lease;
        var isPooled = ResolveIsPooled(context);

        // Notify the collector — this handles physical-creation tracking.
        // For pooled contexts: fires on first creation AND on reuse.
        // OnContextRented is handled separately by RentTrackingInterceptor.
        _collector.OnContextInitialized(name, instanceId, lease, isPooled);

        // Wire up the resettable tracking service so pool returns are captured.
        if (isPooled)
        {
            try
            {
                var svc = context.GetService<PoolResettableTrackingService>();
                svc?.Configure(name, instanceId, lease);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[EFObservability] Failed to configure PoolResettableTrackingService for {Context}",
                    name);
            }
        }
    }
    /// <summary>
    /// This fires when a context is PHYSICALLY DISPOSED
    /// For pooled contexts, this only happens on overflow or shutdown
    /// </summary>
    private void HandleContextDisposed(object? payload)
    {
        if (payload is not DbContextEventData eventData || eventData.Context is null)
        {
            _logger.LogWarning("[EFObservability] ContextDisposed: unexpected event data type");
            return;
        }

        var context = eventData.Context;
        var name = context.GetType().Name;
        var instanceId = context.ContextId.InstanceId;
        var lease = context.ContextId.Lease;
        var isPooled = ResolveIsPooled(context);

        if (isPooled)
            // Pooled context being destroyed means:
            // 1. Pool overflow (couldn't be returned)
            // 2. Application shutdown
            // 3. Context was leaked (never returned)
            _collector.OnPooledContextDisposed(name, instanceId, lease);
        else
            // Non-pooled context disposal (normal)
            _collector.OnStandardContextDisposed(name, instanceId);
    }





    private static bool ResolveIsPooled(DbContext context)
    {
        try
        {
            var maxPoolSize = context
                .GetService<IDbContextOptions>()
                .Extensions
                .OfType<CoreOptionsExtension>()
                .FirstOrDefault()?.MaxPoolSize ?? 0;
            return maxPoolSize > 0;
        }
        catch
        {
            return false;
        }
    }






}
