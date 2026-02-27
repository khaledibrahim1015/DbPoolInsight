
using EFCore.Observability.Core.Abstractions;
using EFCore.Observability.Core.Models;
using EFCore.Observability.Interceptors;
using EFCore.Observability.Internal;
using EFCore.Observability.Observers;
using EFCore.Observability.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace EFCore.Observability.Extensions;


/// <summary>
/// Dependency injection registration for EFCore.Observability.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all EFCore.Observability services and subscribes to the EF Core
    /// DiagnosticListener.
    ///
    /// <code>
    /// builder.Services.AddEFCoreObservability(opts =>
    /// {
    ///     opts.TrackRentDurations = true;
    ///     opts.LeakDetectionThresholdMs = 30_000;
    /// });
    /// </code>
    /// </summary>
    public static IServiceCollection AddEFCoreObservability(
        this IServiceCollection services,
        Action<ObservabilityOptions>? configure = null)
    {
        // Options
        var optionsBuilder = services.AddOptions<ObservabilityOptions>();
        if (configure is not null)
            optionsBuilder.Configure(configure);

        // Core infrastructure
        services.TryAddSingleton<IInstanceActivityStore>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<ObservabilityOptions>>().Value;
            return new RingBufferActivityStore(opts.MaxActivityHistoryPerContext);
        });

        services.TryAddSingleton<DbContextLifeCycleTracker>();

        // Register tracker under both interfaces (same singleton instance)
        services.TryAddSingleton<IContextMetricsCollector>(sp =>
            sp.GetRequiredService<DbContextLifeCycleTracker>());
        services.TryAddSingleton<IContextMetricsProvider>(sp =>
            sp.GetRequiredService<DbContextLifeCycleTracker>());

        // Interceptor and observer
        services.TryAddSingleton<RentTrackingInterceptor>();
        services.TryAddSingleton<EFCoreDiagnosticObserver>();

        // Query / summary service
        services.TryAddSingleton<DiagnosticsQueryService>();

        return services;
    }
}

/// <summary>
/// Host-level extension to activate the DiagnosticListener subscription.
/// Call this after <c>builder.Build()</c>.
///
/// <code>
/// var app = builder.Build();
/// app.Services.UseEFCoreObservability();
/// or 
/// app.UseEFCoreObservability();
/// </code>
/// </summary>
public static class ApplicationBuilderExtensions
{
    public static IServiceProvider UseEFCoreObservability(this IServiceProvider services)
    {
        var observer = services.GetRequiredService<EFCoreDiagnosticObserver>();
        DiagnosticListener.AllListeners.Subscribe(observer);
        return services;
    }

    public static IHost UseEFCoreObservability(this IHost host)
    {

        var observer = host.Services.GetRequiredService<EFCoreDiagnosticObserver>();
        DiagnosticListener.AllListeners.Subscribe(observer);


        return host;
    }

}