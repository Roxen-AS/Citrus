using System.Runtime.InteropServices;

namespace Citrus.Windows.Presentation;

/// <summary>One serialized desktop GPU stream per monitor, shared across visible glass surfaces.</summary>
internal static class DesktopGlassCapturePool
{
    internal static object GpuGate { get; } = new();
    private static readonly Registry Shared = new(
        (x, y) => MonitorFromPoint(new NativePoint(x, y), 0),
        CreateSharedSource);

    internal static Lease? TryAcquire(int screenX, int screenY) => Shared.TryAcquire(screenX, screenY);
    internal static Task RetirementCompletion => Shared.RetirementCompletion;

    internal static IReadOnlyList<GlassMonitor> Monitors()
    {
        var monitors = new List<GlassMonitor>();
        EnumerateMonitors callback = (nint monitor, nint _, ref NativeRect bounds, nint _) =>
        {
            long width = (long)bounds.Right - bounds.Left, height = (long)bounds.Bottom - bounds.Top;
            if (width is > 0 and <= int.MaxValue && height is > 0 and <= int.MaxValue)
                monitors.Add(new(monitor, bounds.Left, bounds.Top, (int)width, (int)height));
            return true;
        };
        if (!EnumDisplayMonitors(0, 0, callback, 0)) throw new InvalidOperationException("Monitor enumeration failed.");
        return monitors;
    }

    private static ISource? CreateSharedSource(nint monitor, int x, int y)
    {
        // Retain borrowed pointers before leaving the short device lock. WinRT
        // setup, monitor enumeration and StartCapture cannot hold that lock.
        using var device = StudioGlassRuntime.Shared.AcquireDevice();
        var source = DesktopGlassSource.TryCreateMonitorAt(x, y, device.Device, device.Context);
        if (source is null) return null;
        if (source.Monitor != monitor)
        { source.Dispose(); return null; } // Display topology changed during creation.
        return new SharedSource(source);
    }

    private sealed class SharedSource(DesktopGlassSource source) : ISource
    {
        private bool disposed;
        public string Diagnostic => source.Diagnostic;
        public long FrameVersion => source.FrameVersion;
        public bool TryUseLatestFrame(Action<nint, nint, nint, int, int, int, int> useFrame)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            // The entry lock owns the source; it contains its own short native
            // command locks so capture restarts stay outside the GPU gate.
            return source.TryUseLatestFrame(useFrame);
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            source.Dispose();
        }
    }

    internal interface ISource : IDisposable
    {
        bool TryUseLatestFrame(Action<nint, nint, nint, int, int, int, int> useFrame);
        // Unknown/test sources opt out of version-based suppression.
        long FrameVersion => -1;
        string Diagnostic => "No source diagnostics.";
    }

    internal sealed class Registry(Func<int, int, nint> resolveMonitor, Func<nint, int, int, ISource?> createSource)
    {
        private readonly object gate = new();
        private readonly Dictionary<nint, MonitorSlot> slots = [];
        private readonly HashSet<Task> operations = [];
        internal Task RetirementCompletion { get { lock (gate) return Task.WhenAll(operations); } }

        internal Lease? TryAcquire(int screenX, int screenY)
        {
            nint monitor = resolveMonitor(screenX, screenY);
            if (monitor == 0) return null;
            MonitorSlot slot;
            lock (gate)
            {
                if (!slots.TryGetValue(monitor, out slot!)) slots.Add(monitor, slot = new(monitor));
                // A reservation prevents replacing this slot while an acquirer
                // waits for the preceding native shutdown on the same monitor.
                slot.Reservations++;
            }
            bool transferred = false;
            try
            {
                lock (slot.Gate)
                {
                    if (slot.Active is null)
                    {
                        var operation = BeginOperation();
                        try
                        {
                            var source = createSource(monitor, screenX, screenY);
                            if (source is null) return null;
                            slot.Active = new(monitor, source, slot);
                        }
                        finally { EndOperation(operation); }
                    }
                    var entry = slot.Active;
                    var lease = new Lease(this, entry);
                    entry.References++;
                    transferred = true;
                    return lease;
                }
            }
            finally { if (!transferred) ReleaseReservation(slot); }
        }

        private TaskCompletionSource BeginOperation()
        {
            var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (gate) operations.Add(operation.Task);
            return operation;
        }

        private void EndOperation(TaskCompletionSource operation)
        {
            lock (gate) operations.Remove(operation.Task);
            operation.TrySetResult();
        }

        private void ReleaseReservation(MonitorSlot slot)
        {
            lock (gate)
            {
                if (--slot.Reservations == 0 && slots.TryGetValue(slot.Monitor, out var current) && ReferenceEquals(current, slot))
                    slots.Remove(slot.Monitor);
            }
        }

        internal void Release(Entry entry)
        {
            var slot = entry.Slot;
            try
            {
                lock (slot.Gate)
                {
                    if (--entry.References != 0) return;
                    var operation = BeginOperation();
                    try
                    {
                        // Keep this monitor's slot reserved until OS shutdown is
                        // finished. Other monitors and the GPU remain available.
                        lock (entry.Gate) entry.Source.Dispose();
                    }
                    finally { slot.Active = null; EndOperation(operation); }
                }
            }
            finally { ReleaseReservation(slot); }
        }
    }

    internal sealed class Lease(Registry registry, Entry value) : IDisposable
    {
        private Entry? entry = value;
        internal string Diagnostic => Volatile.Read(ref entry)?.Source.Diagnostic ?? "Capture lease disposed.";

        /// <summary>Owns one source across frame use and disposal; native GPU commands use a separate short gate.</summary>
        internal bool TryUseLatestFrame(Action<nint, nint, nint, int, int, int, int> useFrame)
        {
            ArgumentNullException.ThrowIfNull(useFrame);
            var active = Volatile.Read(ref entry);
            ObjectDisposedException.ThrowIf(active is null, this);
            lock (active.Gate)
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref entry) is null, this);
                return active.Source.TryUseLatestFrame(useFrame);
            }
        }

        internal bool TryUseLatestFrame(Action<nint, nint, nint, int, int, int, int, long> useFrame)
        {
            ArgumentNullException.ThrowIfNull(useFrame);
            var active = Volatile.Read(ref entry);
            ObjectDisposedException.ThrowIf(active is null, this);
            lock (active.Gate)
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref entry) is null, this);
                return active.Source.TryUseLatestFrame((device, context, texture, width, height, x, y) =>
                    useFrame(device, context, texture, width, height, x, y, active.Source.FrameVersion));
            }
        }

        internal void Synchronize(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            var active = Volatile.Read(ref entry);
            ObjectDisposedException.ThrowIf(active is null, this);
            lock (active.Gate)
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref entry) is null, this);
                action();
            }
        }

        public void Dispose()
        {
            var released = Interlocked.Exchange(ref entry, null);
            if (released is not null) registry.Release(released);
        }
    }

    internal sealed class MonitorSlot(nint monitor)
    {
        internal nint Monitor { get; } = monitor;
        internal object Gate { get; } = new();
        internal Entry? Active;
        internal int Reservations;
    }

    internal sealed class Entry(nint monitor, ISource source, MonitorSlot slot)
    {
        internal nint Monitor { get; } = monitor;
        internal ISource Source { get; } = source;
        internal object Gate { get; } = new();
        internal MonitorSlot Slot { get; } = slot;
        internal int References;
    }

    [StructLayout(LayoutKind.Sequential)] private readonly struct NativePoint(int x, int y)
    { public readonly int X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool EnumerateMonitors(nint monitor, nint dc, ref NativeRect bounds, nint data);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint dc, nint clip, EnumerateMonitors callback, nint data);
}
