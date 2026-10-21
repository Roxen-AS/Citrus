using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Threading;
using Citrus.Core;
using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

/// <summary>Test-only desktop-compositor crop over an owned scene. Never used as a production texture.</summary>
internal static class GlassCompositorCapture
{
    internal static Bitmap Capture(GlassOwnedScene scene, nint companion, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var snapshot = RequireOwned(scene, companion, path + ".preflight.json");
        int hr = BackdropNativeMethods.DwmFlush();
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        ScreenRect bounds = snapshot.Crop;
        nint screen = GetDC(0), memory = 0, nativeBitmap = 0, previous = 0;
        try
        {
            if (screen == 0) throw new System.ComponentModel.Win32Exception();
            memory = CreateCompatibleDC(screen);
            nativeBitmap = CreateCompatibleBitmap(screen, (int)bounds.Width, (int)bounds.Height);
            if (memory == 0 || nativeBitmap == 0) throw new System.ComponentModel.Win32Exception();
            previous = SelectObject(memory, nativeBitmap);
            if (previous == 0 || previous == -1) throw new System.ComponentModel.Win32Exception();
            if (!BitBlt(memory, 0, 0, (int)bounds.Width, (int)bounds.Height, screen, (int)bounds.Left, (int)bounds.Top, 0x40CC0020))
                throw new System.ComponentModel.Win32Exception(); // SRCCOPY | CAPTUREBLT includes layered HWNDs.
            var bitmap = Image.FromHbitmap(nativeBitmap);
            bitmap.Save(path, ImageFormat.Png);
            return bitmap;
        }
        finally
        {
            if (previous != 0 && previous != -1) SelectObject(memory, previous);
            if (nativeBitmap != 0) BackdropNativeMethods.DeleteObject(nativeBitmap);
            if (memory != 0) DeleteDC(memory);
            if (screen != 0) ReleaseDC(0, screen);
        }
    }
    internal static GlassCaptureSnapshot RequireOwned(GlassOwnedScene scene, nint companion, string metadataPath, int paddingPixels = 0)
    {
        // Identity and every physical input pixel are prerequisites; visual z order alone proved insufficient on OOBE.
        var snapshot = scene.ReadCaptureSnapshot(companion, paddingPixels);
        return QualifySnapshot(snapshot, metadataPath);
    }

    internal static async Task<GlassCaptureSnapshot> RequireOwnedAsync(GlassOwnedScene scene, nint companion, string metadataPath, int paddingPixels = 0)
    {
        var snapshot = await scene.ReadCaptureSnapshotAsync(companion, paddingPixels);
        return QualifySnapshot(snapshot, metadataPath);
    }

    private static GlassCaptureSnapshot QualifySnapshot(GlassCaptureSnapshot snapshot, string metadataPath)
    {
        var preflight = GlassCaptureOwnership.Check(snapshot);
        // Keep the decision from the original snapshot. Read-only refusal identity
        // metadata cannot make a rejected crop eligible or permit a screen read.
        if (!preflight.Allowed) snapshot = GlassNativeDesktopProbe.AddRefusalIdentities(snapshot);
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(new
        {
            CheckedBeforeScreenRead = true, preflight.Allowed, preflight.Reason, CaptureSkipped = !preflight.Allowed,
            Method = "Actual input/thread desktop and active session; exhaustive full-crop input roots; native visual z order",
            Snapshot = snapshot
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (!preflight.Allowed)
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(metadataPath)!, "metadata.json"), JsonSerializer.Serialize(new
            {
                Eligibility = "CapabilityUnavailableNoOpticalEvidence", CaptureSkipped = true, SkipReason = preflight.Reason,
                ProductionCompanionValidated = false, ScreenReadPerformedForRefusedFrame = false,
                DiagnosticFile = Path.GetFileName(metadataPath), Snapshot = snapshot,
                Note = "Host input desktop/session or scene ownership is unavailable. No setup/privacy UI interaction or OS preference write was attempted."
            }, new JsonSerializerOptions { WriteIndented = true }));
            throw new InvalidOperationException("Native glass capability unavailable; screen capture skipped: " + preflight.Reason);
        }
        return snapshot;
    }

    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint item);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);

    internal static double EdgeEnergy(Bitmap image, double scale)
    {
        double sum = 0; int count = 0;
        for (int y = 1; y < image.Height; y++)
            for (int x = 1; x < image.Width; x++)
            {
                double radius = Math.Sqrt(Math.Pow(x / scale - 184, 2) + Math.Pow(y / scale - 184, 2));
                if (radius < 169 || radius > 173) continue;
                var a = image.GetPixel(x, y); var b = image.GetPixel(x - 1, y);
                sum += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B); count++;
            }
        return sum / Math.Max(1, count);
    }
    internal static double LiveDifference(Bitmap a, Bitmap b, double scale)
    {
        double sum = 0; int count = 0;
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
            {
                double radius = Math.Sqrt(Math.Pow(x / scale - 184, 2) + Math.Pow(y / scale - 184, 2));
                if (radius < 169 || radius > 173) continue;
                var first = a.GetPixel(x, y); var second = b.GetPixel(x, y);
                sum += Math.Abs(first.R - second.R) + Math.Abs(first.G - second.G) + Math.Abs(first.B - second.B); count++;
            }
        return sum / Math.Max(1, count);
    }
}

[Collection("Desktop")]
public sealed class GlassNativeProofTests(DesktopFixture desktop)
{
    [Fact]
    public Task Owned_compositor_frames_record_actual_clipped_disabled_acrylic_eligibility() => desktop.RunAsync(async () =>
    {
        Assert.Equal(Architecture.X64, RuntimeInformation.ProcessArchitecture);
        string root = Environment.GetEnvironmentVariable("CITRUS_GLASS_ARTIFACTS") ?? Path.Combine(AppContext.BaseDirectory, "TestResults", "glass-native");
        Directory.CreateDirectory(root);
        using var scene = new GlassOwnedScene(); scene.Show(activateOwnedBackground: true);
        var fixtureActivation = new
        {
            Experiment = "One normal activation attempt on the owned opaque background before showing the nonactivating wheel; no force or retry",
            scene.OriginalForeground, scene.BackgroundActivationAttempted, scene.BackgroundActivationReturned,
            scene.BackgroundActivationSucceeded, scene.BaselineForeground,
            OwnedBackground = scene.BackgroundHandle.ToInt64(),
            ForegroundBaselineEstablishedAfterOwnedActivationAttempt = true
        };
        // Preserve setup diagnostics even when the unchanged first capture preflight refuses all pixels.
        File.WriteAllText(Path.Combine(root, "fixture-activation.json"), JsonSerializer.Serialize(fixtureActivation, new JsonSerializerOptions { WriteIndented = true }));
        await scene.WaitForPresentationAsync();
        using var monitor = new MaterialPreferenceMonitor(Dispatcher.CurrentDispatcher, () => new());
        using var controller = new WheelBackdropController(nativeCompanionValidated: true);
        controller.Attach(scene.WheelHandle);
        controller.Invalidated += () => scene.SetShellState(controller.Result.State);
        // Product presentation has its own gated controller. Finish its bounded solid-first
        // callbacks before explicitly exposing the native tint as the no-helper measurement control.
        async Task DrainProductPresentationAsync()
        {
            for (int i = 0; i < 100 && scene.Wheel.MaterialPresentationPending; i++) await Task.Delay(10);
            Assert.False(scene.Wheel.MaterialPresentationPending, "Product material presentation did not settle before the controlled capture.");
        }
        await DrainProductPresentationAsync();
        scene.SetShellState(BackdropState.ConfiguredNativeAcrylic);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        File.WriteAllText(Path.Combine(root, "01-owned-stripes-no-companion.state.json"), JsonSerializer.Serialize(new
        {
            Purpose = "Native-tint-only measurement control on the actual Shell, not an optical native-material claim",
            ShellState = scene.Wheel.MaterialState.ToString(), Helper = controller.CompanionHandle.ToInt64(),
            CaptureStillRequiresOwnershipPreflight = true
        }, new JsonSerializerOptions { WriteIndented = true }));
        using var baseline = GlassCompositorCapture.Capture(scene, controller.CompanionHandle, Path.Combine(root, "01-owned-stripes-no-companion.png"));
        var result = controller.Synchronize(1, scene.Bounds, scene.DpiScale, true, monitor.Decision, false);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(300); // Bounded native compositor settle, never an application animation loop.
        // The external proof controller's native placement can invalidate the product controller.
        // Drain that callback, then use the proof controller's actual result, never an alpha guess.
        await DrainProductPresentationAsync();
        result = controller.Result;
        scene.SetShellState(result.State);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        result = controller.Result;
        var configuredShellState = scene.Wheel.MaterialState;
        Assert.Equal(result.State, configuredShellState);
        using var configured = GlassCompositorCapture.Capture(scene, controller.CompanionHandle, Path.Combine(root, "02-disabled-clipped-native.png"));
        scene.AssertWheelOwnsClip(controller.CompanionHandle);
        scene.SetPattern(true);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(300);
        await DrainProductPresentationAsync();
        scene.SetShellState(controller.Result.State);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var changedShellState = scene.Wheel.MaterialState;
        Assert.Equal(controller.Result.State, changedShellState);
        using var changed = GlassCompositorCapture.Capture(scene, controller.CompanionHandle, Path.Combine(root, "03-live-owned-background-change.png"));
        nint helper = controller.CompanionHandle;
        nint immediatelyAboveHelper = helper == 0 ? 0 : BackdropNativeMethods.GetWindow(helper, 3);
        long styles = helper == 0 ? 0 : BackdropNativeMethods.GetWindowLongPtr(helper, -16).ToInt64();
        long extended = helper == 0 ? 0 : BackdropNativeMethods.GetWindowLongPtr(helper, -20).ToInt64();
        nint region = BackdropNativeMethods.CreateRectRgn(0, 0, 0, 0);
        int regionKind;
        BackdropNativeMethods.Rect regionBounds = default;
        bool centerIncluded = false, cornerIncluded = false;
        try
        {
            regionKind = helper == 0 ? 0 : BackdropNativeMethods.GetWindowRgn(helper, region);
            if (regionKind != 0)
            {
                BackdropNativeMethods.GetRgnBox(region, out regionBounds);
                centerIncluded = BackdropNativeMethods.PtInRegion(region, (int)(184 * scene.DpiScale), (int)(184 * scene.DpiScale));
                cornerIncluded = BackdropNativeMethods.PtInRegion(region, (int)(9 * scene.DpiScale), (int)(9 * scene.DpiScale));
            }
        }
        finally { BackdropNativeMethods.DeleteObject(region); }
        double baselineEdges = GlassCompositorCapture.EdgeEnergy(baseline, scene.DpiScale);
        double configuredEdges = GlassCompositorCapture.EdgeEnergy(configured, scene.DpiScale);
        double liveDifference = GlassCompositorCapture.LiveDifference(configured, changed, scene.DpiScale);
        controller.Hide(1); scene.SetShellState(controller.Result.State);
        Assert.Equal(BackdropState.Solid, scene.Wheel.MaterialState);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        using var solid = GlassCompositorCapture.Capture(scene, controller.CompanionHandle, Path.Combine(root, "04-opaque-fallback.png"));
        var metadata = new
        {
            SourceSha = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "UnavailableOutsideCIRecordExactCommitAlongsideArtifact",
            CaptureOwnership = "Each frame has a preflight JSON; full crop checked before every screen read",
            OperatingSystem = Environment.OSVersion.ToString(), RuntimeInformation.OSArchitecture, RuntimeInformation.ProcessArchitecture,
            Host = "Windows 11 ARM64; x64 product/test process under emulation when run by windows-glass.yml",
            Build = Environment.OSVersion.Version.Build, Policy = monitor.Decision, Result = result,
            ActualMonitorDpiScale = scene.DpiScale, RasterScaling = false, Bounds = scene.Bounds,
            Wheel = scene.WheelHandle.ToInt64(), Helper = helper.ToInt64(), Owner = controller.LifetimeOwnerHandle.ToInt64(),
            HelperOwner = helper == 0 ? 0 : BackdropNativeMethods.GetWindow(helper, 4).ToInt64(),
            WindowImmediatelyAboveHelper = immediatelyAboveHelper.ToInt64(),
            Style = styles, ExtendedStyle = extended, RegionKind = regionKind, RegionBounds = regionBounds.ToScreenRect(), CenterIncluded = centerIncluded, CornerIncluded = cornerIncluded,
            FixtureActivation = fixtureActivation,
            ForegroundBefore = scene.ForegroundBefore.ToInt64(), ForegroundAfter = BackdropNativeMethods.GetForegroundWindow().ToInt64(),
            WholeEllipseWindowFromPoint = "passed", OleAdmission = "Synthetic callback on real registered OLE HWND; external drag not proven",
            BaselineEdgeEnergy = baselineEdges, ConfiguredEdgeEnergy = configuredEdges, LiveBackgroundDifference = liveDifference,
            Eligibility = "RawMeasurementsRequireHumanFrameReviewSolidGateRemainsFalse",
            UniversalOpticalThresholdApplied = false,
            ShellPresentation = new
            {
                Baseline = "Explicit native-tint-only measurement control on the real Shell; no helper and no optical eligibility claim",
                BaselineShellState = BackdropState.ConfiguredNativeAcrylic.ToString(),
                ConfiguredShellState = configuredShellState.ToString(),
                LiveBackgroundShellState = changedShellState.ToString(),
                Fallback = "Controller.Hide result drives the real Shell; neutral-center backing is unchanged",
                FallbackShellState = scene.Wheel.MaterialState.ToString()
            },
            ProductionCompanionValidated = false,
            Limitation = "SetWindowRgn/WS_DISABLED may suppress Acrylic. S_OK never enables the production gate. No OS transparency settings were changed."
        };
        File.WriteAllText(Path.Combine(root, "metadata.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        Assert.Equal(scene.ForegroundBefore, BackdropNativeMethods.GetForegroundWindow());
        Assert.False(BackdropNativeMethods.IsWindowVisible(helper));
        // These are configuration/capture assertions. Optical eligibility is deliberately a separate recorded result.
        if (result.IsNative)
        {
            Assert.NotEqual(0, styles & BackdropNativeMethods.WsDisabled);
            Assert.Equal(0, extended & BackdropNativeMethods.WsExLayered);
            Assert.Equal(3, regionKind); // COMPLEXREGION: ellipse, never a rectangular frosting helper.
            Assert.True(centerIncluded); Assert.False(cornerIncluded);
            Assert.Equal(scene.WheelHandle, immediatelyAboveHelper);
            Assert.InRange(regionBounds.Right, 1, (int)scene.Bounds.Width);
            Assert.InRange(regionBounds.Bottom, 1, (int)scene.Bounds.Height);
        }
    });
}
