using System;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace Citrus.Windows.Presentation;

/// <summary>
/// A monitor-scoped Windows Graphics Capture source. The Studio path caches the latest desktop
/// texture on the GPU and exposes it only inside the pool's serialized context lease. The legacy
/// ROI reader remains for isolated capability proofs. No desktop frame is persisted.
/// </summary>
internal sealed class DesktopGlassSource : IDesktopGlassSource, DesktopGlassCapturePool.ISource
{
    private const int NotFound = unchecked((int)0x887A0002);
    private static readonly Guid FactoryId = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid TextureId = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid DxgiDeviceId = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid DxgiAccessId = new("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1");
    private static readonly Guid CaptureInteropId = new("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
    private static readonly Guid CaptureItemId = new("79c3f95b-31f7-4ec2-a464-632ef5d30760");
    private static readonly Guid BorderlessAccessId = new("743ed370-06ec-5040-a58a-901f0f757095");
    private static readonly Guid BorderSessionId = new("f2cdd966-22ae-5ea1-9596-3a289344c3be");
    private static readonly Guid AsyncInfoId = new("00000036-0000-0000-c000-000000000046");
    private static readonly object AccessLock = new();
    private static bool? borderlessAllowed;
    private static readonly CaptureCapabilityCache Capabilities = new(
        () => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348) && GraphicsCaptureSession.IsSupported(),
        EnsureBorderlessAccess);
    private nint device, context, staging, output, borderSession, latestTexture;
    private IDirect3DDevice? captureDevice;
    private GraphicsCaptureItem? captureItem;
    private Direct3D11CaptureFramePool? framePool;
    private GraphicsCaptureSession? session;
    private global::Windows.Graphics.SizeInt32 frameSize;
    private readonly int screenX, screenY, pixelSize;
    private OutputDescription outputDescription;
    private CopyBox crop;
    private bool disposed;
    private bool captureClosed;
    private readonly CaptureFrameBarrier frameBarrier = new();
    private string diagnostic = "Not initialized.";
    private static string lastCreationDiagnostic = "No capture creation attempted.";
    private int captureRestarts, frameReads, rejectedFrames;
    private long lastFrameTimestamp, frameVersion;
    internal static string LastCreationDiagnostic => Volatile.Read(ref lastCreationDiagnostic);
    public string Diagnostic => Volatile.Read(ref diagnostic);
    internal nint Monitor => outputDescription.Monitor;
    internal nint Device => device;
    internal nint Context => context;
    public long FrameVersion => Volatile.Read(ref frameVersion);

    private DesktopGlassSource(int screenX, int screenY, int pixelSize)
    { this.screenX = screenX; this.screenY = screenY; this.pixelSize = pixelSize; }

    /// <summary>Resolve optional capture prerequisites without creating a device, session or desktop texture.</summary>
    internal static bool PrewarmCapabilities()
    {
        try { return Capabilities.Resolve(); }
        catch (Exception error) when (error is ExternalException or InvalidOperationException or
            ArgumentException or NotSupportedException or UnauthorizedAccessException)
        { return false; }
    }

    internal static DesktopGlassSource? TryCreate(int screenX, int screenY, int pixelSize)
    {
        if (pixelSize is < 128 or > 1536 || (long)screenX + pixelSize > int.MaxValue || (long)screenY + pixelSize > int.MaxValue)
            return null;
        var source = new DesktopGlassSource(screenX, screenY, pixelSize);
        try
        {
            if (source.Initialize()) return source;
        }
        catch (Exception error) when (error is ExternalException or InvalidOperationException or ArgumentException or NotSupportedException or UnauthorizedAccessException) { }
        source.Dispose();
        return null;
    }

    internal static DesktopGlassSource? TryCreateMonitorAt(int screenX, int screenY, nint sharedDevice = 0, nint sharedContext = 0)
    {
        if ((sharedDevice == 0) != (sharedContext == 0)) return null;
        var source = new DesktopGlassSource(screenX, screenY, 0);
        try
        {
            if (sharedDevice != 0)
            {
                Marshal.AddRef(sharedDevice); source.device = sharedDevice;
                Marshal.AddRef(sharedContext); source.context = sharedContext;
            }
            if (source.Initialize())
            { Volatile.Write(ref lastCreationDiagnostic, source.Diagnostic); return source; }
        }
        catch (Exception error) when (error is ExternalException or InvalidOperationException or ArgumentException or NotSupportedException or UnauthorizedAccessException)
        { Volatile.Write(ref source.diagnostic, $"{source.Diagnostic} {error.GetType().Name} 0x{error.HResult:X8}: {error.Message}"); }
        Volatile.Write(ref lastCreationDiagnostic, source.Diagnostic);
        source.Dispose();
        return null;
    }

    private bool Initialize()
    {
        Volatile.Write(ref diagnostic, "Checking Windows capture prerequisites.");
        // The newer border APIs are queried through their verified ABI while retaining the pinned
        // 19041 projection. Never start capture until borderless access has actually been granted.
        if (!PrewarmCapabilities())
        { Volatile.Write(ref diagnostic, "Windows capture or borderless access unavailable."); return false; }
        nint factory = 0;
        try
        {
            Guid iid = FactoryId;
            Volatile.Write(ref diagnostic, "Enumerating physical DXGI monitors.");
            Check(CreateDXGIFactory1(ref iid, out factory));
            var enumerateAdapters = Method<Enumerate>(factory, 12);
            for (uint adapterIndex = 0; ; adapterIndex++)
            {
                int result = enumerateAdapters(factory, adapterIndex, out nint adapter);
                if (result == NotFound)
                {
                    Volatile.Write(ref diagnostic, $"No attached DXGI output contains capture anchor {screenX},{screenY}.");
                    return false;
                }
                Check(result);
                try
                {
                    var enumerateOutputs = Method<Enumerate>(adapter, 7);
                    for (uint outputIndex = 0; ; outputIndex++)
                    {
                        result = enumerateOutputs(adapter, outputIndex, out nint candidate);
                        if (result == NotFound) break;
                        Check(result);
                        try
                        {
                            Check(Method<GetOutputDescription>(candidate, 7)(candidate, out var description));
                            // Legacy proof crops must fit one output. A monitor-scoped source selects
                            // only its anchor point; padded shader sampling clamps to that monitor.
                            if (description.AttachedToDesktop == 0 ||
                                screenX < description.Desktop.Left || screenY < description.Desktop.Top ||
                                screenX >= description.Desktop.Right || screenY >= description.Desktop.Bottom ||
                                (pixelSize != 0 && ((long)screenX + pixelSize > description.Desktop.Right ||
                                    (long)screenY + pixelSize > description.Desktop.Bottom)))
                                continue;
                            outputDescription = description;
                            output = candidate; candidate = 0;
                            // Legacy proofs create a device on the selected output's adapter. Pooled
                            // captures reuse one device: WGC delivers each monitor onto that device,
                            // allowing cross-monitor GPU stitching without cross-device CPU copies.
                            if (device == 0)
                                Check(D3D11CreateDevice(adapter, 0, 0, 0x20, 0, 0, 7, out device, out _, out context));
                            Volatile.Write(ref diagnostic, "Creating WGC monitor session.");
                            InitializeCapture(description.Monitor);
                            if (pixelSize == 0) return true;
                            var texture = new TextureDescription
                            {
                                Width = (uint)pixelSize, Height = (uint)pixelSize, MipLevels = 1, ArraySize = 1,
                                Format = 87, SampleCount = 1, Usage = 3, CpuAccessFlags = 0x20000
                            }; // BGRA8, STAGING, CPU_ACCESS_READ; no bind or miscellaneous flags.
                            Check(Method<CreateTexture>(device, 5)(device, ref texture, 0, out staging));
                            crop = new CopyBox
                            {
                                Left = (uint)(screenX - description.Desktop.Left), Top = (uint)(screenY - description.Desktop.Top),
                                Right = (uint)(screenX - description.Desktop.Left + pixelSize),
                                Bottom = (uint)(screenY - description.Desktop.Top + pixelSize), Back = 1
                            };
                            return true;
                        }
                        finally { Release(ref candidate); }
                    }
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Release(ref factory); }
    }

    private void InitializeCapture(nint monitor)
    {
        Guid dxgiId = DxgiDeviceId;
        Check(Marshal.QueryInterface(device, in dxgiId, out nint dxgiDevice));
        try
        {
            Check(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out nint inspectable));
            try { captureDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable); }
            finally { Marshal.Release(inspectable); }
        }
        finally { Marshal.Release(dxgiDevice); }

        using (var factory = WinRT.ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem", CaptureInteropId))
        {
            Guid itemId = CaptureItemId;
            Check(Method<CreateMonitorItem>(factory.ThisPtr, 4)(factory.ThisPtr, monitor, ref itemId, out nint item));
            try { captureItem = WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(item); }
            finally { Marshal.Release(item); }
        }
        captureItem.Closed += OnCaptureClosed;
        frameSize = captureItem.Size;
        // WGC describes display-oriented content through item.Size/ContentSize. DXGI's
        // scanout rotation applies to Desktop Duplication, not a WGC client-side transform.
        // Require the actual captured dimensions to match the physical desktop rectangle.
        if (frameSize.Width != outputDescription.Desktop.Right - outputDescription.Desktop.Left ||
            frameSize.Height != outputDescription.Desktop.Bottom - outputDescription.Desktop.Top)
            throw new InvalidOperationException($"WGC item {frameSize.Width}x{frameSize.Height} differs from physical monitor " +
                $"{outputDescription.Desktop.Right - outputDescription.Desktop.Left}x{outputDescription.Desktop.Bottom - outputDescription.Desktop.Top} (DXGI rotation {outputDescription.Rotation}).");
        // This new capture is created after the controller acquired exclusion. Its first
        // buffer is already a fresh session; do not throw it away and restart a second time.
        frameBarrier.Initialize(StudioCaptureExclusion.ContentRevision, QpcTimestamp());
        StartCaptureSession();
    }

    private void StartCaptureSession()
    {
        framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(captureDevice!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, frameSize);
        session = framePool.CreateCaptureSession(captureItem!);
        session.IsCursorCaptureEnabled = false;
        if (session.IsCursorCaptureEnabled) throw new InvalidOperationException("Cursor exclusion is unavailable.");
        nint sessionInspectable = WinRT.MarshalInspectable<GraphicsCaptureSession>.FromManaged(session);
        try
        {
            Guid borderId = BorderSessionId;
            Check(Marshal.QueryInterface(sessionInspectable, in borderId, out borderSession));
        }
        finally { Marshal.Release(sessionInspectable); }
        Check(Method<PutBoolean>(borderSession, 7)(borderSession, 0));
        Check(Method<GetBoolean>(borderSession, 6)(borderSession, out byte border));
        if (border != 0) throw new InvalidOperationException("Borderless capture is unavailable.");
        session.StartCapture();
        Volatile.Write(ref diagnostic, $"WGC started; {frameSize.Width}x{frameSize.Height}; DXGI rotation {outputDescription.Rotation}; awaiting initial frame.");
    }

    private void OnCaptureClosed(GraphicsCaptureItem sender, object args) => Volatile.Write(ref captureClosed, true);

    private static bool EnsureBorderlessAccess()
    {
        lock (AccessLock)
        {
            if (borderlessAllowed is bool available) return available;
            // Packaged applications can show OS consent UI. This drag-only backend supports the
            // unpackaged desktop distribution, for which the OS grants full-trust access silently.
            uint packageNameLength = 0;
            if (GetCurrentPackageFullName(ref packageNameLength, 0) != 15700) return (borderlessAllowed = false).Value;
            using var factory = WinRT.ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureAccess", BorderlessAccessId);
            Check(Method<RequestAccess>(factory.ThisPtr, 6)(factory.ThisPtr, 0, out nint operation)); // Borderless = 0.
            nint information = 0;
            try
            {
                Guid infoId = AsyncInfoId;
                Check(Marshal.QueryInterface(operation, in infoId, out information));
                long deadline = Environment.TickCount64 + 1000;
                int status;
                do
                {
                    Check(Method<GetInteger>(information, 7)(information, out status));
                    if (status != 0) break; // AsyncStatus.Started = 0.
                    Thread.Sleep(10);
                } while (Environment.TickCount64 < deadline);
                if (status == 0) Method<NoArguments>(information, 9)(information); // Cancel a stalled request.
                if (status != 1) return (borderlessAllowed = false).Value; // AsyncStatus.Completed = 1.
                Check(Method<GetInteger>(operation, 8)(operation, out int access));
                return (borderlessAllowed = access == 4).Value; // AppCapabilityAccessStatus.Allowed = 4.
            }
            finally
            {
                if (information != 0) { Method<NoArguments>(information, 10)(information); Marshal.Release(information); }
                Marshal.Release(operation);
            }
        }
    }

    public bool TryReadFrame(byte[] destination)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.Length != checked(pixelSize * pixelSize * 4))
            throw new ArgumentException("Expected one square BGRA32 wheel frame.", nameof(destination));
        if (pixelSize == 0) throw new InvalidOperationException("Monitor sources support GPU rendering only.");
        ValidateCapture();
        using var frame = ReadLatestFrame();
        if (frame is null) return false;
        if (frame.ContentSize.Width != frameSize.Width || frame.ContentSize.Height != frameSize.Height)
            throw new InvalidOperationException("Captured monitor dimensions changed.");
        nint surface = WinRT.MarshalInspectable<IDirect3DSurface>.FromManaged(frame.Surface);
        nint access = 0;
        try
        {
            Guid accessId = DxgiAccessId;
            Check(Marshal.QueryInterface(surface, in accessId, out access));
            Guid textureId = TextureId;
            Check(Method<GetInterface>(access, 3)(access, ref textureId, out nint texture));
            try
            {
                Method<GetTextureDescription>(texture, 10)(texture, out var description);
                if (description.Format != 87 || description.Width < crop.Right || description.Height < crop.Bottom)
                    throw new InvalidOperationException("Desktop frame dimensions or format changed.");
                // The compatibility proof reader copies its small ROI only. Production Studio
                // rendering never maps or CPU-copies the desktop source resource.
                Method<CopyRegion>(context, 46)(context, staging, 0, 0, 0, 0, texture, 0, ref crop);
                Check(Method<MapTexture>(context, 14)(context, staging, 0, 1, 0, out var mapped));
                try
                {
                    int rowBytes = checked(pixelSize * 4);
                    if (mapped.Data == 0 || mapped.RowPitch < rowBytes)
                        throw new InvalidOperationException("Invalid desktop staging surface.");
                    for (int y = 0; y < pixelSize; y++)
                        Marshal.Copy(mapped.Data + checked((int)mapped.RowPitch * y), destination, y * rowBytes, rowBytes);
                }
                finally { Method<UnmapTexture>(context, 15)(context, staging, 0); }
                Check(Method<DeviceStatus>(device, 39)(device));
                return true;
            }
            finally { Marshal.Release(texture); }
        }
        finally { Release(ref access); Marshal.Release(surface); }
    }

    public bool TryUseLatestFrame(Action<nint, nint, nint, int, int, int, int> useFrame)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(useFrame);
        ValidateCapture();
        long expectedRevision = StudioCaptureExclusion.ContentRevision;
        if (frameBarrier.Refresh(expectedRevision, DiscardCachedFrames, FlushAndRestartCapture))
        {
            Volatile.Write(ref diagnostic, $"Restart {captureRestarts}; awaiting compositor frame at QPC {frameBarrier.MinimumTimestamp}.");
            return false;
        }
        // Copying into our resource allows the WGC frame to return to its two-buffer pool
        // immediately. Optical animation and other surfaces reuse this texture if no frame arrives.
        using (var frame = ReadLatestFrame())
        {
            if (frame is not null)
            {
                frameReads++; lastFrameTimestamp = frame.SystemRelativeTime.Ticks;
                if (frameBarrier.Accept(lastFrameTimestamp)) CacheFrame(frame);
                else rejectedFrames++;
            }
        }
        Volatile.Write(ref diagnostic, $"Restarts {captureRestarts}; frames {frameReads}; rejected {rejectedFrames}; frameQPC {lastFrameTimestamp}; minimumQPC {frameBarrier.MinimumTimestamp}; texture {(latestTexture == 0 ? "empty" : "ready")}.");
        if (latestTexture == 0 || expectedRevision != StudioCaptureExclusion.ContentRevision) return false;
        // Only immediate-context commands/callbacks share the renderer gate.
        // WinRT frame acquisition and exclusion-driven capture restarts above
        // keep their own per-source ownership without blocking another monitor.
        lock (DesktopGlassCapturePool.GpuGate)
        {
            useFrame(device, context, latestTexture, frameSize.Width, frameSize.Height,
                outputDescription.Desktop.Left, outputDescription.Desktop.Top);
            Check(Method<DeviceStatus>(device, 39)(device));
        }
        return expectedRevision == StudioCaptureExclusion.ContentRevision;
    }

    private void DiscardCachedFrames()
    {
        Release(ref latestTexture);
        using var discarded = ReadLatestFrame();
    }

    private long FlushAndRestartCapture()
    {
        captureRestarts++;
        // Run only on the capture worker. Flush this application's pending composition changes,
        // then request a new initial frame even if the desktop becomes completely static. Reusing
        // an old frame pool here could leave only one pre-boundary frame and starve the material.
        StopCaptureSession();
        Check(BackdropNativeMethods.DwmFlush());
        long timestamp = QpcTimestamp();
        StartCaptureSession();
        return timestamp;
    }

    private static long QpcTimestamp() => (long)(System.Diagnostics.Stopwatch.GetTimestamp() *
        (double)TimeSpan.TicksPerSecond / System.Diagnostics.Stopwatch.Frequency);

    private void CacheFrame(Direct3D11CaptureFrame frame)
    {
        if (frame.ContentSize.Width != frameSize.Width || frame.ContentSize.Height != frameSize.Height)
            throw new InvalidOperationException("Captured monitor dimensions changed.");
        nint surface = WinRT.MarshalInspectable<IDirect3DSurface>.FromManaged(frame.Surface);
        nint access = 0;
        try
        {
            Guid accessId = DxgiAccessId;
            Check(Marshal.QueryInterface(surface, in accessId, out access));
            Guid textureId = TextureId;
            Check(Method<GetInterface>(access, 3)(access, ref textureId, out nint texture));
            try
            {
                Method<GetTextureDescription>(texture, 10)(texture, out var description);
                if (description.Format != 87 || description.Width != frameSize.Width || description.Height != frameSize.Height ||
                    description.ArraySize != 1 || description.MipLevels != 1 || description.SampleCount != 1)
                    throw new InvalidOperationException("Desktop frame dimensions or format changed.");
                if (latestTexture == 0)
                {
                    description.Usage = 0; description.BindFlags = 8; // DEFAULT, SHADER_RESOURCE.
                    description.CpuAccessFlags = 0; description.MiscFlags = 0;
                    Check(Method<CreateTexture>(device, 5)(device, ref description, 0, out latestTexture));
                }
                lock (DesktopGlassCapturePool.GpuGate)
                    Method<CopyResource>(context, 47)(context, latestTexture, texture);
                Interlocked.Increment(ref frameVersion);
            }
            finally { Marshal.Release(texture); }
        }
        finally { Release(ref access); Marshal.Release(surface); }
    }

    private void ValidateCapture()
    {
        if (Volatile.Read(ref captureClosed)) throw new InvalidOperationException("The captured monitor closed.");
        Check(Method<DeviceStatus>(device, 39)(device));
        Check(Method<GetOutputDescription>(output, 7)(output, out var current));
        if (current.AttachedToDesktop == 0 || current.Monitor != outputDescription.Monitor || current.Rotation != outputDescription.Rotation ||
            current.Desktop.Left != outputDescription.Desktop.Left || current.Desktop.Top != outputDescription.Desktop.Top ||
            current.Desktop.Right != outputDescription.Desktop.Right || current.Desktop.Bottom != outputDescription.Desktop.Bottom)
            throw new InvalidOperationException("The captured monitor changed; recreate the shared source.");

        Check(Method<GetBoolean>(borderSession, 6)(borderSession, out byte border));
        if (border != 0) throw new InvalidOperationException("Borderless capture was invalidated.");
    }

    private Direct3D11CaptureFrame? ReadLatestFrame()
    {
        var latest = framePool!.TryGetNextFrame();
        if (latest is null) return null;
        try
        {
            // The pool owns two GPU buffers. Consume at most both so a 30 Hz renderer uses
            // the latest available desktop instead of keeping a permanently queued older frame.
            var second = framePool.TryGetNextFrame();
            if (second is null) return latest;
            var previous = latest;
            latest = second;
            previous.Dispose();
            return latest;
        }
        catch { latest.Dispose(); throw; }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { StopCaptureSession(); }
        finally
        {
            try { QuietClose(() => { if (captureItem is not null) captureItem.Closed -= OnCaptureClosed; }); }
            finally
            {
                captureItem = null;
                try { QuietClose(() => captureDevice?.Dispose()); }
                finally
                {
                    captureDevice = null;
                    Release(ref staging); Release(ref latestTexture);
                    Release(ref output); Release(ref context); Release(ref device);
                }
            }
        }
    }

    private void StopCaptureSession()
    {
        try { QuietClose(() => session?.Dispose()); }
        finally
        {
            session = null;
            try { QuietClose(() => framePool?.Dispose()); }
            finally { framePool = null; Release(ref borderSession); }
        }
    }

    // Monitor/device loss may disconnect WinRT objects before teardown. Native GPU references
    // must still be released and disposal must remain harmless to the admitted drag.
    private static void QuietClose(Action close)
    {
        try { close(); }
        catch (Exception error) when (error is ExternalException or InvalidOperationException) { }
    }

    private static void Check(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }
    private static void Release(ref nint value) { if (value == 0) return; Marshal.Release(value); value = 0; }
    // Slot numbers and IIDs are the fixed Windows SDK ABI: dxgi.h, d3d11.h,
    // windows.graphics.capture.idl, asyncinfo.h and graphics capture/D3D11 interop headers.
    private static T Method<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct OutputDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public NativeRect Desktop;
        public int AttachedToDesktop, Rotation;
        public nint Monitor;
    }
    [StructLayout(LayoutKind.Sequential)] private struct TextureDescription
    { public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccessFlags, MiscFlags; }
    [StructLayout(LayoutKind.Sequential)] private struct CopyBox { public uint Left, Top, Front, Right, Bottom, Back; }
    [StructLayout(LayoutKind.Sequential)] private struct MappedTexture { public nint Data; public uint RowPitch, DepthPitch; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Enumerate(nint instance, uint index, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetOutputDescription(nint instance, out OutputDescription value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateMonitorItem(nint instance, nint monitor, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetInterface(nint instance, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int RequestAccess(nint instance, int kind, out nint operation);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetInteger(nint instance, out int value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetBoolean(nint instance, out byte value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PutBoolean(nint instance, byte value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int NoArguments(nint instance);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTexture(nint instance, ref TextureDescription description, nint initialData, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int DeviceStatus(nint instance);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GetTextureDescription(nint instance, out TextureDescription value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CopyRegion(nint instance, nint destination, uint destinationSubresource, uint x, uint y, uint z, nint source, uint sourceSubresource, ref CopyBox box);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CopyResource(nint instance, nint destination, nint source);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int MapTexture(nint instance, nint resource, uint subresource, uint type, uint flags, out MappedTexture mapped);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void UnmapTexture(nint instance, nint resource, uint subresource);
    [DllImport("dxgi.dll", ExactSpelling = true)] private static extern int CreateDXGIFactory1(ref Guid iid, out nint factory);
    [DllImport("d3d11.dll", ExactSpelling = true)] private static extern int D3D11CreateDevice(nint adapter, uint driverType, nint software, uint flags, nint levels, uint levelCount, uint sdkVersion, out nint device, out uint featureLevel, out nint context);
    [DllImport("d3d11.dll", ExactSpelling = true)] private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern int GetCurrentPackageFullName(ref uint length, nint packageFullName);
}
