

using EFCore.Observability.Core.Models;

namespace EFCore.Observability.Core.Abstractions;

/// <summary>
/// Stores and retrieves per-instance lifecycle activity records.
/// </summary>
public interface IInstanceActivityStore
{
    /// <summary>Appends a completed activity record for the given context.</summary>
    void Record(string contextName, InstanceActivity activity);

    /// <summary>Returns the most recent <paramref name="take"/> records for the context.</summary>
    IReadOnlyList<InstanceActivity> GetRecent(string contextName, int take = 20);

    /// <summary>Returns all stored records for the context.</summary>
    IReadOnlyList<InstanceActivity> GetAll(string contextName);

    /// <summary>Clears all stored records for the context.</summary>
    void Clear(string contextName);
}