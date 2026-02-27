using EFCore.Observability.Core.Abstractions;
using EFCore.Observability.Core.Models;
using System.Diagnostics.Metrics;

namespace EFCore.Observability.OpenTelemetry;

/// <summary>
/// Exposes EF Core standard (non-pooled) DbContext metrics via <see cref="System.Diagnostics.Metrics.Meter"/>.
/// These are automatically picked up by OpenTelemetry, Prometheus, Datadog, and any
/// other collector that supports the .NET Metrics API.
/// </summary>
/// <remarks>
/// Meter name  : <c>EFCore.Standard</c><br/>
/// Meter version: <c>1.0.0</c>
/// </remarks>
public sealed class EFCoreStandardMeter : IDisposable
{
    public const string MeterName = "EFCore.Standard";
    public const string MeterVersion = "1.0.0";

    // Only _meter needs to be retained; the Meter owns all instruments internally.
    private readonly Meter _meter;
    private readonly IContextMetricsProvider _provider;

    /// <summary>
    /// Initialises the meter and registers all observable instruments.
    /// </summary>
    /// <param name="provider">Source of per-context standard metrics.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="provider"/> is <see langword="null"/>.</exception>
    public EFCoreStandardMeter(IContextMetricsProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        _provider = provider;
        _meter = new Meter(MeterName, MeterVersion);

        // ── Current state ─────────────────────────────────────────────────
        _meter.CreateObservableGauge(
            "efcore.standard.active",
            observeValues: () => Observe(m => Measure(m.ActiveContexts, m)),
            unit: "{instances}",
            description: "DbContext instances currently alive (created but not yet disposed).");

        _meter.CreateObservableGauge(
            "efcore.standard.leaks",
            observeValues: () => Observe(m => Measure(m.PotentialLeaks, m)),
            unit: "{contexts}",
            description: "DbContext instances that were created but never disposed (potential leaks).");

        // ── Lifetime duration (rolling window gauges) ─────────────────────
        _meter.CreateObservableGauge(
            "efcore.standard.duration.avg_ms",
            observeValues: () => Observe(m => Measure(m.AvgLifetimeMs, m)),
            unit: "ms",
            description: "Rolling average DbContext lifetime in milliseconds.");

        _meter.CreateObservableGauge(
            "efcore.standard.duration.min_ms",
            observeValues: () => Observe(m => Measure(m.MinLifetimeMs, m)),
            unit: "ms",
            description: "Minimum recorded DbContext lifetime in milliseconds.");

        _meter.CreateObservableGauge(
            "efcore.standard.duration.max_ms",
            observeValues: () => Observe(m => Measure(m.MaxLifetimeMs, m)),
            unit: "ms",
            description: "Maximum recorded DbContext lifetime in milliseconds.");

        // ── Cumulative counters ───────────────────────────────────────────
        _meter.CreateObservableCounter(
            "efcore.standard.creations.total",
            observeValues: () => Observe(m => Measure(m.TotalCreations, m)),
            unit: "{instances}",
            description: "Total standard DbContext instances ever created.");

        _meter.CreateObservableCounter(
            "efcore.standard.disposals.total",
            observeValues: () => Observe(m => Measure(m.TotalDisposals, m)),
            unit: "{instances}",
            description: "Total standard DbContext instances ever disposed.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Projects each standard context's metrics into an observable measurement sequence.
    /// </summary>
    private IEnumerable<Measurement<T>> Observe<T>(
        Func<StandardContextMetrics, Measurement<T>> selector)
        where T : struct
    {
        foreach (var metrics in _provider.GetAllStandardMetrics().Values)
            yield return selector(metrics);
    }

    /// <summary>
    /// Creates a <see cref="Measurement{T}"/> tagged with the context name.
    /// </summary>
    private static Measurement<T> Measure<T>(T value, StandardContextMetrics m)
        where T : struct =>
        new(value, new KeyValuePair<string, object?>("db.context", m.ContextName));

    /// <inheritdoc/>
    public void Dispose() => _meter.Dispose();
}