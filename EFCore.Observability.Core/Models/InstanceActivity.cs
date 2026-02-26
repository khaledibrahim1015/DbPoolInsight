

namespace EFCore.Observability.Core.Models;


/// <summary>
/// Represents a single recorded lifecycle event for a DbContext instance.
/// For pooled contexts this is a rent/return cycle; for standard contexts it is the full lifetime.
/// </summary>
public sealed record InstanceActivity
{
    /// <summary>First 8 characters of the instance GUID for human-readable logging.</summary>
    public string InstanceId { get; init; } = string.Empty;

    /// <summary>EF Core's lease counter — increments on every pool reuse.</summary>
    public int Lease { get; init; }

    /// <summary>When the context was rented / created.</summary>
    public DateTime StartedAt { get; init; }

    /// <summary>When the context was returned / disposed. Null if still active.</summary>
    public DateTime? EndedAt { get; init; }

    /// <summary>Duration in milliseconds. Null if still active.</summary>
    public long? DurationMs { get; init; }

    /// <summary>Whether this activity record is still open (context not yet returned).</summary>
    public bool IsActive => EndedAt is null;
}
