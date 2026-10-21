using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using static Citrus.Windows.Presentation.StudioD3D11Native;

namespace Citrus.Windows.Presentation;

/// <summary>
/// Studio's background, separable Gaussian and STEP9 optical passes on D3D11.
/// The monitor texture is never mapped: only the finished body-sized BGRA output is read back.
/// Calls (including disposal) are serialized by the owning monitor capture session.
/// </summary>
internal sealed class D3D11StudioRenderer : IDisposable
{
    // Fourteen float4 parameters, seventeen shoulder vectors, fifty-one Gaussian vectors.
    // Keep these offsets aligned with the HLSL constant buffer.
    private const int ConstantFloats = 56 + 68 + 204;
    private static readonly Lazy<IReadOnlyDictionary<string, byte[]>> ShaderCode = new(CompileShaders);
    /// <summary>Compiles immutable bytecode only; no device, capture session or desktop texture is created.</summary>
    internal static void PrecompileShaders() => _ = ShaderCode.Value;
    private readonly GlassScene scene;
    private readonly float[] constants = new float[ConstantFloats];
    private readonly nint[] resources = new nint[3], target = new nint[1], one = new nint[1];
    private readonly nint[] noResources = new nint[3];
    private readonly GCHandle constantsPin;
    private nint device, context, vertex, backgroundShader, horizontalShader, verticalShader, opticalShader, clampShader;
    private nint constantBuffer, sampler, field, fieldView, uploaded;
    private readonly ReadbackSlot[] readbacks = [new(), new()];
    private long nextReadbackSequence, readbackGeneration;
    private bool readbacksHeldForTesting;
    private int uploadedWidth, uploadedHeight;
    private Surface? input, tiledInput, background, horizontal, blurred, finished;
    private bool disposed, generatedTestSession, generatedFrame, processedSource, executionPrepared;
    internal double LastRenderMilliseconds { get; private set; }
    internal double LastReadbackMilliseconds { get; private set; }
    internal int SourcePassCount { get; private set; }
    internal int OpticalPassCount { get; private set; }
    internal int PendingReadbackCount => (readbacks[0].Pending ? 1 : 0) + (readbacks[1].Pending ? 1 : 0);
    internal int FreeReadbackSlots => readbacks.Length - PendingReadbackCount;
    internal bool ExecutionPrepared => executionPrepared;
    internal long ReadbackSubmissionCount { get; private set; }
    internal long ReadbackCompletionCount { get; private set; }
    internal long ReadbackPollCount { get; private set; }
    internal long ReadbackFlushCount { get; private set; }
    // Logical texture/buffer allocation bytes. Driver bookkeeping is not portable.
    internal long AllocationBytes => (long)scene.CaptureWidth * scene.CaptureHeight * (28 + (tiledInput is null ? 0 : 4)) +
        (long)scene.PixelWidth * scene.PixelHeight * 28 + readbacks.Length * sizeof(int) + ConstantFloats * sizeof(float) +
        (long)uploadedWidth * uploadedHeight * 4 + ShaderCode.Value.Sum(pair => (long)pair.Value.Length);

    /// <summary>Complete driver first-use work using cleared input; never captures or retains desktop content.</summary>
    internal void PrewarmExecution()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (executionPrepared) return;
        var completed = new byte[checked(scene.PixelWidth * scene.PixelHeight * 4)];
        try
        {
            if (!TrySubmitClearedInput(new(NavigationVisible: true), 0))
                throw new InvalidOperationException("Preparation requires a free readback slot.");
            // Compatibility helper only. Production runtime polls between short
            // GPU-gate sections instead of waiting inside that shared gate.
            while (!TryComplete(completed, out _)) Thread.Sleep(1);
            MarkExecutionPrepared();
        }
        finally
        {
            Array.Clear(completed);
            ClearCapturedContent();
        }
    }

    internal D3D11StudioRenderer(nint device, nint context, GlassScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (device == 0 || context == 0) throw new ArgumentException("A native D3D11 device and immediate context are required.");
        this.scene = scene;
        this.device = device; this.context = context;
        Marshal.AddRef(device); Marshal.AddRef(context);
        constantsPin = GCHandle.Alloc(constants, GCHandleType.Pinned);
        try { Initialize(); }
        catch { Dispose(); throw; }
    }

    internal static D3D11StudioRenderer CreateForTesting(GlassScene scene, bool software = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        nint device = 0, context = 0;
        try
        {
            Check(D3D11CreateDevice(0, software ? 5u : 1u, 0, 0x20, 0, 0, 7, out device, out _, out context));
            return new D3D11StudioRenderer(device, context, scene) { generatedTestSession = true };
        }
        finally { Release(ref context); Release(ref device); }
    }

    private void Initialize()
    {
        vertex = CreateShader("Vertex", 12);
        backgroundShader = CreateShader("Background", 15);
        horizontalShader = CreateShader("Horizontal", 15);
        verticalShader = CreateShader("Vertical", 15);
        opticalShader = CreateShader("Optical", 15);
        clampShader = CreateShader("ClampSource", 15);
        // A render-target view lets the resident pool erase source pixels without
        // allocating a body-sized CPU zero buffer on every close.
        input = new Surface(device, scene.CaptureWidth, scene.CaptureHeight, 87);
        background = new Surface(device, scene.CaptureWidth, scene.CaptureHeight, 10);
        horizontal = new Surface(device, scene.CaptureWidth, scene.CaptureHeight, 10);
        blurred = new Surface(device, scene.CaptureWidth, scene.CaptureHeight, 10);
        finished = new Surface(device, scene.PixelWidth, scene.PixelHeight, 87, shaderResource: false);
        var fieldDescription = Description(scene.PixelWidth, scene.PixelHeight, 2, 8);
        Check(Method<CreateTexture>(device, 5)(device, ref fieldDescription, 0, out field));
        Check(Method<CreateView>(device, 7)(device, field, 0, out fieldView));
        var fieldPin = GCHandle.Alloc(scene.Field, GCHandleType.Pinned);
        try { Method<Update>(context, 48)(context, field, 0, 0, fieldPin.AddrOfPinnedObject(), (uint)scene.PixelWidth * 16, 0); }
        finally { fieldPin.Free(); }
        var buffer = new BufferDescription { ByteWidth = ConstantFloats * 4, BindFlags = 4 };
        Check(Method<CreateBuffer>(device, 3)(device, ref buffer, 0, out constantBuffer));
        var sample = new SamplerDescription { Filter = 0x15, AddressU = 3, AddressV = 3, AddressW = 3,
            MaxAnisotropy = 1, Comparison = 1, MaxLod = float.MaxValue };
        Check(Method<CreateSampler>(device, 23)(device, ref sample, out sampler));
        var stagingDescription = Description(scene.PixelWidth, scene.PixelHeight, 87, 0);
        stagingDescription.Usage = 3; stagingDescription.CpuAccessFlags = 0x20000;
        foreach (var slot in readbacks)
        {
            Check(Method<CreateTexture>(device, 5)(device, ref stagingDescription, 0, out slot.Texture));
            var queryDescription = new QueryDescription(); // D3D11_QUERY_EVENT.
            Check(Method<CreateQuery>(device, 24)(device, ref queryDescription, out slot.Query));
        }
        PrepareMaterialConstants();
    }

    private void PrepareMaterialConstants()
    {
        GlassMaterial material = scene.Material;
        Set(4, scene.CaptureWidth, scene.CaptureHeight, scene.PaddingPixels, scene.Scale);
        Set(8, scene.PixelWidth, scene.PixelHeight, scene.WidthDip, scene.HeightDip);
        Set(12, material.ThicknessDip, material.RefractionIndex, material.RefractionDistance, material.Dispersion);
        Set(16, material.FresnelRange, material.FresnelHardness, material.FresnelIntensity, Math.Ceiling(material.BlurRadiusDip * scene.Scale));
        Set(20, material.GlareRange, material.GlareHardness, material.GlareIntensity, material.GlareConvergence);
        Set(24, material.GlareOpposite, material.GlareAngleRadians, 0, 1);
        Set(28, material.ShadowExpandDip, material.ShadowStrength, material.ShadowOffsetXDip, material.ShadowOffsetYDip);
        Set(36, material.TintRed, material.TintGreen, material.TintBlue, material.TintAlpha);
        Set(40, material.DarkTintRed, material.DarkTintGreen, material.DarkTintBlue, material.DarkTintAlpha);
        Set(44, scene.LightingReferenceHeightDip, scene.LightingCenterXDip, scene.LightingCenterYDip, material.SelectionTintOpacity);
        Set(48, material.EffectiveExteriorShadowExpandDip, material.ShadowStrength,
            material.EffectiveExteriorShadowOffsetXDip, material.EffectiveExteriorShadowOffsetYDip);
        Set(52, 1, scene.IsWheelBacking ? 1 : 0, material.EdgeFeatherDip, 0);
        // Region zero is reserved; use its shoulder slot without changing the HLSL layout.
        constants[56] = (float)material.EdgeHighlightOpacity;
        foreach (GlassRegion region in scene.Regions) constants[56 + region.Id] = (float)scene.ShoulderThicknessDip(region.Id);
        float[] gaussian = StudioGlassMath.GaussianKernel(material.BlurRadiusDip * scene.Scale);
        int radius = gaussian.Length / 2;
        if (radius > 200) throw new ArgumentOutOfRangeException(nameof(scene), "Studio supports at most 200 physical pixels of Gaussian support.");
        for (int offset = 0; offset <= radius; offset++) constants[124 + offset] = gaussian[radius + offset];
    }

    internal void Render(nint sourceTexture, int sourceWidth, int sourceHeight, int sourceLeft, int sourceTop,
        GlassFrameState state, byte[] destination)
    {
        ValidateDestination(destination);
        ValidateTexture(sourceTexture, sourceWidth, sourceHeight);
        long started = Stopwatch.GetTimestamp();
        CopySourceRoi(sourceTexture, sourceWidth, sourceHeight, sourceLeft, sourceTop);
        PrepareFrame(state);
        FinishFrame(input!.View, destination, started);
    }

    /// <summary>Submit only; callers serialize context access but never wait under their shared GPU gate.</summary>
    internal bool TrySubmitPixels(byte[] source, int sourceWidth, int sourceHeight, int sourceLeft, int sourceTop,
        GlassFrameState state, long stamp)
    {
        ValidateSubmission();
        if (FreeReadbackSlots == 0) return false;
        ArgumentNullException.ThrowIfNull(source);
        if (sourceWidth <= 0 || sourceHeight <= 0 || source.Length != checked(sourceWidth * sourceHeight * 4))
            throw new ArgumentException("Expected exact BGRA input dimensions.", nameof(source));
        long started = Stopwatch.GetTimestamp();
        if (sourceWidth == scene.CaptureWidth && sourceHeight == scene.CaptureHeight && sourceLeft == 0 && sourceTop == 0)
        {
            // The fresh bootstrap already is the exact padded ROI. Upload it to
            // the retained input instead of allocating and copying another texture.
            UploadPixels(input!.Texture, source, sourceWidth);
            Set(0, sourceWidth, sourceHeight, 0, 0);
        }
        else
        {
            UploadGeneratedInput(source, sourceWidth, sourceHeight);
            CopySourceRoi(uploaded, sourceWidth, sourceHeight, sourceLeft, sourceTop);
        }
        PrepareFrame(state);
        ProcessSource(input!.View);
        SubmitOptics(stamp, started);
        generatedFrame = generatedTestSession;
        return true;
    }

    internal bool TrySubmitTiles(IReadOnlyList<StudioGlassTile> tiles, GlassFrameState state, long stamp)
    {
        ValidateSubmission();
        if (FreeReadbackSlots == 0) return false;
        long started = Stopwatch.GetTimestamp();
        nint view = PrepareTiles(tiles, state);
        ProcessSource(view);
        SubmitOptics(stamp, started);
        return true;
    }

    internal bool TrySubmitCached(GlassFrameState state, long stamp)
    {
        ValidateSubmission();
        if (FreeReadbackSlots == 0) return false;
        if (!processedSource) throw new InvalidOperationException("A fresh source must be rendered before reusing its blurred textures.");
        long started = Stopwatch.GetTimestamp();
        PrepareFrame(state);
        SubmitOptics(stamp, started);
        return true;
    }

    /// <summary>Capture-free driver preparation through the same staged four-pass path.</summary>
    internal bool TrySubmitClearedInput(GlassFrameState state, long stamp)
    {
        ValidateSubmission();
        if (PendingReadbackCount != 0) return false;
        ClearCapturedContent();
        long started = Stopwatch.GetTimestamp();
        Set(0, scene.CaptureWidth, scene.CaptureHeight, 0, 0);
        PrepareFrame(state);
        ProcessSource(input!.View);
        SubmitOptics(stamp, started);
        return true;
    }

    internal void MarkExecutionPrepared()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (PendingReadbackCount != 0) throw new InvalidOperationException("GPU preparation must finish before it is reusable.");
        executionPrepared = true;
    }

    private void ValidateSubmission()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Check(Method<DeviceStatus>(device, 39)(device));
    }

    private void ValidateDestination(byte[] destination)
    {
        ValidateReadbackDestination(destination);
        if (PendingReadbackCount != 0)
            throw new InvalidOperationException("Synchronous compatibility rendering cannot overwrite pending staged output.");
        generatedFrame = false;
    }

    private void ValidateReadbackDestination(byte[] destination)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.Length != checked(scene.PixelWidth * scene.PixelHeight * 4)) throw new ArgumentException("Expected a body-sized BGRA buffer.", nameof(destination));
        Check(Method<DeviceStatus>(device, 39)(device));
    }

    private void ValidateTexture(nint sourceTexture, int sourceWidth, int sourceHeight)
    {
        if (sourceTexture == 0 || sourceWidth <= 0 || sourceHeight <= 0) throw new ArgumentException("A valid desktop texture is required.");
        Method<GetTextureDescription>(sourceTexture, 10)(sourceTexture, out var sourceDescription);
        if (sourceDescription.Width != sourceWidth || sourceDescription.Height != sourceHeight || sourceDescription.Format != 87 || sourceDescription.SampleCount != 1)
            throw new ArgumentException("Desktop texture must match the supplied BGRA8 dimensions.");
        Method<GetResourceDevice>(sourceTexture, 3)(sourceTexture, out nint owner);
        try { if (owner != device) throw new ArgumentException("All glass tiles must share the renderer D3D11 device."); }
        finally { Release(ref owner); }
    }

    private void CopySourceRoi(nint sourceTexture, int sourceWidth, int sourceHeight, int sourceLeft, int sourceTop)
    {
        // Copy only the intersection of the padded scene and monitor. Pass one extends the
        // nearest valid edge texel deterministically where a scene crosses a monitor edge.
        int left = Math.Clamp(sourceLeft, 0, sourceWidth - 1), top = Math.Clamp(sourceTop, 0, sourceHeight - 1);
        int right = (int)Math.Clamp((long)sourceLeft + scene.CaptureWidth, left + 1L, sourceWidth);
        int bottom = (int)Math.Clamp((long)sourceTop + scene.CaptureHeight, top + 1L, sourceHeight);
        var box = new Box { Left = (uint)left, Top = (uint)top, Right = (uint)right, Bottom = (uint)bottom, Back = 1 };
        Unbind();
        Method<Copy>(context, 46)(context, input!.Texture, 0, 0, 0, 0, sourceTexture, 0, ref box);
        Set(0, right - left, bottom - top, (double)sourceLeft - left, (double)sourceTop - top);
    }

    private void PrepareFrame(GlassFrameState state)
    {
        Set(24, scene.Material.GlareOpposite, scene.Material.GlareAngleRadians,
            Finite(state.Illumination, 0), Finite(state.Materialize, 1));
        Set(32, state.AccentRegion, Finite(state.PointerXDip, scene.LightingCenterXDip),
            Finite(state.PointerYDip, scene.LightingCenterYDip), state.Dark ? 1 : 0);
        Set(52, state.NavigationVisible ? 1 : 0, scene.IsWheelBacking ? 1 : 0, scene.Material.EdgeFeatherDip, 0);
        Method<Update>(context, 48)(context, constantBuffer, 0, 0, constantsPin.AddrOfPinnedObject(), 0, 0);
        Method<SetLayout>(context, 17)(context, 0);
        Method<SetTopology>(context, 24)(context, 4);
        Method<SetShader>(context, 11)(context, vertex, 0, 0);
        one[0] = constantBuffer; Method<SetResources>(context, 16)(context, 0, 1, one);
        one[0] = sampler; Method<SetResources>(context, 10)(context, 0, 1, one);
    }

    private void FinishFrame(nint inputView, byte[] destination, long started)
    {
        ProcessSource(inputView);
        FinishOptics(destination, started);
    }

    private void ProcessSource(nint inputView)
    {
        SourcePassCount++;
        Pass(background!, backgroundShader, inputView, 0, fieldView);
        Pass(horizontal!, horizontalShader, background!.View, 0, 0);
        Pass(blurred!, verticalShader, horizontal!.View, 0, 0);
        processedSource = true;
    }

    internal void RenderCached(GlassFrameState state, byte[] destination)
    {
        ValidateDestination(destination);
        if (!processedSource) throw new InvalidOperationException("A fresh source must be rendered before reusing its blurred textures.");
        long started = Stopwatch.GetTimestamp();
        PrepareFrame(state);
        FinishOptics(destination, started);
    }

    private void FinishOptics(byte[] destination, long started)
    {
        DrawOptics();
        LastRenderMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        started = Stopwatch.GetTimestamp();
        nint staging = readbacks[0].Texture;
        Method<CopyResource>(context, 47)(context, staging, finished!.Texture);
        Check(Method<Map>(context, 14)(context, staging, 0, 1, 0, out var mapped));
        try { CopyCompletedPixels(mapped, destination); }
        finally { Method<Unmap>(context, 15)(context, staging, 0); }
        LastReadbackMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    private void DrawOptics()
    {
        OpticalPassCount++;
        Pass(finished!, opticalShader, background!.View, blurred!.View, fieldView);
        Unbind();
    }

    private void SubmitOptics(long stamp, long started)
    {
        ReadbackSlot slot = readbacks[0].Pending ? readbacks[1] : readbacks[0];
        if (slot.Pending) throw new InvalidOperationException("A free final readback slot is required.");
        DrawOptics();
        Method<CopyResource>(context, 47)(context, slot.Texture, finished!.Texture);
        Method<EndQuery>(context, 28)(context, slot.Query);
        slot.Stamp = stamp; slot.Generation = readbackGeneration; slot.Sequence = ++nextReadbackSequence;
        slot.Pending = true; slot.Ready = false; slot.SubmittedAt = Stopwatch.GetTimestamp();
        // This offscreen pipeline has no Present to flush its small command batch.
        // Flush once on submission; GetData polling never flushes or waits.
        Method<FlushCommands>(context, 111)(context);
        ReadbackSubmissionCount++; ReadbackFlushCount++;
        slot.RenderMilliseconds = LastRenderMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        LastReadbackMilliseconds = 0;
    }

    /// <summary>Copy the newest completed valid output. A newer source submission never starves an older completed frame.</summary>
    internal bool TryComplete(byte[] destination, out long stamp)
    {
        ValidateReadbackDestination(destination);
        stamp = 0;
        ReadbackSlot? selected = null;
        foreach (var slot in readbacks)
        {
            if (!IsComplete(slot)) continue;
            if (slot.Generation != readbackGeneration) { slot.Free(); continue; }
            if (selected is null || slot.Sequence > selected.Sequence) selected = slot;
        }
        if (selected is null) return false;
        const uint DoNotWait = 0x100000;
        int result = Method<Map>(context, 14)(context, selected.Texture, 0, 1, DoNotWait, out var mapped);
        if (result == unchecked((int)0x887A000A)) return false; // DXGI_ERROR_WAS_STILL_DRAWING.
        Check(result);
        try { CopyCompletedPixels(mapped, destination); }
        finally { Method<Unmap>(context, 15)(context, selected.Texture, 0); }
        stamp = selected.Stamp;
        LastRenderMilliseconds = selected.RenderMilliseconds;
        LastReadbackMilliseconds = Stopwatch.GetElapsedTime(selected.SubmittedAt).TotalMilliseconds;
        long completedSequence = selected.Sequence;
        foreach (var slot in readbacks)
            if (slot.Pending && slot.Sequence <= completedSequence && IsComplete(slot)) slot.Free();
        ReadbackCompletionCount++;
        return true;
    }

    internal void InvalidateReadbacks()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        readbackGeneration++;
    }

    /// <summary>Retirement poll; never maps or exposes canceled pixels and never waits for GPU completion.</summary>
    internal bool TryDiscardCompletedReadbacks()
    {
        ValidateSubmission();
        foreach (var slot in readbacks) if (IsComplete(slot)) slot.Free();
        return PendingReadbackCount == 0;
    }

    private bool IsComplete(ReadbackSlot slot, bool ignoreTestHold = false)
    {
        if (!slot.Pending || readbacksHeldForTesting && !ignoreTestHold) return false;
        if (slot.Ready) return true;
        ReadbackPollCount++;
        int result = Method<ReadQuery>(context, 29)(context, slot.Query, out int completed, sizeof(int), 1); // DONOTFLUSH.
        if (result == 1) return false; // S_FALSE means GPU work has not completed.
        Check(result);
        return slot.Ready = completed != 0;
    }

    private void CopyCompletedPixels(MappedData mapped, byte[] destination)
    {
        int rowBytes = checked(scene.PixelWidth * 4);
        if (mapped.Data == 0 || mapped.RowPitch < rowBytes) throw new InvalidOperationException("Invalid finished glass staging texture.");
        for (int row = 0; row < scene.PixelHeight; row++)
            Marshal.Copy(mapped.Data + checked((int)mapped.RowPitch * row), destination, row * rowBytes, rowBytes);
    }

    internal void SetReadbackCompletionHeldForTesting(bool held)
    {
        if (!generatedTestSession) throw new InvalidOperationException("Readback delay injection requires a generated-input renderer.");
        readbacksHeldForTesting = held;
    }

    internal bool ReadbackQueriesCompletedForTesting()
    {
        if (!generatedTestSession) throw new InvalidOperationException("Query inspection requires a generated-input renderer.");
        foreach (var slot in readbacks) if (slot.Pending && !IsComplete(slot, ignoreTestHold: true)) return false;
        return true;
    }

    internal void RenderPixels(byte[] source, int sourceWidth, int sourceHeight, int sourceLeft, int sourceTop,
        GlassFrameState state, byte[] destination)
    {
        UploadGeneratedInput(source, sourceWidth, sourceHeight);
        Render(uploaded, sourceWidth, sourceHeight, sourceLeft, sourceTop, state, destination);
        generatedFrame = generatedTestSession;
    }

    private void UploadGeneratedInput(byte[] source, int sourceWidth, int sourceHeight)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        if (sourceWidth <= 0 || sourceHeight <= 0 || source.Length != checked(sourceWidth * sourceHeight * 4)) throw new ArgumentException("Expected exact BGRA input dimensions.", nameof(source));
        if (uploadedWidth != sourceWidth || uploadedHeight != sourceHeight)
        {
            Release(ref uploaded);
            var description = Description(sourceWidth, sourceHeight, 87, 0);
            Check(Method<CreateTexture>(device, 5)(device, ref description, 0, out uploaded));
            uploadedWidth = sourceWidth; uploadedHeight = sourceHeight;
        }
        UploadPixels(uploaded, source, sourceWidth);
    }

    private void UploadPixels(nint texture, byte[] source, int sourceWidth)
    {
        var pin = GCHandle.Alloc(source, GCHandleType.Pinned);
        try { Method<Update>(context, 48)(context, texture, 0, 0, pin.AddrOfPinnedObject(), (uint)sourceWidth * 4, 0); }
        finally { pin.Free(); }
    }

    internal void RenderTiles(IReadOnlyList<StudioGlassTile> tiles, GlassFrameState state, byte[] destination)
    {
        ValidateDestination(destination);
        long started = Stopwatch.GetTimestamp();
        nint view = PrepareTiles(tiles, state);
        FinishFrame(view, destination, started);
    }

    private nint PrepareTiles(IReadOnlyList<StudioGlassTile> tiles, GlassFrameState state)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (tiles.Count == 0) throw new ArgumentException("At least one monitor tile is required.", nameof(tiles));
        for (int index = 0; index < tiles.Count; index++)
        {
            StudioGlassTile tile = tiles[index];
            ValidateTexture(tile.Texture, tile.SourceWidth, tile.SourceHeight);
            if (tile.Width <= 0 || tile.Height <= 0 || tile.SourceLeft < 0 || tile.SourceTop < 0 ||
                tile.DestinationLeft < 0 || tile.DestinationTop < 0 ||
                (long)tile.SourceLeft + tile.Width > tile.SourceWidth || (long)tile.SourceTop + tile.Height > tile.SourceHeight ||
                (long)tile.DestinationLeft + tile.Width > scene.CaptureWidth || (long)tile.DestinationTop + tile.Height > scene.CaptureHeight)
                throw new ArgumentException("Monitor tile must remain within its source and padded destination.", nameof(tiles));
        }
        StudioGlassTile anchor = tiles[0];
        int anchorLeft = anchor.SourceLeft - anchor.DestinationLeft, anchorTop = anchor.SourceTop - anchor.DestinationTop;
        if (tiles.Count == 1)
        {
            CopySourceRoi(anchor.Texture, anchor.SourceWidth, anchor.SourceHeight, anchorLeft, anchorTop);
            PrepareFrame(state);
            return input!.View;
        }
        CopySourceRoi(anchor.Texture, anchor.SourceWidth, anchor.SourceHeight, anchorLeft, anchorTop);
        PrepareFrame(state);
        // One GPU preprocessing pass extends anchor edge texels into desktop gaps and the
        // exterior. Exact neighbouring monitor intersections then replace those pixels.
        tiledInput ??= new Surface(device, scene.CaptureWidth, scene.CaptureHeight, 87);
        Pass(tiledInput, clampShader, input!.View, 0, 0);
        Unbind();
        for (int index = 0; index < tiles.Count; index++)
        {
            StudioGlassTile tile = tiles[index];
            var box = new Box { Left = (uint)tile.SourceLeft, Top = (uint)tile.SourceTop,
                Right = (uint)(tile.SourceLeft + tile.Width), Bottom = (uint)(tile.SourceTop + tile.Height), Back = 1 };
            Method<Copy>(context, 46)(context, tiledInput.Texture, 0, (uint)tile.DestinationLeft,
                (uint)tile.DestinationTop, 0, tile.Texture, 0, ref box);
        }
        Set(0, scene.CaptureWidth, scene.CaptureHeight, 0, 0);
        Method<Update>(context, 48)(context, constantBuffer, 0, 0, constantsPin.AddrOfPinnedObject(), 0, 0);
        return tiledInput.View;
    }

    internal void RenderPixelsAsTilesForTesting(byte[] source, int sourceWidth, int sourceHeight,
        IReadOnlyList<StudioGlassTile> placements, GlassFrameState state, byte[] destination)
    {
        if (!generatedTestSession) throw new InvalidOperationException("Generated monitor fixtures require an offscreen test session.");
        ArgumentNullException.ThrowIfNull(placements);
        UploadGeneratedInput(source, sourceWidth, sourceHeight);
        var tiles = new StudioGlassTile[placements.Count];
        for (int index = 0; index < placements.Count; index++) tiles[index] = placements[index] with { Texture = uploaded };
        RenderTiles(tiles, state, destination);
        generatedFrame = true;
    }

    /// <summary>Generated-scene qualification only. Live sessions cannot read intermediate desktop pixels.</summary>
    internal float[] ReadIntermediateForTesting(StudioPass pass)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!generatedTestSession || !generatedFrame) throw new InvalidOperationException("Intermediate inspection requires an offscreen generated-input frame.");
        Surface source = pass switch
        {
            StudioPass.Background => background!, StudioPass.GaussianX => horizontal!,
            StudioPass.GaussianY => blurred!, _ => throw new ArgumentOutOfRangeException(nameof(pass))
        };
        var description = Description(source.Width, source.Height, 10, 0);
        description.Usage = 3; description.CpuAccessFlags = 0x20000;
        nint readback = 0;
        try
        {
            Check(Method<CreateTexture>(device, 5)(device, ref description, 0, out readback));
            Method<CopyResource>(context, 47)(context, readback, source.Texture);
            Check(Method<Map>(context, 14)(context, readback, 0, 1, 0, out var mapped));
            try
            {
                int rowValues = source.Width * 4;
                if (mapped.Data == 0 || mapped.RowPitch < rowValues * 2) throw new InvalidOperationException("Invalid floating-point qualification texture.");
                short[] row = new short[rowValues];
                float[] pixels = new float[rowValues * source.Height];
                for (int y = 0; y < source.Height; y++)
                {
                    Marshal.Copy(mapped.Data + checked((int)mapped.RowPitch * y), row, 0, rowValues);
                    for (int x = 0; x < rowValues; x++) pixels[y * rowValues + x] = (float)BitConverter.UInt16BitsToHalf(unchecked((ushort)row[x]));
                }
                return pixels;
            }
            finally { Method<Unmap>(context, 15)(context, readback, 0); }
        }
        finally { Release(ref readback); }
    }

    private void Pass(Surface surface, nint shader, nint first, nint second, nint contour)
    {
        Unbind();
        target[0] = surface.Target;
        Method<SetTarget>(context, 33)(context, 1, target, 0);
        var viewport = new Viewport { Width = surface.Width, Height = surface.Height, MaxDepth = 1 };
        Method<SetViewports>(context, 44)(context, 1, ref viewport);
        resources[0] = first; resources[1] = second; resources[2] = contour;
        Method<SetResources>(context, 8)(context, 0, 3, resources);
        Method<SetShader>(context, 9)(context, shader, 0, 0);
        Method<Draw>(context, 13)(context, 3, 0);
    }

    private void Unbind()
    {
        Method<SetResources>(context, 8)(context, 0, 3, noResources);
        target[0] = 0; Method<SetTarget>(context, 33)(context, 1, target, 0);
    }

    private nint CreateShader(string entry, int slot)
    {
        byte[] code = ShaderCode.Value[entry];
        var pin = GCHandle.Alloc(code, GCHandleType.Pinned);
        try { Check(Method<CreateShader>(device, slot)(device, pin.AddrOfPinnedObject(), (nuint)code.Length, 0, out nint shader)); return shader; }
        finally { pin.Free(); }
    }

    private static IReadOnlyDictionary<string, byte[]> CompileShaders()
    {
        Assembly assembly = typeof(D3D11StudioRenderer).Assembly;
        string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("StudioGlass.hlsl", StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        byte[] source = Encoding.UTF8.GetBytes(reader.ReadToEnd());
        var result = new Dictionary<string, byte[]>();
        foreach (string entry in new[] { "Vertex", "Background", "Horizontal", "Vertical", "Optical", "ClampSource" })
        {
            nint code = 0, errors = 0;
            try
            {
                int status = D3DCompile(source, (nuint)source.Length, "StudioGlass.hlsl", 0, 0, entry,
                    entry == "Vertex" ? "vs_5_0" : "ps_5_0", 0x8800, 0, out code, out errors);
                if (status < 0)
                {
                    string message = errors == 0 ? "Unknown shader error" : Marshal.PtrToStringAnsi(Method<BlobPointer>(errors, 3)(errors)) ?? "Shader compilation failed";
                    throw new InvalidOperationException($"Studio shader {entry}: {message}");
                }
                byte[] bytes = new byte[checked((int)Method<BlobLength>(code, 4)(code))];
                Marshal.Copy(Method<BlobPointer>(code, 3)(code), bytes, 0, bytes.Length);
                result.Add(entry, bytes);
            }
            finally { Release(ref errors); Release(ref code); }
        }
        return result;
    }

    private void Set(int offset, double a, double b, double c, double d)
    { constants[offset] = (float)a; constants[offset + 1] = (float)b; constants[offset + 2] = (float)c; constants[offset + 3] = (float)d; }
    private static double Finite(double value, double fallback) => double.IsFinite(value) ? value : fallback;
    private static TextureDescription Description(int width, int height, uint format, uint bind) => new()
    { Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1, Format = format, SampleCount = 1, BindFlags = bind };

    /// <summary>Erase every resource that can contain captured pixels before idle publication.</summary>
    internal void ClearCapturedContent()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (PendingReadbackCount != 0)
            throw new InvalidOperationException("Submitted readbacks must retire before captured resources can return to the idle cache.");
        Check(Method<DeviceStatus>(device, 39)(device));
        Unbind();
        float[] transparent = [0, 0, 0, 0];
        foreach (var surface in new[] { input, tiledInput, background, horizontal, blurred, finished })
            if (surface is not null) Method<ClearTarget>(context, 50)(context, surface.Target, transparent);
        foreach (var slot in readbacks) Method<CopyResource>(context, 47)(context, slot.Texture, finished!.Texture);
        Release(ref uploaded); uploadedWidth = uploadedHeight = 0;
        generatedFrame = false;
        processedSource = false;
        LastRenderMilliseconds = LastReadbackMilliseconds = 0;
    }

    /// <summary>Generated-input privacy qualification only; never callable for desktop sessions.</summary>
    internal byte[] ReadClearedContentForTesting()
    {
        if (!generatedTestSession) throw new InvalidOperationException("Clear inspection requires an offscreen generated-input session.");
        var result = new List<byte>();
        foreach (var surface in new[] { input, tiledInput, background, horizontal, blurred, finished })
        {
            if (surface is null) continue;
            Method<GetTextureDescription>(surface.Texture, 10)(surface.Texture, out var description);
            description.Usage = 3; description.BindFlags = 0; description.CpuAccessFlags = 0x20000;
            nint readback = 0;
            try
            {
                Check(Method<CreateTexture>(device, 5)(device, ref description, 0, out readback));
                Method<CopyResource>(context, 47)(context, readback, surface.Texture);
                result.AddRange(ReadTexture(readback, surface.Width, surface.Height, description.Format == 10 ? 8 : 4));
            }
            finally { Release(ref readback); }
        }
        foreach (var slot in readbacks) result.AddRange(ReadTexture(slot.Texture, scene.PixelWidth, scene.PixelHeight, 4));
        return result.ToArray();
    }

    private byte[] ReadTexture(nint texture, int width, int height, int bytesPerPixel)
    {
        Check(Method<Map>(context, 14)(context, texture, 0, 1, 0, out var mapped));
        try
        {
            int rowBytes = checked(width * bytesPerPixel);
            byte[] result = new byte[checked(rowBytes * height)];
            if (mapped.Data == 0 || mapped.RowPitch < rowBytes) throw new InvalidOperationException("Invalid cleared qualification texture.");
            for (int row = 0; row < height; row++)
                Marshal.Copy(mapped.Data + checked((int)mapped.RowPitch * row), result, row * rowBytes, rowBytes);
            return result;
        }
        finally { Method<Unmap>(context, 15)(context, texture, 0); }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void ClearTarget(nint context, nint target, [In] float[] color);

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (context != 0) Unbind();
        finished?.Dispose(); blurred?.Dispose(); horizontal?.Dispose(); background?.Dispose(); tiledInput?.Dispose(); input?.Dispose();
        Release(ref uploaded);
        foreach (var slot in readbacks) { Release(ref slot.Query); Release(ref slot.Texture); slot.Free(); }
        Release(ref fieldView); Release(ref field);
        Release(ref sampler); Release(ref constantBuffer); Release(ref opticalShader); Release(ref verticalShader);
        Release(ref horizontalShader); Release(ref backgroundShader); Release(ref clampShader); Release(ref vertex);
        if (constantsPin.IsAllocated) constantsPin.Free();
        Release(ref context); Release(ref device);
    }

    private sealed class Surface : IDisposable
    {
        internal nint Texture, View, Target;
        internal readonly int Width, Height;
        internal Surface(nint device, int width, int height, uint format, bool render = true, bool shaderResource = true)
        {
            Width = width; Height = height;
            try
            {
                var description = Description(width, height, format, (render ? 0x20u : 0) | (shaderResource ? 8u : 0));
                Check(Method<CreateTexture>(device, 5)(device, ref description, 0, out Texture));
                if (shaderResource) Check(Method<CreateView>(device, 7)(device, Texture, 0, out View));
                if (render) Check(Method<CreateView>(device, 9)(device, Texture, 0, out Target));
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { Release(ref Target); Release(ref View); Release(ref Texture); }
    }

    private sealed class ReadbackSlot
    {
        internal nint Texture, Query;
        internal bool Pending, Ready;
        internal long Stamp, Sequence, Generation, SubmittedAt;
        internal double RenderMilliseconds;
        internal void Free()
        {
            Pending = Ready = false;
            Stamp = Sequence = Generation = SubmittedAt = 0;
            RenderMilliseconds = 0;
        }
    }
}

internal enum StudioPass { Background, GaussianX, GaussianY }

internal readonly record struct StudioGlassTile(nint Texture, int SourceWidth, int SourceHeight,
    int SourceLeft, int SourceTop, int DestinationLeft, int DestinationTop, int Width, int Height);
