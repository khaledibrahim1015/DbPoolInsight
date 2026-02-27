

using EFCore.Observability.Core.Abstractions;
using EFCore.Observability.Core.Models;
using System.Diagnostics.Metrics;

namespace EFCore.Observability.OpenTelemetry;


/// <summary>
/// Exposes EF Core pool metrics via <see cref="System.Diagnostics.Metrics.Meter"/>.
/// These are automatically picked up by OpenTelemetry, Prometheus, Datadog, and any
/// other collector that supports the .NET Metrics API.
///
/// Meter name: <c>EFCore.Pool</c>
/// </summary>
public sealed class EFCorePoolMeter : IDisposable
{
    public const string MeterName = "EFCore.Pool";
    public const string MeterVersion = "1.0.0";

    private readonly Meter _meter;
    private readonly IContextMetricsProvider _provider;

    // ── Observable gauges (polled by the metrics pipeline) ────────────────
    private readonly ObservableGauge<long> _physicalInPool;
    private readonly ObservableGauge<long> _availableInPool;
    private readonly ObservableGauge<long> _activeRents;
    private readonly ObservableGauge<double> _poolUtilization;
    private readonly ObservableGauge<long> _leakedContexts;

    // ── Cumulative counters ───────────────────────────────────────────────
    private readonly ObservableCounter<long> _totalRents;
    private readonly ObservableCounter<long> _totalReturns;
    private readonly ObservableCounter<long> _overflowDisposals;
    private readonly ObservableCounter<long> _physicalCreations;
    private readonly ObservableCounter<long> _physicalDisposals;

    // ── Histograms (pre-aggregated averages — true histograms need per-event recording)
    private readonly ObservableGauge<double> _avgRentDurationMs;
    private readonly ObservableGauge<double> _maxRentDurationMs;
    private readonly ObservableGauge<double> _minRentDurationMs;





    public EFCorePoolMeter(IContextMetricsProvider provider)
    {
        _provider = provider;
        _meter = new Meter(MeterName, MeterVersion);


        // ── DbContextPool ───────────────────────────────────────────────────────────────
        // ── Gauges ────────────────────────────────────────────────────────

        _physicalInPool = _meter.CreateObservableGauge(
            "efcore.pool.instances.physical",
            unit: "{instances}",
            description: "Number of physical DbContext instances currently held in the pool.",
            observeValues: () => ObservePooled(m => new Measurement<long>(m.PhysicalInPool, ContextTag(m))));

        _availableInPool = _meter.CreateObservableGauge(
            "efcore.pool.instances.available",
            unit: "{instances}",
            description: "Idle DbContext instances ready to be rented.",
            observeValues: () => ObservePooled(m => new Measurement<long>(m.AvailableInPool, ContextTag(m))));

        _activeRents = _meter.CreateObservableGauge(
            "efcore.pool.rents.active",
            unit: "{rents}",
            description: "DbContext instances currently rented (in use).",
            observeValues: () => ObservePooled(m => new Measurement<long>(m.ActiveRents, ContextTag(m))));

        _poolUtilization = _meter.CreateObservableGauge(
            "efcore.pool.utilization",
            unit: "%",
            description: "Pool utilization as a percentage of MaxPoolSize.",
            observeValues: () => ObservePooled(m => new Measurement<double>(m.PoolUtilization, ContextTag(m))));

        _leakedContexts = _meter.CreateObservableGauge(
            "efcore.pool.leaks",
            unit: "{contexts}",
            description: "DbContext instances that were rented but never returned (leaks).",
            observeValues: () => ObservePooled(m => new Measurement<long>(m.LeakedContexts, ContextTag(m))));

        _avgRentDurationMs = _meter.CreateObservableGauge(
            "efcore.pool.rent.duration.avg_ms",
            unit: "ms",
            description: "Rolling average rent duration in milliseconds.",
            observeValues: () => ObservePooled(m => new Measurement<double>(m.AvgRentDurationMs, ContextTag(m))));

          _minRentDurationMs = _meter.CreateObservableGauge(
            "efcore.pool.rent.duration.min_ms",
            unit: "ms",
            description: "Minimum recorded rent duration in milliseconds.",
            observeValues: () => ObservePooled(m => new Measurement<double>(m.MinRentDurationMs, ContextTag(m))));

        _maxRentDurationMs = _meter.CreateObservableGauge(
            "efcore.pool.rent.duration.max_ms",
            unit: "ms",
            description: "Maximum recorded rent duration in milliseconds.",
            observeValues: () => ObservePooled(m => new Measurement<double>(m.MaxRentDurationMs, ContextTag(m))));

        // ── Counters ──────────────────────────────────────────────────────

        _totalRents = _meter.CreateObservableCounter(
            "efcore.pool.rents.total",
            unit: "{rents}",
            description: "Cumulative total pool rents since application start.",
            observeValues: () => ObservePooled(m => new Measurement<long>(m.TotalRents, ContextTag(m))));

        _totalReturns = _meter.CreateObservableCounter(
            "efcore.pool.returns.total",
            unit: "{returns}",
            description: "Cumulative total pool returns since application start.",
            observeValues: () => ObservePooled(m => new Measurement<long>(m.TotalReturns, ContextTag(m))));

        _overflowDisposals = _meter.CreateObservableCounter(
            "efcore.pool.overflow_disposals.total",
            unit: "{disposals}",
            description: "Contexts disposed due to pool overflow (normal under load).",
            observeValues: () => ObservePooled(m => new Measurement<long>(m.OverflowDisposals, ContextTag(m))));

        _physicalCreations = _meter.CreateObservableCounter(
            "efcore.pool.physical_creations.total",
            unit: "{instances}",
            description: "Total physical DbContext instances ever created.",
            observeValues: () => ObservePooled(m => new Measurement<long>(m.PhysicalCreations, ContextTag(m))));

        _physicalDisposals = _meter.CreateObservableCounter(
            "efcore.pool.physical_disposals.total",
            unit: "{instances}",
            description: "Total physical DbContext instances ever disposed.",
            observeValues: () => ObservePooled(m => new Measurement<long>(m.PhysicalDisposals, ContextTag(m))));




    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private IEnumerable<Measurement<T>> ObservePooled<T>(
        Func<PooledContextMetrics, Measurement<T>> selector)
        where T : struct
    {
        foreach (var metrics in _provider.GetAllPooledMetrics().Values)
            yield return selector(metrics);
    }

    private static KeyValuePair<string, object?>[] ContextTag(PooledContextMetrics m) =>
        [new KeyValuePair<string, object?>("db.context", m.ContextName)];

    public void Dispose() => _meter.Dispose();
}
