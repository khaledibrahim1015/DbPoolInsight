

using System.Collections.Concurrent;

namespace EFCore.Observability.Internal;
/// <summary>
/// Thread-safe store for tracking per-instance state during its active lifetime.
/// Entries are removed when the instance is disposed or returned to pool.
/// </summary>
internal class InstanceStateStore
{

    private readonly ConcurrentDictionary<Guid, InstanceState> _states = new();
    /// <summary>Tracks all instance IDs seen per context to detect physical creations.</summary>
    private readonly ConcurrentDictionary<string , ConcurrentDictionary<Guid , bool>> _seenInstances = new();
    /// <summary>Tracks currently rented instances per context for ActiveRents calculation.</summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, bool>> _rentedInstances = new();



    // InstanceStates
    public void AddOrUpdateState(Guid instanceId, InstanceState state)
        =>  _states[instanceId] = state;
    public void UpdateState(Guid instanceId, Action<InstanceState> updateAction)
    {
        if(_states.TryGetValue(instanceId, out var existing))
        {
            updateAction(existing);
            return;
        }
    }
    public bool TryUpdateState(Guid instanceId, Action<InstanceState> updateAction)
    {
        if (_states.TryGetValue(instanceId, out var existing))
        {
            updateAction(existing);
            return true;
        }
        return false;
    }
    public bool TryGetState(Guid instanceId, out InstanceState? state) =>
     _states.TryGetValue(instanceId, out state);

    public bool TryRemoveState(Guid instanceId, out InstanceState? state) =>
        _states.TryRemove(instanceId, out state);

    //_seenInstances
    public bool TryAddSeen(string contextName, Guid instanceId)
    {
        var seen = _seenInstances.GetOrAdd(contextName, _ => new ConcurrentDictionary<Guid, bool>());
        return seen.TryAdd(instanceId, true);
    }




    //_rentedInstances
    public bool TryAddRented(string contextName, Guid instanceId)
    {
        var rented = _rentedInstances.GetOrAdd(contextName, _ => new ConcurrentDictionary<Guid, bool>());
        return rented.TryAdd(instanceId, true);
    }
    public void RemoveRented(string contextName, Guid instanceId)
    {
        if (_rentedInstances.TryGetValue(contextName, out var rented))
            rented.TryRemove(instanceId, out _);
    }


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