namespace Citrus.Windows.Presentation;

/// <summary>Idle allocations only: active surfaces are never evicted or counted as reusable memory.</summary>
internal sealed class StudioGlassAllocationCache<T>(int maximumEntries, long maximumBytes,
    Func<T, long> size, Action<T> clear) : IDisposable where T : class, IDisposable
{
    private readonly object gate = new();
    private readonly LinkedList<Entry> idle = new();
    private long bytes;
    private bool disposed;
    internal int IdleCount { get { lock (gate) return idle.Count; } }
    internal long IdleBytes { get { lock (gate) return bytes; } }

    internal T Rent(object key, Func<T> create)
        => Rent(key, create, out _);

    internal T Rent(object key, Func<T> create, out bool prepared)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            for (var node = idle.First; node is not null; node = node.Next)
            {
                if (!Equals(node.Value.Key, key)) continue;
                idle.Remove(node); bytes -= node.Value.Bytes;
                prepared = true;
                return node.Value.Allocation;
            }
            prepared = false;
            return create();
        }
    }

    internal bool Return(object key, T allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        lock (gate)
        {
            if (disposed) { allocation.Dispose(); return false; }
            long allocationBytes;
            try
            {
                // Clearing is part of publication, never deferred until the next renter.
                clear(allocation);
                allocationBytes = size(allocation);
            }
            catch
            {
                allocation.Dispose();
                return false;
            }
            if (allocationBytes <= 0 || allocationBytes > maximumBytes || maximumEntries <= 0)
            { allocation.Dispose(); return false; }
            // Concurrent surfaces can have matching allocations. Only the most recently
            // returned one needs to stay idle; active leases remain independently owned.
            for (var node = idle.First; node is not null;)
            {
                var next = node.Next;
                if (Equals(node.Value.Key, key)) Remove(node);
                node = next;
            }
            idle.AddLast(new Entry(key, allocation, allocationBytes));
            bytes += allocationBytes;
            while (idle.Count > maximumEntries || bytes > maximumBytes) Remove(idle.First!);
            return true;
        }
    }

    private void Remove(LinkedListNode<Entry> node)
    {
        idle.Remove(node); bytes -= node.Value.Bytes;
        node.Value.Allocation.Dispose();
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            while (idle.First is { } first) Remove(first);
        }
    }

    private sealed record Entry(object Key, T Allocation, long Bytes);
}
