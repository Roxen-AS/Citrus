using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Citrus.Core;
using Citrus.Windows.Presentation;
using Windows.System;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;

namespace Citrus.Windows.Tests;

/// <summary>Test-only composition experiment. Never attached to a production backdrop controller.</summary>
internal sealed class HostBackdropProbeWindow : IAsyncDisposable
{
    internal const int WsExNoRedirectionBitmap = 0x00200000;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly string className = "Citrus.HostBackdropProbe." + Guid.NewGuid().ToString("N");
    private readonly BackdropNativeMethods.WindowProcedure procedure = WindowProc;
    private readonly nint instance = BackdropNativeMethods.GetModuleHandle(null);
    private bool registered, disposed;
    private readonly HostBackdropProbeQueue queueLease;
    private Compositor? compositor;
    private DesktopWindowTarget? target;
    private SpriteVisual? visual;
    private CompositionEllipseGeometry? geometry;
    private CompositionGeometricClip? clip;
    private CompositionBackdropBrush? hostBrush;
    private CompositionColorBrush? colorBrush;
    internal nint Handle { get; private set; }
    internal nint Owner { get; private set; }
    internal bool OwnsQueue => queueLease.OwnsQueue;
    internal bool QueueAcquired => queueLease.Acquired;
    internal bool TargetDetached { get; private set; }
    internal bool CompositionClosed { get; private set; }
    internal bool WindowsDestroyed { get; private set; }
    internal int? HostAttributeHResult { get; private set; }
    internal int? SystemBackdropHResult { get; private set; }
    internal int? QueueCreateHResult => queueLease.CreateHResult;
    internal int? InteropQueryHResult { get; private set; }
    internal int? TargetCreateHResult { get; private set; }
    internal string Stage { get; private set; } = "Created";
    internal bool Configured { get; private set; }
    internal bool UsesHostBrush { get; private set; }
    internal bool NoRedirectionBitmap { get; }
    internal int ClipLeft { get; private set; }
    internal int ClipRight { get; private set; }
    internal int Width { get; private set; }
    internal int Height { get; private set; }

    internal HostBackdropProbeWindow(bool noRedirectionBitmap, HostBackdropProbeQueue queueLease)
    {
        dispatcher.VerifyAccess();
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Composition probe requires the existing WPF STA.");
        NoRedirectionBitmap = noRedirectionBitmap;
        this.queueLease = queueLease;
    }

    internal void Configure(ScreenRect bounds, double scale)
    {
        dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        Stage = "CreateOwnedWindows";
        var windowClass = new BackdropNativeMethods.WindowClass
        {
            Size = (uint)Marshal.SizeOf<BackdropNativeMethods.WindowClass>(), Procedure = procedure,
            Instance = instance, ClassName = className
        };
        if (BackdropNativeMethods.RegisterClassEx(ref windowClass) == 0) throw new Win32Exception();
        registered = true;
        int exclusions = BackdropNativeMethods.WsExNoActivate | BackdropNativeMethods.WsExToolWindow | BackdropNativeMethods.WsExTransparent;
        const uint style = (uint)(BackdropNativeMethods.WsPopup | BackdropNativeMethods.WsDisabled);
        Owner = BackdropNativeMethods.CreateWindowEx(exclusions, className, "Citrus host probe hidden lifetime", style, 0, 0, 0, 0, 0, 0, instance, 0);
        if (Owner == 0) throw new Win32Exception();
        int helperEx = exclusions | (NoRedirectionBitmap ? WsExNoRedirectionBitmap : 0);
        Handle = BackdropNativeMethods.CreateWindowEx(helperEx, className, "Citrus host probe disabled helper", style, 0, 0, 0, 0, Owner, 0, instance, 0);
        if (Handle == 0) throw new Win32Exception();
        Width = checked((int)bounds.Width); Height = checked((int)bounds.Height);
        ClipLeft = checked((int)Math.Ceiling(8 * scale)); ClipRight = checked((int)Math.Floor(360 * scale));
        if (ClipRight > Width || ClipRight > Height || ClipRight <= ClipLeft) throw new ArgumentOutOfRangeException(nameof(bounds));
        nint region = BackdropNativeMethods.CreateEllipticRgn(ClipLeft, ClipLeft, ClipRight, ClipRight);
        if (region == 0) throw new Win32Exception();
        if (BackdropNativeMethods.SetWindowRgn(Handle, region, false) == 0)
        {
            BackdropNativeMethods.DeleteObject(region);
            throw new Win32Exception();
        } // HWND now owns the native region, independently of the visual clip.
        Stage = "EnableHostBackdropOnly";
        int none = 1, enabled = 1;
        SystemBackdropHResult = BackdropNativeMethods.DwmSetWindowAttribute(Handle, 38, ref none, sizeof(int));
        Marshal.ThrowExceptionForHR(SystemBackdropHResult.Value);
        HostAttributeHResult = BackdropNativeMethods.DwmSetWindowAttribute(Handle, 17, ref enabled, sizeof(int));
        Marshal.ThrowExceptionForHR(HostAttributeHResult.Value);
        Stage = "AcquireDispatcherQueue";
        queueLease.Ensure();
        Stage = "CreateCompositorAndDesktopTarget";
        compositor = new Compositor();
        nint compositorAbi = 0, interop = 0, rawTarget = 0;
        try
        {
            compositorAbi = WinRT.MarshalInspectable<Compositor>.FromManaged(compositor);
            // Official windows.ui.composition.interop.h: IUnknown + CreateDesktopWindowTarget
            // (HWND, BOOL, IDesktopWindowTarget**), slot 3; EnsureOnThread is slot 4.
            var iid = new Guid("29E691FA-4567-4DCA-B319-D0F207EB6807");
            InteropQueryHResult = Marshal.QueryInterface(compositorAbi, in iid, out interop);
            Marshal.ThrowExceptionForHR(InteropQueryHResult.Value);
            var create = Marshal.GetDelegateForFunctionPointer<CreateDesktopTarget>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(interop), 3 * IntPtr.Size));
            // TRUE means visuals above this helper's own HWND content; native HWND placement
            // below the layered wheel remains explicit and independent.
            TargetCreateHResult = create(interop, Handle, 1, out rawTarget);
            Marshal.ThrowExceptionForHR(TargetCreateHResult.Value);
            target = WinRT.MarshalInspectable<DesktopWindowTarget>.FromAbi(rawTarget);
        }
        finally
        {
            if (rawTarget != 0) Marshal.Release(rawTarget);
            if (interop != 0) Marshal.Release(interop);
            if (compositorAbi != 0) Marshal.Release(compositorAbi);
        }
        Stage = "CreateRawHostBrushAndEllipseVisual";
        hostBrush = compositor.CreateHostBackdropBrush();
        colorBrush = compositor.CreateColorBrush(global::Windows.UI.Color.FromArgb(255, 255, 0, 255));
        geometry = compositor.CreateEllipseGeometry();
        float radius = (ClipRight - ClipLeft) / 2f;
        geometry.Center = new Vector2(ClipLeft + radius, ClipLeft + radius);
        geometry.Radius = new Vector2(radius);
        clip = compositor.CreateGeometricClip(geometry);
        visual = compositor.CreateSpriteVisual();
        visual.Size = new Vector2(Width, Height);
        visual.Clip = clip;
        visual.Brush = hostBrush;
        target.Root = visual;
        if (!ReferenceEquals(target.Root, visual) || visual.Brush != hostBrush || visual.Clip != clip)
            throw new InvalidOperationException("Composition target root/brush/clip bindings did not round-trip.");
        Configured = true; UsesHostBrush = true;
        Stage = "ConfiguredRawHostBackdropNotOpticallyValidated";
    }

    internal void ShowBehind(nint wheel, ScreenRect bounds)
    {
        dispatcher.VerifyAccess();
        if (!Configured) throw new InvalidOperationException("Cannot present an unconfigured composition probe.");
        nint batch = BackdropNativeMethods.BeginDeferWindowPos(2);
        if (batch == 0) throw new Win32Exception();
        batch = BackdropNativeMethods.DeferWindowPos(batch, wheel, -1, 0, 0, 0, 0,
            BackdropNativeMethods.NoActivate | BackdropNativeMethods.NoMove | BackdropNativeMethods.NoSize);
        if (batch == 0) throw new Win32Exception();
        batch = BackdropNativeMethods.DeferWindowPos(batch, Handle, wheel, (int)bounds.Left, (int)bounds.Top,
            Width, Height, BackdropNativeMethods.NoActivate | BackdropNativeMethods.ShowWindowFlag);
        if (batch == 0 || !BackdropNativeMethods.EndDeferWindowPos(batch)) throw new Win32Exception();
    }

    internal void SetOpaquePresentationControl(bool opaque)
    {
        dispatcher.VerifyAccess();
        if (visual is null) throw new InvalidOperationException("No configured visual.");
        visual.Brush = opaque ? colorBrush : hostBrush;
        if (visual.Brush != (opaque ? colorBrush : (CompositionBrush?)hostBrush))
            throw new InvalidOperationException("Composition presentation-control brush did not round-trip.");
        UsesHostBrush = !opaque;
    }

    internal (Vector2 Size, Vector2 Center, Vector2 Radius) ReadVisualGeometry()
    {
        dispatcher.VerifyAccess();
        if (visual is null || geometry is null || clip is null || visual.Clip != clip || clip.Geometry != geometry)
            throw new InvalidOperationException("The configured visual must retain its ellipse geometry clip.");
        return (visual.Size, geometry.Center, geometry.Radius);
    }

    internal void Hide()
    {
        dispatcher.VerifyAccess();
        if (Handle != 0) BackdropNativeMethods.ShowWindow(Handle, 0);
    }

    public ValueTask DisposeAsync()
    {
        dispatcher.VerifyAccess();
        if (disposed) return ValueTask.CompletedTask;
        disposed = true;
        var failures = new List<Exception>();
        void Attempt(Action action) { try { action(); } catch (Exception error) { failures.Add(error); } }
        // Hide synchronously before any await or composition cleanup, even after setup failure.
        Attempt(Hide);
        Attempt(() => { if (target is not null) target.Root = null; TargetDetached = true; });
        Attempt(() => { if (visual is not null) { visual.Brush = null; visual.Clip = null; } });
        foreach (var item in new IDisposable?[] { visual, clip, geometry, hostBrush, colorBrush, target, compositor })
            if (item is not null) Attempt(item.Dispose);
        visual = null; clip = null; geometry = null; hostBrush = null; colorBrush = null; target = null; compositor = null;
        Configured = false; UsesHostBrush = false;
        CompositionClosed = failures.Count == 0;
        nint helper = Handle, owner = Owner;
        if (helper != 0) Attempt(() => { if (!BackdropNativeMethods.DestroyWindow(helper)) throw new Win32Exception(); });
        if (owner != 0) Attempt(() => { if (!BackdropNativeMethods.DestroyWindow(owner)) throw new Win32Exception(); });
        WindowsDestroyed = !BackdropNativeMethods.IsWindow(helper) && !BackdropNativeMethods.IsWindow(owner);
        Handle = 0; Owner = 0;
        if (registered) Attempt(() => { if (!BackdropNativeMethods.UnregisterClass(className, instance)) throw new Win32Exception(); });
        registered = false;
        GC.KeepAlive(procedure);
        if (failures.Count != 0) throw new AggregateException("Host backdrop probe cleanup failed.", failures);
        return ValueTask.CompletedTask;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateDesktopTarget(nint self, nint hwnd, int isTopmost, out nint target);
    private static nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam) => message switch
    {
        0x21 => 3, 0x84 => -1, 0x14 => 1,
        _ => BackdropNativeMethods.DefWindowProc(hwnd, message, wParam, lParam)
    };
}


/// <summary>One queue lease for the complete experiment; never recreated between helper variants.</summary>
internal sealed class HostBackdropProbeQueue : IAsyncDisposable
{
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private DispatcherQueueController? controller;
    private DispatcherQueue? queue;
    private bool disposed;
    internal bool OwnsQueue { get; private set; }
    internal bool Acquired => queue is not null;
    internal bool ShutdownCompleted { get; private set; }
    internal int? CreateHResult { get; private set; }
    internal void Ensure()
    {
        dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (queue is not null) return;
        queue = DispatcherQueue.GetForCurrentThread();
        if (queue is not null) return; // Borrowed: must never shut this queue down.
        nint raw = 0;
        try
        {
            CreateHResult = CreateDispatcherQueueController(new() { Size = Marshal.SizeOf<QueueOptions>(), ThreadType = 2, ApartmentType = 2 }, out raw);
            Marshal.ThrowExceptionForHR(CreateHResult.Value);
            controller = WinRT.MarshalInspectable<DispatcherQueueController>.FromAbi(raw);
            OwnsQueue = true;
            queue = controller.DispatcherQueue;
        }
        finally { if (raw != 0) Marshal.Release(raw); }
        if (queue is null) throw new InvalidOperationException("No Windows.System.DispatcherQueue on the probe STA.");
    }
    public async ValueTask DisposeAsync()
    {
        dispatcher.VerifyAccess();
        if (disposed) return;
        disposed = true;
        if (controller is not null)
        {
            // All helpers/targets are already gone; this STA keeps pumping through the await.
            await controller.ShutdownQueueAsync();
            ShutdownCompleted = true;
            controller = null;
        }
        queue = null;
    }
    [StructLayout(LayoutKind.Sequential)] private struct QueueOptions { public int Size, ThreadType, ApartmentType; }
    [DllImport("CoreMessaging.dll")] private static extern int CreateDispatcherQueueController(QueueOptions options, out nint controller);
}
