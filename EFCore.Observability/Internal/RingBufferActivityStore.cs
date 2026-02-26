
using EFCore.Observability.Core.Abstractions;
using EFCore.Observability.Core.Models;
using System.Collections.Concurrent;

namespace EFCore.Observability.Internal;
/// <summary>
/// A bounded, thread-safe activity store using a circular-buffer approach.
/// When the capacity is reached, the oldest entry is dropped automatically.
/// This replaces the unbounded ConcurrentQueue + manual cleanup from the original code.
/// </summary>
internal sealed class RingBufferActivityStore : IInstanceActivityStore
{
    private readonly int _mxCapacity;
    private readonly ConcurrentDictionary<string, BoundedQueue> _queues = new();

    public RingBufferActivityStore(int maxCapacity = 500) =>  _mxCapacity = maxCapacity;


    public void Record(string contextName, InstanceActivity activity)
    {
       var queue =  _queues.GetOrAdd(contextName, _ => new BoundedQueue(_mxCapacity));
        queue.Enqueue(activity);
    }
    public IReadOnlyList<InstanceActivity> GetRecent(string contextName, int take = 20)
    {
        if(!_queues.TryGetValue(contextName, out var queue))
            return [];
        return queue.TakeLast(take);
    }
    public IReadOnlyList<InstanceActivity> GetAll(string contextName)
    {
        if (!_queues.TryGetValue(contextName, out var queue))
            return [];
        return queue.ToList();
    }
    public void Clear(string contextName)
    {
        if (_queues.TryGetValue(contextName, out var queue))
            queue.Clear();
    }

 

  


    // ── Inner helper ──────────────────────────────────────────────────────
    private sealed class BoundedQueue
    {
        private readonly int _capacity;
        private readonly object _lock = new();
        private readonly Queue<InstanceActivity> _inner ;
        public BoundedQueue(int capacity)
        {
            _capacity   = capacity;
            _inner = new Queue<InstanceActivity>(capacity);
        }
        public void Enqueue(InstanceActivity activity)
        {
            lock (_lock)
            {
                if (_inner.Count >=  _capacity)
                    _inner.Dequeue(); // Drop oldest
                _inner.Enqueue(activity);
            }
        }

        public List<InstanceActivity> TakeLast(int count )
        {
            lock (_lock)
            {
                var all  = _inner.ToArray();
                var skip  =  Math.Max(0, all.Length - count);
                return all.Skip(skip).ToList();
            }
        }

        public List<InstanceActivity> ToList()
        {
            lock (_lock) return [.. _inner];
        }

        public void Clear()
        {
            lock (_lock) _inner.Clear();
        }


    }


}




