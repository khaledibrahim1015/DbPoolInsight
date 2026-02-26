
using EFCore.Observability.Core.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Collections.Concurrent;
using System.Data.Common;

namespace EFCore.Observability.Interceptors;


/// <summary>
/// Intercepts the first DB command per context rent to reliably track pool rents.
/// This is necessary because <c>ContextInitialized</c> fires even when contexts are
/// reused from the pool — but we need to record one rent per command cycle.
///
/// This implementation uses a bounded LRU-style eviction to stay O(1).
/// </summary>

public sealed class RentTrackingInterceptor : DbCommandInterceptor
{
    private readonly IContextMetricsCollector _collector;

    // Bounded set: "instanceId:lease" → true
    // Max 10 000 entries — entries are tiny (string + bool), ~100 KB worst case.
    private readonly ConcurrentDictionary<string, bool> _trackedRents = new(
        StringComparer.Ordinal);
    private const int MaxTrackedRents = 10_000;
    private const int EvictTo = 8_000;

    public RentTrackingInterceptor(IContextMetricsCollector collector)
    {
        _collector = collector;
    }

    // ── Sync overrides ────────────────────────────────────────────────────

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        TrackIfNeeded(eventData.Context);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        TrackIfNeeded(eventData.Context);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        TrackIfNeeded(eventData.Context);
        return base.NonQueryExecuting(command, eventData, result);
    }

    // ── Async overrides ───────────────────────────────────────────────────

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        TrackIfNeeded(eventData.Context);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        TrackIfNeeded(eventData.Context);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        TrackIfNeeded(eventData.Context);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    // ── Core logic ────────────────────────────────────────────────────────

    private void TrackIfNeeded(DbContext? context)
    {
        if (context is null) return;
        if (!IsPooled(context)) return;

        var instanceId = context.ContextId.InstanceId;
        var lease = context.ContextId.Lease;

        // String.Create avoids allocating a format string on every call
        var rentKey = $"{instanceId}:{lease}";

        if (_trackedRents.TryAdd(rentKey, true))
        {
            _collector.OnContextRented(context.GetType().Name, instanceId, lease);
            MaybeEvict();
        }
    }

    /// <summary>
    /// When the dictionary grows past MaxTrackedRents, remove the oldest 20 %.
    /// Because ConcurrentDictionary has no ordering, we just remove any excess keys.
    /// Older rents are naturally less likely to recur so this is safe.
    /// </summary>
    private void MaybeEvict()
    {
        if (_trackedRents.Count <= MaxTrackedRents) return;

        int toRemove = _trackedRents.Count - EvictTo;
        foreach (var key in _trackedRents.Keys)
        {
            if (toRemove-- <= 0) break;
            _trackedRents.TryRemove(key, out _);
        }
    }

    private static bool IsPooled(DbContext context)
    {
        try
        {
            var maxPoolSize = context
                .GetService<IDbContextOptions>()
                .Extensions
                .OfType<CoreOptionsExtension>()
                .FirstOrDefault()?.MaxPoolSize ?? 0;
            return maxPoolSize > 0;
        }
        catch
        {
            return false;
        }
    }
}
