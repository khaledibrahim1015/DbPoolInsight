namespace EFCore.Observability.Core.Enums;


/// <summary>
/// Represents the health status of a DbContext pool or instance.
/// </summary>
public enum ContextHealthStatus
{
    /// <summary>No data collected yet.</summary>
    Unknown = 0,

    /// <summary>All contexts returned, no leaks detected.</summary>
    Healthy = 1,

    /// <summary>Minor anomalies detected but within acceptable range.</summary>
    Warning = 2,

    /// <summary>One or more contexts were never returned to the pool (leaked).</summary>
    Leaking = 3
}

/// <summary>
/// Qualitative rating of pool reuse efficiency.
/// </summary>
public enum ReuseQuality
{
    Unknown = 0,
    Poor = 1,
    Fair = 2,
    Good = 3,
    VeryGood = 4,
    Excellent = 5
}