using EFCore.Observability.Core.Abstractions;
using EFCore.Observability.Core.Models;
using System.Diagnostics.Metrics;

namespace EFCore.Observability.OpenTelemetry;

/// <summary>
/// Exposes EF Core pool metrics via <see cref="System.Diagnostics.Metrics.Meter"/>.
/// These are automatically picked up by OpenTelemetry, Prometheus, Datadog, and any
/// other collector that supports the .NET Metrics API.
/// </summary>
/// <remarks>
/// Meter name  : <c>EFCore.Pool</c><br/>
/// Meter version: <c>1.0.0</c>
/// </remarks>
public sealed class EFCorePoolMeter : IDisposable
{
    public const string MeterName = "EFCore.Pool";
    public const string MeterVersion = "1.0.0";

    // Only _meter needs to be retained; the Meter owns all instruments internally.
    private readonly Meter _meter;
    private readonly IContextMetricsProvider _provider;

    /// <summary>
    /// Initialises the meter and registers all observable instruments.
    /// </summary>
    /// <param name="provider">Source of per-context pool metrics.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="provider"/> is <see langword="null"/>.</exception>
    public EFCorePoolMeter(IContextMetricsProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        _provider = provider;
        _meter = new Meter(MeterName, MeterVersion);

        // ── Configuration ─────────────────────────────────────────────────
        _meter.CreateObservableGauge(
            "efcore.pool.max_size",
            observeValues: () => Observe(m => Measure(m.MaxPoolSize, m)),
            unit: "{instances}",
            description: "Configured maximum pool size.");

        _meter.CreateObservableGauge(
            "efcore.pool.room_to_grow",
            observeValues: () => Observe(m => Measure(m.RoomToGrow, m)),
            unit: "{instances}",
            description: "Number of additional physical DbContext instances that can be created before the pool is full.");

        // ── Current state ─────────────────────────────────────────────────
        _meter.CreateObservableGauge(
            "efcore.pool.instances.physical",
            observeValues: () => Observe(m => Measure(m.PhysicalInPool, m)),
            unit: "{instances}",
            description: "Number of physical DbContext instances currently held in the pool.");

        _meter.CreateObservableGauge(
            "efcore.pool.instances.available",
            observeValues: () => Observe(m => Measure(m.AvailableInPool, m)),
            unit: "{instances}",
            description: "Idle DbContext instances ready to be rented.");

        _meter.CreateObservableGauge(
            "efcore.pool.rents.active",
            observeValues: () => Observe(m => Measure(m.ActiveRents, m)),
            unit: "{rents}",
            description: "DbContext instances currently rented (in use).");

        _meter.CreateObservableGauge(
            "efcore.pool.utilization",
            observeValues: () => Observe(m => Measure(m.PoolUtilization, m)),
            unit: "%",
            description: "Pool utilization as a percentage of MaxPoolSize.");

        _meter.CreateObservableGauge(
            "efcore.pool.reuse_ratio",
            observeValues: () => Observe(m => Measure(m.ReuseRatio, m)),
            unit: "{instances}",
            description: "Average number of requests handled per physical instance.");

        _meter.CreateObservableGauge(
            "efcore.pool.return_rate",
            observeValues: () => Observe(m => Measure(m.ReturnRate, m)),
            unit: "%",
            description: "Percentage of rents that resulted in a clean return.");

        _meter.CreateObservableGauge(
            "efcore.pool.leaks",
            observeValues: () => Observe(m => Measure(m.LeakedContexts, m)),
            unit: "{contexts}",
            description: "DbContext instances that were rented but never returned (potential leaks).");

        // ── Rent duration (rolling window gauges) ─────────────────────────
        _meter.CreateObservableGauge(
            "efcore.pool.rent.duration.avg_ms",
            observeValues: () => Observe(m => Measure(m.AvgRentDurationMs, m)),
            unit: "ms",
            description: "Rolling average rent duration in milliseconds.");

        _meter.CreateObservableGauge(
            "efcore.pool.rent.duration.min_ms",
            observeValues: () => Observe(m => Measure(m.MinRentDurationMs, m)),
            unit: "ms",
            description: "Minimum recorded rent duration in milliseconds.");

        _meter.CreateObservableGauge(
            "efcore.pool.rent.duration.max_ms",
            observeValues: () => Observe(m => Measure(m.MaxRentDurationMs, m)),
            unit: "ms",
            description: "Maximum recorded rent duration in milliseconds.");

        // ── Cumulative counters ───────────────────────────────────────────
        _meter.CreateObservableCounter(
            "efcore.pool.rents.total",
            observeValues: () => Observe(m => Measure(m.TotalRents, m)),
            unit: "{rents}",
            description: "Cumulative total pool rents since application start.");

        _meter.CreateObservableCounter(
            "efcore.pool.returns.total",
            observeValues: () => Observe(m => Measure(m.TotalReturns, m)),
            unit: "{returns}",
            description: "Cumulative total pool returns since application start.");

        _meter.CreateObservableCounter(
            "efcore.pool.overflow_disposals.total",
            observeValues: () => Observe(m => Measure(m.OverflowDisposals, m)),
            unit: "{disposals}",
            description: "Contexts disposed due to pool overflow (normal under sustained load).");

        _meter.CreateObservableCounter(
            "efcore.pool.physical_creations.total",
            observeValues: () => Observe(m => Measure(m.PhysicalCreations, m)),
            unit: "{instances}",
            description: "Total physical DbContext instances ever created.");

        _meter.CreateObservableCounter(
            "efcore.pool.physical_disposals.total",
            observeValues: () => Observe(m => Measure(m.PhysicalDisposals, m)),
            unit: "{instances}",
            description: "Total physical DbContext instances ever disposed.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Projects each pooled context's metrics into an observable measurement sequence.
    /// </summary>
    private IEnumerable<Measurement<T>> Observe<T>(
        Func<PooledContextMetrics, Measurement<T>> selector)
        where T : struct
    {
        foreach (var metrics in _provider.GetAllPooledMetrics().Values)
            yield return selector(metrics);
    }

    /// <summary>
    /// Creates a <see cref="Measurement{T}"/> tagged with the context name.
    /// The tag array is allocated once per call; avoid caching it across calls
    /// since measurements are consumed immediately by the metrics pipeline.
    /// </summary>
    private static Measurement<T> Measure<T>(T value, PooledContextMetrics m)
        where T : struct =>
        new(value, new KeyValuePair<string, object?>("db.context", m.ContextName));

    /// <inheritdoc/>
    public void Dispose() => _meter.Dispose();
}