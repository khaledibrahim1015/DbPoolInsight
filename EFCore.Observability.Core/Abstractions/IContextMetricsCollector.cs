

namespace EFCore.Observability.Core.Abstractions;

/// <summary>
/// Base interface for DbContext metrics collection.
/// </summary>
public interface IContextMetricsCollector
{
    /// <summary>
    /// Called when a physical DbContext instance is first created.
    /// For pooled contexts this fires once per instance, not once per rent.
    /// </summary>
    void OnContextInitialized(string contextName, Guid instanceId, int lease, bool isPooled);

    /// <summary>
    /// Called when a pooled context is rented (borrowed from the pool).
    /// Fires on every rent — including first use.
    /// </summary>
    void OnContextRented(string contextName, Guid instanceId, int lease);

    /// <summary>
    /// Called when a pooled context's ResetState is invoked, meaning it was
    /// successfully returned to the pool for reuse.
    /// </summary>
    void OnContextReturnedToPool(string contextName, Guid instanceId, int lease);

    /// <summary>
    /// Called when a pooled context instance is physically disposed.
    /// This can happen due to pool overflow, shutdown, or a genuine leak.
    /// </summary>
    void OnPooledContextDisposed(string contextName, Guid instanceId, int lease);

    /// <summary>
    /// Called when a standard (non-pooled) DbContext instance is disposed.
    /// </summary>
    void OnStandardContextDisposed(string contextName, Guid instanceId);
}
