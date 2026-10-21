using System.Runtime.InteropServices;

namespace Citrus.Windows.Presentation;

/// <summary>Reference-counted capture affinity, shared by every live glass surface.</summary>
internal sealed class StudioCaptureExclusion
{
    private static long contentRevision;
    private static readonly Registry Shared = new(new NativeAffinity(), () => Interlocked.Increment(ref contentRevision));
    internal static long ContentRevision => Volatile.Read(ref contentRevision);
    internal static IDisposable? TryAcquire(nint[] windows) => Shared.TryAcquire(windows);

    internal interface IAffinityApi
    {
        bool IsOwnedTopLevelWindow(nint window);
        bool TryGet(nint window, out uint affinity);
        bool TrySet(nint window, uint affinity);
    }

    internal sealed class Registry(IAffinityApi api, Action? affinityChanged = null)
    {
        private readonly object gate = new();
        private readonly Dictionary<nint, Entry> entries = [];

        internal IDisposable? TryAcquire(nint[] windows)
        {
            ArgumentNullException.ThrowIfNull(windows);
            var distinct = windows.Distinct().ToArray();
            lock (gate)
            {
                // Validate the whole request before changing any native window. Capture affinity
                // is supported only for top-level HWNDs owned by this process.
                if (distinct.Length == 0 || distinct.Any(window => window == 0 || !api.IsOwnedTopLevelWindow(window)))
                    return null;
                var acquired = new List<nint>(distinct.Length);
                foreach (nint window in distinct)
                {
                    if (entries.TryGetValue(window, out var existing))
                    {
                        if (!api.TryGet(window, out uint active) || active != 0x11)
                        { ReleaseLocked(acquired); return null; }
                        existing.Count++;
                        acquired.Add(window);
                        continue;
                    }
                    if (!api.TryGet(window, out uint original))
                    { ReleaseLocked(acquired); return null; }
                    if ((original != 0x11 && !TrySet(window, 0x11)) ||
                        !api.TryGet(window, out uint verified) || verified != 0x11)
                    {
                        if (api.IsOwnedTopLevelWindow(window)) TrySet(window, original);
                        ReleaseLocked(acquired);
                        return null;
                    }
                    entries.Add(window, new Entry(original));
                    acquired.Add(window);
                }
                return new Lease(this, acquired);
            }
        }

        private void ReleaseLocked(IEnumerable<nint> windows)
        {
            foreach (nint window in windows)
            {
                if (!entries.TryGetValue(window, out var entry) || --entry.Count != 0) continue;
                entries.Remove(window);
                if (api.IsOwnedTopLevelWindow(window)) TrySet(window, entry.Original);
            }
        }

        private bool TrySet(nint window, uint affinity)
        {
            if (!api.TrySet(window, affinity)) return false;
            affinityChanged?.Invoke();
            return true;
        }

        private sealed class Entry(uint original)
        { internal uint Original { get; } = original; internal int Count = 1; }

        private sealed class Lease(Registry registry, List<nint> windows) : IDisposable
        {
            private List<nint>? acquired = windows;
            public void Dispose()
            {
                lock (registry.gate)
                {
                    if (acquired is null) return;
                    registry.ReleaseLocked(acquired);
                    acquired = null;
                }
            }
        }
    }

    private sealed class NativeAffinity : IAffinityApi
    {
        public bool IsOwnedTopLevelWindow(nint window) => BackdropNativeMethods.IsWindow(window) &&
            GetWindowThreadProcessId(window, out uint process) != 0 && process == Environment.ProcessId &&
            GetAncestor(window, 2) == window;
        public bool TryGet(nint window, out uint affinity) => GetWindowDisplayAffinity(window, out affinity);
        public bool TrySet(nint window, uint affinity) => SetWindowDisplayAffinity(window, affinity);
    }

    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(nint window, out uint affinity);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
}

/// <summary>A shared capture must not reuse pixels composed before a window's exclusion changed.</summary>
internal sealed class CaptureFrameBarrier
{
    private long acceptedRevision = -1, minimumTimestamp;
    internal long MinimumTimestamp => minimumTimestamp;
    internal void Initialize(long revision, long timestamp)
    { acceptedRevision = revision; minimumTimestamp = timestamp; }
    internal bool Refresh(long revision, Action discardCached, Func<long> flushAndTimestamp)
    {
        if (revision == acceptedRevision) return false;
        discardCached();
        minimumTimestamp = flushAndTimestamp();
        acceptedRevision = revision;
        return true;
    }
    internal bool Accept(long systemRelativeTicks) => systemRelativeTicks >= minimumTimestamp;
}
