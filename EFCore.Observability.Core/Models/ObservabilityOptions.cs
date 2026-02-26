namespace EFCore.Observability.Core.Models;


/// <summary>
/// Configuration options for EFCore.Observability.
/// </summary>
public sealed class ObservabilityOptions
{
    /// <summary>
    /// Maximum number of recent activity records to retain per context.
    /// Older entries are dropped automatically (ring-buffer behaviour).
    /// Default: 500
    /// </summary>
    public int MaxActivityHistoryPerContext { get; set; } = 500;

    /// <summary>
    /// Duration in milliseconds after which a rented context is considered a potential leak
    /// and a warning is emitted. Default: 30 000 ms (30 s).
    /// </summary>
    public long LeakDetectionThresholdMs { get; set; } = 30_000;

    /// <summary>
    /// Whether to track per-rent duration histograms.
    /// Disabling reduces allocation overhead in extreme high-throughput scenarios.
    /// Default: true
    /// </summary>
    public bool TrackRentDurations { get; set; } = true;

    /// <summary>
    /// Whether to emit structured log messages for lifecycle events.
    /// Default: true
    /// </summary>
    public bool EnableDiagnosticLogging { get; set; } = true;

    /// <summary>
    /// Whether to expose OpenTelemetry metrics via System.Diagnostics.Metrics.
    /// Requires the EFCore.Observability.OpenTelemetry package.
    /// Default: false (opt-in)
    /// </summary>
    public bool EnableOpenTelemetry { get; set; } = false;
}
