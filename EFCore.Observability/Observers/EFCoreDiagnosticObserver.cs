using EFCore.Observability.Core.Abstractions;
using EFCore.Observability.Core.Consts;
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









}
