


using EFCore.Observability.Core.Abstractions;
using EFCore.Observability.Core.Models;
using System.Diagnostics.Metrics;

/// <summary>
/// Exposes EF Core standard metrics via <see cref="System.Diagnostics.Metrics.Meter"/>.
/// These are automatically picked up by OpenTelemetry, Prometheus, Datadog, and any
/// other collector that supports the .NET Metrics API.
///
/// Meter name: <c>EFCore.Standard</c>
/// </summary>
namespace EFCore.Observability.OpenTelemetry;

public sealed class EFCoreStandardMeter : IDisposable
{
    public const string MeterName = "EFCore.Standard";
    public const string MeterVersion = "1.0.0";

    private readonly Meter _meter;
    private readonly IContextMetricsProvider _provider;




    // ── standard dbcontext metrics ────────────────

    private readonly ObservableGauge<double> _avgDurationMs;
    private readonly ObservableGauge<double> _maxDurationMs;
    private readonly ObservableGauge<double> _minDurationMs;

    private readonly ObservableGauge<long> _leakedStandardContexts;
    private readonly ObservableCounter<long> _totalCreations;
    private readonly ObservableCounter<long> _totalDisposals;
    private readonly ObservableGauge<long> _activeInUse;



    public EFCoreStandardMeter(IContextMetricsProvider provider)
    {
        _provider = provider;
        _meter = new Meter(MeterName, MeterVersion);


        // ── Standard DbContext ───────────────────────────────────────────────────────────────
        _leakedStandardContexts = _meter.CreateObservableGauge(
            "efcore.Standard.leaks",
            unit: "{contexts}",
            description: "DbContext instances that were created but never disposed (potential leaks).",
            observeValues: () => ObserveStandard(m => new Measurement<long>(m.PotentialLeaks, ContextTag(m))));


        _totalCreations = _meter.CreateObservableCounter(
          "efcore.standard.physical_creations.total",
          unit: "{instances}",
          description: "Total standard DbContext instances ever created.",
          observeValues: () => ObserveStandard(m => new Measurement<long>(m.TotalCreations, ContextTag(m))));

        _totalDisposals = _meter.CreateObservableCounter(
            "efcore.standard.physical_disposals.total",
            unit: "{instances}",
            description: "Total standard DbContext instances ever disposed.",
            observeValues: () => ObserveStandard(m => new Measurement<long>(m.TotalDisposals, ContextTag(m))));


        _activeInUse = _meter.CreateObservableGauge(
            "efcore.standard.active",
            unit: "{rents}",
            description: "DbContext instances currently alive (created but not yet disposed).",
            observeValues: () => ObserveStandard(m => new Measurement<long>(m.ActiveContexts, ContextTag(m))));




        _avgDurationMs = _meter.CreateObservableGauge(
            "efcore.standard.duration.avg_ms",
            unit: "ms",
            description: "Rolling average duration in milliseconds.",
            observeValues: () => ObserveStandard(m => new Measurement<double>(m.AvgLifetimeMs, ContextTag(m))));

        _minDurationMs = _meter.CreateObservableGauge(
          "efcore.standard.duration.min_ms",
          unit: "ms",
          description: "Minimum recorded duration in milliseconds.",
          observeValues: () => ObserveStandard(m => new Measurement<double>(m.MinLifetimeMs, ContextTag(m))));

        _maxDurationMs = _meter.CreateObservableGauge(
            "efcore.standard.duration.max_ms",
            unit: "ms",
            description: "Maximum recorded duration in milliseconds.",
            observeValues: () => ObserveStandard(m => new Measurement<double>(m.MaxLifetimeMs, ContextTag(m))));


    }
    private IEnumerable<Measurement<T>> ObserveStandard<T>(
        Func<StandardContextMetrics, Measurement<T>> selector)
        where T : struct
    {
        foreach (var metrics in _provider.GetAllStandardMetrics().Values)
            yield return selector(metrics);
    }

    private static KeyValuePair<string, object?>[] ContextTag(StandardContextMetrics m) =>
        [ new KeyValuePair<string, object?>("db.context", m.ContextName)];


    public void Dispose() => _meter.Dispose();

}
