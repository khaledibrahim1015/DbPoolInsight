
using EFCore.Observability.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

namespace EFCore.Observability.OpenTelemetry;

/// <summary>
/// Registers the EF Core pool meter into an OpenTelemetry <see cref="MeterProviderBuilder"/>.
/// The meter is backed by <see cref="IContextMetricsProvider"/> which is resolved from DI.
/// </summary>
public static class EFCoreMetricsInstrumentation
{
    /// <summary>
    /// Adds EF Core pool observability metrics to the OpenTelemetry pipeline.
    ///
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .WithMetrics(m => m.AddEFCorePoolInstrumentation());
    /// </code>
    /// </summary>
    public static MeterProviderBuilder AddEFCorePoolInstrumentation(
        this MeterProviderBuilder builder)
    {
        // Register the meter as a singleton so it lives for the application lifetime.
        builder.ConfigureServices(services =>
            services.AddSingleton<EFCorePoolMeter>());

        // Tell OTel to collect from this meter by name.
        builder.AddMeter(EFCorePoolMeter.MeterName);

        // Ensure the meter is constructed (and therefore subscribed) at startup.
        builder.AddInstrumentation(sp => sp.GetRequiredService<EFCorePoolMeter>());

        return builder;
    }
    /// <summary>
    /// Adds EF Core standard observability metrics to the OpenTelemetry pipeline.
    ///
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .WithMetrics(m => m.AddEFCoreStandardInstrumentation());
    /// </code>
    /// </summary>
    public static MeterProviderBuilder AddEFCoreStandardInstrumentation(
    this MeterProviderBuilder builder)
    {
        // Register the meter as a singleton so it lives for the application lifetime.
        builder.ConfigureServices(services =>
            services.AddSingleton<EFCoreStandardMeter>());

        // Tell OTel to collect from this meter by name.
        builder.AddMeter(EFCoreStandardMeter.MeterName);

        // Ensure the meter is constructed (and therefore subscribed) at startup.
        builder.AddInstrumentation(sp => sp.GetRequiredService<EFCoreStandardMeter>());

        return builder;
    }


    /// <summary>
    /// Adds EF Core standard and pool observability metrics to the OpenTelemetry pipeline.
    ///
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .WithMetrics(m => m.AddEFCoreInstrumentation() );
    /// </code>
    /// </summary>
    public static MeterProviderBuilder AddEFCoreInstrumentation(
    this MeterProviderBuilder builder)
    {
        builder.AddEFCorePoolInstrumentation();
        builder.AddEFCoreStandardInstrumentation();
        return builder;
    }


}
