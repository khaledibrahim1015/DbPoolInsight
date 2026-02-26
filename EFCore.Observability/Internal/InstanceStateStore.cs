

using System.Collections.Concurrent;

namespace EFCore.Observability.Internal;
/// <summary>
/// Thread-safe store for tracking per-instance state during its active lifetime.
/// Entries are removed when the instance is disposed or returned to pool.
/// </summary>
internal class InstanceStateStore
{
    private readonly ConcurrentDictionary<Guid, InstanceState> _states = new();





    public void AddOrUpdateState(Guid instanceId, InstanceState state)
        =>  _states[instanceId] = state;

    public bool TryRemoveState(Guid instanceId, out InstanceState? state)
        => _states.TryRemove(instanceId, out state);


}



/// <summary>
/// Mutable state for a single DbContext instance while it is alive.
/// </summary>
internal sealed class InstanceState
{
    public string ContextName { get; set; } = string.Empty;
    public bool IsPooled { get; set; }
    public int CurrentLease { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastRented { get; set; }
    public DateTime? LastReturned { get; set; }
    public bool WasReturnedToPool { get; set; }

    /// <summary>
    /// True when the instance was physically created after the pool was already at MaxPoolSize.
    /// This flag is set once at creation and never cleared.
    /// </summary>
    public bool IsOverflow { get; set; }
}