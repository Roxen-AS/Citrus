using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Citrus.Core;
using Citrus.Windows.Presentation;
using Xunit.Abstractions;
using Point = System.Windows.Point;
using Image = System.Windows.Controls.Image;
using ShapePath = System.Windows.Shapes.Path;
using Color = System.Windows.Media.Color;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class NativeLiveWheelGlassTests(DesktopFixture desktop, ITestOutputHelper output)
{
    private const double ProductionTintAlpha = .68;
    private const double ProductionTintBlue = 232 / 255d;
    private const double ProductionBlurSupportDip = 23;
    private const double ProductionShadowStrength = 0;
    private const int NavigationCenterY = 390;
    private const double CheckerTileDip = NavigationCenterY - 184;
    private const double CheckerSourceLow = 191, SourceHigh = 255;
    private const double CheckerSourceFraction = CheckerSourceLow / SourceHigh;
    private const double ExpectedCheckerDelta = (SourceHigh - CheckerSourceLow) * (1 - ProductionTintAlpha);
    private const double ExpectedBroadLow = SourceHigh * ProductionTintBlue * ProductionTintAlpha;
    private const double ExpectedBroadDelta = SourceHigh * (1 - ProductionTintAlpha);
    private const double ExpectedBroadHigh = ExpectedBroadLow + ExpectedBroadDelta;
    private const double ChannelTolerance = 3;
    // Qualify black/white endpoints against the accepted pale #E1E8E8 backing
    // first, then normalize checker samples by those independently measured endpoints.
    // Selection belongs to the foreground and cannot tint these optical samples.
    // Center one checker cell on the hub and the next on navigation at y390.
    // Their206-DIP separation keeps the two surfaces in opposite plateaus.
    // Every sampled source position stays at least93DIP from a tile edge,
    // beyond the23-DIP finite Gaussian support.
    // Studio's default20-DIP calibration fixture remains unchanged.
    private const double MaximumShadowBias = SourceHigh * .6 * ProductionShadowStrength * (1 - ProductionTintAlpha);
    private const double MinimumCheckerChange = ExpectedCheckerDelta * .8;
    private const double MinimumCheckerRange = (1 - CheckerSourceFraction) * .8;

    [NativeLiquidGlassFact]
    public Task Admitted_wheel_refracts_owned_desktop_with_both_windows_excluded_and_policy_teardown_restores_them() => desktop.RunAsync(async () =>
    {
        string artifacts = Path.Combine(AppContext.BaseDirectory, "TestResults", "live-wheel-glass",
            DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));
        string samplePath = Path.Combine(Path.GetTempPath(), "citrus-live-wheel-owned-" + Guid.NewGuid().ToString("N") + ".fixture");
        GlassOwnedScene? owned = null;
        GuardedSession? capturedSession = null;
        var dispatcher = Dispatcher.CurrentDispatcher;
        var presentationLatency = new List<double>();
        var presentationWork = new List<double>();
        var presentationQueue = new List<double>();
        int presentedFrames = 0;
        GlassFrameState presentedState = default;
        StudioGlassSession? Prime(GlassScene optical, int x, int y) => StudioGlassSession.TryCreate(optical, x, y);
        ILiveGlassSession? Create(GlassScene optical, int x, int y)
        {
            dispatcher.Invoke(() => GlassCompositorCapture.RequireOwned(owned!, 0,
                Path.Combine(artifacts, "create.preflight.json"), optical.PaddingPixels));
            var native = StudioGlassSession.TryCreate(optical, x, y);
            return native is null ? null : capturedSession = new GuardedSession(native, () => dispatcher.Invoke(() =>
                GlassCompositorCapture.RequireOwned(owned!, 0, Path.Combine(artifacts, "frame.preflight.json"), optical.PaddingPixels)));
        }
        var wheel = new WheelWindow(new OperationCatalog([new OwnedEngine()]), null, null, null, Create);
        wheel.ApplySettings(new CitrusSettings { ReduceMotion = true, SoundEnabled = false });
        owned = new GlassOwnedScene(wheel);
        var opticalSurface = (FrameworkElement)wheel.FindName("Surface");
        double RenderScale()
        {
            Point origin = opticalSurface.PointToScreen(default);
            Point right = opticalSurface.PointToScreen(new Point(368, 0));
            // Match the product's integer physical capture width after measuring
            // the actual Viewbox transform, including layout rounding.
            return Math.Round(right.X - origin.X) / 368;
        }
        var controller = (LiveWheelGlass)typeof(WheelWindow).GetField("glass", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(wheel)!;
        var productPresented = controller.FramePresented;
        controller.PresentationMeasured = (queue, work) => { presentationQueue.Add(queue); presentationWork.Add(work); };
        controller.FramePresented = (optical, pixels) =>
        {
            productPresented?.Invoke(optical, pixels);
            Assert.True(dispatcher.CheckAccess());
            presentedState = capturedSession!.LastState;
            presentedFrames++;
            presentationLatency.Add(System.Diagnostics.Stopwatch.GetElapsedTime(capturedSession.RenderCompleted).TotalMilliseconds);
        };
        try
        {
            // An unknown extension deliberately avoids context-family subpages. The
            // fixture exercises one full nine-target page and its real archive pager.
            File.WriteAllText(samplePath, "Owned native glass wheel input");
            Assert.Equal(9, WheelGeometry.MaxTargetsPerPage);
            owned.SetSolidPattern(light: false); owned.Show(activateOwnedBackground: true);
            wheel.ShowAtPointer(OperationMode.Convert);
            Hover(DragDrop.PreviewDragEnterEvent, new Point(184, 184));
            await WaitUntil(() => ((Canvas)wheel.FindName("Sectors")).Children.Count == WheelGeometry.MaxTargetsPerPage, "prepared nine-petal menu");
            Assert.Equal(Visibility.Visible, ((Border)wheel.FindName("NavigationPlate")).Visibility);
            // Let queued product placement and native/WPF DPI settle before the
            // final owned centering. A subsequent idle yield could otherwise apply
            // the wheel's original pointer anchor after a synchronous test move.
            int padding = await owned.CenterCaptureTargetWithPaddingAsync(
                _ => StudioGlassMath.CapturePaddingPixels(GlassMaterial.Wheel, RenderScale()));
            double renderScale = RenderScale();
            var optical = GlassScene.WheelPresentation(WheelGeometry.MaxTargetsPerPage, renderScale, navigationVisible: true);
            Assert.Equal(padding, optical.PaddingPixels);
            Assert.InRange(Math.Abs(renderScale / owned.DpiScale - WheelPresentationMetrics.Scale),
                0, 1 / (WheelGeometry.Width * owned.DpiScale));
            GlassCompositorCapture.RequireOwned(owned, 0, Path.Combine(artifacts, "admitted-padded.preflight.json"), optical.PaddingPixels);
            // Initialize the real shared WGC/D3D path without rendering, mapping, or
            // saving any desktop pixels. Reassert only our owned fixture afterward.
            Point screenOrigin = opticalSurface.PointToScreen(default);
            await Task.Run(() =>
            {
                using var prime = Prime(optical, (int)Math.Round(screenOrigin.X), (int)Math.Round(screenOrigin.Y));
                Assert.NotNull(prime);
            });
            Assert.True(NativeMethods.SetWindowPos(owned.BackgroundHandle, new nint(-1), 0, 0, 0, 0, 0x13));
            Assert.True(NativeMethods.SetWindowPos(owned.CaptureTargetHandle, new nint(-1), 0, 0, 0, 0, 0x13));
            nint capture = wheel.DragCaptureHandle;
            Assert.NotEqual(0, capture);
            wheel.ApplyMaterialPolicy(new(true, true, "OwnedStudioOpticalProof"), false, 1);
            await WaitUntil(() => wheel.MaterialState == BackdropState.LiveRefractiveGlass && presentedFrames > 0,
                "first native Studio frame", () => $"Controller: {controller.LastFailure}; Session: {capturedSession?.Diagnostic}; Creation: {StudioGlassSession.LastCreationDiagnostic}");
            Assert.NotNull(capturedSession);
            Assert.Equal(renderScale, capturedSession.Scene.Scale, 8);
            Assert.Equal(GlassMaterial.Wheel, capturedSession.Scene.Material);
            Assert.Equal(ProductionBlurSupportDip, capturedSession.Scene.Material.BlurRadiusDip);
            Assert.Equal(ProductionTintAlpha, capturedSession.Scene.Material.TintAlpha);
            Assert.Equal(225 / 255d, capturedSession.Scene.Material.TintRed);
            Assert.Equal(232 / 255d, capturedSession.Scene.Material.TintGreen);
            Assert.Equal(ProductionTintBlue, capturedSession.Scene.Material.TintBlue);
            Assert.Equal(ProductionShadowStrength, capturedSession.Scene.Material.ShadowStrength);
            Assert.Equal(new[] { 1, 32 }, capturedSession.Scene.Regions.Select(region => region.Id));
            Assert.Equal(1, presentedState.Materialize); Assert.Equal(0, presentedState.Illumination);
            Assert.True(GetWindowDisplayAffinity(owned.CaptureTargetHandle, out uint wheelAffinity)); Assert.Equal(0x11u, wheelAffinity);
            Assert.True(GetWindowDisplayAffinity(capture, out uint captureAffinity)); Assert.Equal(0x11u, captureAffinity);
            var body = (Image)wheel.FindName("GlassBody");
            AssertTransparentContour((BitmapSource)body.Source, capturedSession.Scene);
            byte[] before = SampleFlatInteriors(body, renderScale);
            owned.SetSolidPattern(light: true);
            await WaitUntil(() => EndpointCalibrationReady(before, SampleFlatInteriors(body, renderScale)),
                "owned black/white controls qualify white brightening and retained desktop variation",
                () => $"Expected every endpoint delta={ExpectedBroadDelta:F2}+/-2 and bounded optical shadow; {Samples(body, renderScale)}; {capturedSession.Diagnostic}");
            byte[] broadAfter = SampleFlatInteriors(body, renderScale);
            Save("owned-studio-neutral-light");

            owned.SetStudioCheckerboard(tileDip: CheckerTileDip, cellCenterInTargetDip: new Point(184, 184));
            await WaitUntil(() => HasCheckerboard(SampleFlatInteriors(body, renderScale), before, broadAfter), "owned coarse checkerboard reaches both frosted flat interiors",
                () => $"Expected calibrated checker fractions={CheckerSourceFraction:F3}/1, range>={MinimumCheckerRange:F3}; {Samples(body, renderScale)}; {capturedSession.Diagnostic}");
            Hover(DragDrop.PreviewDragOverEvent, new Point(184, 184));
            var backingBeforeSelection = body.Source;
            int presentationsBeforeSelection = presentedFrames;
            Hover(DragDrop.PreviewDragOverEvent, new Point(184, 62));
            AssertSelectedForeground(0, "PNG");
            Assert.Same(backingBeforeSelection, body.Source);
            Assert.Equal(presentationsBeforeSelection, presentedFrames);
            Assert.Equal(0, presentedState.AccentRegion);
            Save("owned-studio-wheel-active0");
            Hover(DragDrop.PreviewDragOverEvent, new Point(184, 184));
            var fourth = WheelGeometry.At(122, 3 * 360d / WheelGeometry.MaxTargetsPerPage);
            Hover(DragDrop.PreviewDragOverEvent, new Point(fourth.X, fourth.Y));
            AssertSelectedForeground(3, "TIFF");
            Assert.Same(backingBeforeSelection, body.Source);
            Assert.Equal(presentationsBeforeSelection, presentedFrames);
            Assert.Equal(0, presentedState.AccentRegion);
            Save("owned-studio-wheel-active3");
            byte[] checkerBefore = SampleFlatInteriors(body, renderScale);
            owned.SetStudioCheckerboard(alternate: true, tileDip: CheckerTileDip, cellCenterInTargetDip: new Point(184, 184));
            await WaitUntil(() => HasCheckerboard(SampleFlatInteriors(body, renderScale), before, broadAfter) &&
                InvertedContent(checkerBefore, SampleFlatInteriors(body, renderScale), before, broadAfter),
                "inverted owned checkerboard appears beneath selected action",
                () => $"Expected checker mean change >{MinimumCheckerChange:F2}; {Samples(body, renderScale)}; {capturedSession.Diagnostic}");
            byte[] checkerAfter = SampleFlatInteriors(body, renderScale);
            Save("owned-studio-wheel-active3-background-changed");
            Assert.Null(wheel.FindName("ActionPlate"));
            Assert.Null(wheel.FindName("ActionDescription"));
            var caption = Assert.IsType<TextBlock>(wheel.FindName("Caption"));
            Assert.Equal("TIFF", caption.Text);
            Assert.True(caption.IsVisible);
            VisualArtifacts.AssertInside(caption, (FrameworkElement)wheel.FindName("Surface"));

            var timing = capturedSession!.RenderMilliseconds.ToArray();
            Directory.CreateDirectory(artifacts);
            File.WriteAllText(Path.Combine(artifacts, "studio-native-metadata.json"), JsonSerializer.Serialize(new
            {
                Renderer = nameof(StudioGlassSession), RawDesktopSourceInjected = false,
                PaddedOwnershipGuardBeforeEveryRender = true, capturedSession.Scene.PaddingPixels,
                capturedSession.Scene.WidthDip, capturedSession.Scene.HeightDip,
                capturedSession.Scene.PixelWidth, capturedSession.Scene.PixelHeight,
                MonitorDpiScale = owned.DpiScale, CanonicalToScreenScale = renderScale,
                VisibleScaleRelativeToMonitor = renderScale / owned.DpiScale,
                CaptureWidth = capturedSession.Scene.CaptureWidth, CaptureHeight = capturedSession.Scene.CaptureHeight,
                ContentProof = $"Owned black/white endpoints independently qualify .68 pale #E1E8E8 frosting and retained source delta at every sample; an aligned{CheckerTileDip:0}-DIP .75/1 checker must retain both calibrated plateaus, spatial range and complementary inverted pixel pairs within3/255. Studio's default20-DIP calibration fixture is unchanged.",
                ProductionFrosting = new { BlurSupportDip = ProductionBlurSupportDip, GaussianSigmaDip = ProductionBlurSupportDip / 3,
                    NeutralTint = "#E1E8E8", Alpha = ProductionTintAlpha, OpticalShadowStrength = ProductionShadowStrength },
                WheelFinish = new { capturedSession.Scene.Material.FresnelIntensity,
                    capturedSession.Scene.Material.GlareIntensity, capturedSession.Scene.Material.SelectionTintOpacity },
                Samples = new { HubGrid = "174..194DIP in4-DIP steps (36 pixels)", NavigationMidline = $"x174..194DIP in4-DIP steps at y{NavigationCenterY} (6 pixels)",
                    Count = checkerBefore.Length, AllInsideFlatOpticalInteriors = true },
                CheckerExpectation = new { TileDip = CheckerTileDip, AlignedHubCellCenter = new Point(184, 184),
                    SourceLow = CheckerSourceLow, High = SourceHigh, NormalizedLow = CheckerSourceFraction,
                    IdealDelta = ExpectedCheckerDelta, MinimumRange = MinimumCheckerRange, MinimumMeanInversion = MinimumCheckerChange,
                    ChannelTolerance, BeforeMinimum = checkerBefore.Min(), BeforeMaximum = checkerBefore.Max(),
                    AfterMinimum = checkerAfter.Min(), AfterMaximum = checkerAfter.Max(),
                    NormalizedBeforeMinimum = Normalize(checkerBefore, before, broadAfter).Min(),
                    NormalizedBeforeMaximum = Normalize(checkerBefore, before, broadAfter).Max(),
                    NormalizedAfterMinimum = Normalize(checkerAfter, before, broadAfter).Min(),
                    NormalizedAfterMaximum = Normalize(checkerAfter, before, broadAfter).Max(),
                    ActualMeanInversion = Difference(checkerBefore, checkerAfter),
                    MaximumComplementError = ComplementError(checkerBefore, checkerAfter, before, broadAfter) },
                SolidEndpointCalibration = new { UnshadowedBlack = ExpectedBroadLow, UnshadowedWhite = ExpectedBroadHigh,
                    MaximumOpticalShadowBias = MaximumShadowBias, IdealDelta = ExpectedBroadDelta,
                    EveryPixelDeltaTolerance = 2, BlackMinimum = before.Min(), BlackMaximum = before.Max(),
                    WhiteMinimum = broadAfter.Min(), WhiteMaximum = broadAfter.Max(),
                    MinimumDelta = before.Zip(broadAfter, (black, white) => white - black).Min(),
                    MaximumDelta = before.Zip(broadAfter, (black, white) => white - black).Max(),
                    ActualMeanInversion = Difference(before, broadAfter) },
                Navigation = new { RegionId = 32, Rendered = true, FirstPageCount = WheelGeometry.MaxTargetsPerPage, TotalCatalogCount = new OwnedEngine().Operations.Count },
                // Unset pointer coordinates are intentional NaNs in render state,
                // represented as absent values only in this JSON evidence snapshot.
                FrameState = new { presentedState.AccentRegion,
                    PointerXDip = double.IsFinite(presentedState.PointerXDip) ? (double?)presentedState.PointerXDip : null,
                    PointerYDip = double.IsFinite(presentedState.PointerYDip) ? (double?)presentedState.PointerYDip : null,
                    presentedState.Illumination, presentedState.Materialize, presentedState.Dark, presentedState.NavigationVisible },
                PresentedFrames = presentedFrames,
                SessionRenderCalls = timing.Length,
                SessionRenderAndReadbackMeanMilliseconds = timing.Length == 0 ? 0 : timing.Average(),
                SessionRenderAndReadbackMaximumMilliseconds = timing.Length == 0 ? 0 : timing.Max(),
                WarmSessionRenderAndReadbackMeanMilliseconds = timing.Length <= 2 ? 0 : timing.Skip(2).Average(),
                WarmUiPresentationMeanMilliseconds = presentationWork.Count <= 2 ? 0 : presentationWork.Skip(2).Average(),
                WarmUiPresentationMaximumMilliseconds = presentationWork.Count <= 2 ? 0 : presentationWork.Skip(2).Max(),
                WarmDispatcherQueueMeanMilliseconds = presentationQueue.Count <= 2 ? 0 : presentationQueue.Skip(2).Average(),
                RenderCompletionToUiCallbackMeanMilliseconds = presentationLatency.Count == 0 ? 0 : presentationLatency.Average(),
                TimingLimitation = "Session timing excludes exhaustive ownership preflight; includes native capture acquisition, optical passes and finished-region readback. UI latency includes queueing, WritePixels and the product contrast callback; it is not a compositor present timestamp.",
                Artifacts = "Neutral, immediate foreground selection at action0/action3 with unchanged backing/AccentRegion0, changed checkerboard; native body and composed WPF views include optical navigation region32",
                InputQualification = "Routed file admission on registered OLE HWND, not physical Explorer drag",
                VisualAcceptance = "Technical and generated-scene evidence only; human fidelity acceptance remains separate"
            }, new JsonSerializerOptions { WriteIndented = true }));
            wheel.ApplyMaterialPolicy(new(false, false, "ReduceTransparency"), false, 1);
            await wheel.GlassCompletion.WaitAsync(TimeSpan.FromSeconds(90));
            Assert.True(capturedSession!.Disposed);
            Assert.Equal(BackdropState.Solid, wheel.MaterialState); Assert.Null(body.Source);
            Assert.True(GetWindowDisplayAffinity(owned.CaptureTargetHandle, out wheelAffinity)); Assert.Equal(0u, wheelAffinity);
            Assert.True(GetWindowDisplayAffinity(capture, out captureAffinity)); Assert.Equal(0u, captureAffinity);
            wheel.Dismiss(); await wheel.GlassCompletion;
            Assert.False(BackdropNativeMethods.IsWindow(capture)); Assert.False(wheel.IsVisible);
            output.WriteLine($"Native Studio session, padded ownership guard, production frosted desktop changes, exact shell/navigation contour, action0/action3 artifacts, and affinity restoration passed. Evidence {artifacts}. Routed file admission; not a physical Explorer drag.");
        }
        finally
        {
            controller.FramePresented = productPresented;
            controller.PresentationMeasured = null;
            wheel.ApplyMaterialPolicy(new(false, false, "ProofCleanup"), false, 1);
            await wheel.GlassCompletion.WaitAsync(TimeSpan.FromSeconds(90));
            owned.Dispose();
            File.Delete(samplePath);
        }

        void AssertSelectedForeground(int selected, string expectedCaption)
        {
            var petals = ((Canvas)wheel.FindName("Sectors")).Children.OfType<WheelPetal>().ToArray();
            Assert.Equal(WheelGeometry.MaxTargetsPerPage, petals.Length);
            for (int index = 0; index < petals.Length; index++)
            {
                var selectedSurface = petals[index].Children.OfType<ShapePath>().Skip(1).First();
                Assert.Equal(index == selected ? 1d : 0d, selectedSurface.Opacity);
                Assert.Equal(Color.FromRgb(255, 85, 0), Assert.IsType<SolidColorBrush>(selectedSurface.Fill).Color);
                var label = petals[index].Children.OfType<TextBlock>().Single();
                Assert.Equal(index == selected ? Colors.White : Colors.Black, Assert.IsType<SolidColorBrush>(label.Foreground).Color);
            }
            Assert.Equal(expectedCaption, ((TextBlock)wheel.FindName("Caption")).Text);
        }

        void Save(string name)
        {
            GlassCompositorCapture.RequireOwned(owned, 0, Path.Combine(artifacts, name + ".preflight.json"), capturedSession!.Scene.PaddingPixels);
            wheel.UpdateLayout();
            var body = (BitmapSource)((Image)wheel.FindName("GlassBody")).Source;
            AssertTransparentContour(body, capturedSession.Scene);
            SaveBitmap(body, Path.Combine(artifacts, name + "-body.png"));
            var content = (FrameworkElement)wheel.Content;
            DpiScale dpi = VisualTreeHelper.GetDpi(content);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX),
                (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            bitmap.Render(content);
            SaveBitmap(bitmap, Path.Combine(artifacts, name + ".png"));
        }
        void Hover(RoutedEvent routed, Point point)
        {
            var surface = (Canvas)wheel.FindName("Surface");
            var data = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { samplePath });
            var args = (System.Windows.DragEventArgs)Activator.CreateInstance(typeof(System.Windows.DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [data, System.Windows.DragDropKeyStates.LeftMouseButton, System.Windows.DragDropEffects.Copy, surface, point], null)!;
            args.RoutedEvent = routed; surface.RaiseEvent(args);
        }
    }, TimeSpan.FromMinutes(5));

    private static void AssertTransparentContour(BitmapSource bitmap, GlassScene scene)
    {
        Assert.Equal(368, scene.WidthDip); Assert.Equal(454, scene.HeightDip);
        Assert.Equal(368, scene.LightingReferenceHeightDip);
        Assert.Equal(184, scene.LightingCenterXDip); Assert.Equal(184, scene.LightingCenterYDip);
        Assert.Equal(scene.PixelWidth, bitmap.PixelWidth); Assert.Equal(scene.PixelHeight, bitmap.PixelHeight);
        var navigation = Assert.Single(scene.Regions, region => region.Id == 32);
        var pixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        double scale = scene.Scale;
        double alphaSupportDip = Math.Max(1 / scale, scene.Material.EdgeFeatherDip) / 2 + .0001;
        int leaked = 0, nonPremultiplied = 0;
        for (int y = 0; y < bitmap.PixelHeight; y++)
        for (int x = 0; x < bitmap.PixelWidth; x++)
        {
            int p = (y * bitmap.PixelWidth + x) * 4;
            double dx = (x + .5) / scale - 184, dy = (y + .5) / scale - 184;
            // Zero exterior shadow leaves only the shader's half-width AA/feather
            // coverage outside the exact backing circle and navigation contour.
            bool nearShell = dx * dx + dy * dy <= Math.Pow(176 + alphaSupportDip, 2);
            bool nearNavigation = navigation.Sample((x + .5) / scale, (y + .5) / scale).Distance >= -alphaSupportDip;
            if (!nearShell && !nearNavigation && pixels[p + 3] != 0) leaked++;
            if (pixels[p] > pixels[p + 3] || pixels[p + 1] > pixels[p + 3] || pixels[p + 2] > pixels[p + 3]) nonPremultiplied++;
        }
        Assert.Equal(0, leaked); Assert.Equal(0, nonPremultiplied);
        int center = ((int)(184 * scale) * bitmap.PixelWidth + (int)(184 * scale)) * 4;
        Assert.Equal(255, pixels[center + 3]);
        int navigationCenter = ((int)(NavigationCenterY * scale) * bitmap.PixelWidth + (int)(184 * scale)) * 4;
        Assert.Equal(255, pixels[navigationCenter + 3]);
        Assert.Equal(32, scene.Field[navigationCenter + 3]);
        foreach (var point in FlatSamplePoints())
        {
            int fieldOffset = ((int)(point.Y * scale) * bitmap.PixelWidth + (int)(point.X * scale)) * 4;
            int region = (int)scene.Field[fieldOffset + 3];
            Assert.True(scene.Field[fieldOffset + 2] >= scene.ShoulderThicknessDip(region),
                $"Owned proof sample {point.X},{point.Y} must remain outside the refractive/highlight shoulder.");
        }
    }
    private static void SaveBitmap(BitmapSource bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
    private static byte[] SampleFlatInteriors(Image image, double scale)
    {
        if (image.Source is not BitmapSource bitmap) return new byte[42];
        var result = new byte[42];
        var value = new byte[4];
        int index = 0;
        foreach (var point in FlatSamplePoints())
        {
            bitmap.CopyPixels(new Int32Rect((int)(point.X * scale), (int)(point.Y * scale), 1, 1), value, 4, 0);
            result[index++] = value[0];
        }
        return result;
    }
    private static IEnumerable<(int X, int Y)> FlatSamplePoints()
    {
        for (int y = 174; y <= 194; y += 4)
        for (int x = 174; x <= 194; x += 4) yield return (x, y);
        for (int x = 174; x <= 194; x += 4) yield return (x, NavigationCenterY);
    }
    private static double Difference(byte[] first, byte[] second) => first.Zip(second, (a, b) => Math.Abs(a - b)).Average();
    private static bool EndpointCalibrationReady(byte[] black, byte[] white) =>
        black.All(value => value >= ExpectedBroadLow - MaximumShadowBias - ChannelTolerance && value <= ExpectedBroadLow + ChannelTolerance) &&
        white.All(value => value >= ExpectedBroadHigh - MaximumShadowBias - ChannelTolerance && value <= ExpectedBroadHigh + ChannelTolerance) &&
        black.Zip(white, (dark, light) => Math.Abs(light - dark - ExpectedBroadDelta)).All(error => error <= 2);
    private static double[] Normalize(byte[] values, byte[] black, byte[] white) => values.Select((value, index) =>
        (value - black[index]) / Math.Max(1d, white[index] - black[index])).ToArray();
    private static bool HasCheckerboard(byte[] values, byte[] black, byte[] white)
    {
        var normalized = Normalize(values, black, white);
        double tolerance = ChannelTolerance / (ExpectedBroadDelta - 2);
        return normalized.Min() >= CheckerSourceFraction - tolerance && normalized.Min() <= CheckerSourceFraction + tolerance &&
            normalized.Max() >= 1 - tolerance && normalized.Max() <= 1 + tolerance &&
            normalized.Max() - normalized.Min() >= MinimumCheckerRange;
    }
    private static bool InvertedContent(byte[] before, byte[] after, byte[] black, byte[] white) =>
        Difference(before, after) > MinimumCheckerChange && ComplementError(before, after, black, white) <= ChannelTolerance;
    private static double ComplementError(byte[] before, byte[] after, byte[] black, byte[] white) => before.Select((first, index) =>
        Math.Abs(first + after[index] - (2 * black[index] + (1 + CheckerSourceFraction) * (white[index] - black[index])))).Max();
    private static string Samples(Image image, double scale)
    {
        var values = SampleFlatInteriors(image, scale);
        return $"Current sample min={values.Min()}, max={values.Max()}";
    }
    private static async Task WaitUntil(Func<bool> condition, string description, Func<string>? diagnostic = null)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(90)) await Task.Delay(15);
        bool ready = condition();
        Assert.True(ready, ready || diagnostic is null ? description : description + ": " + diagnostic());
    }
    private sealed class GuardedSession(StudioGlassSession session, Action guard) : ILiveGlassSession
    {
        public GlassScene Scene => session.Scene;
        public GlassFrameState LastState;
        public long RenderCompleted;
        public bool Disposed;
        private int attempts;
        private double lastGuardMilliseconds;
        public string Diagnostic => $"Attempts={Volatile.Read(ref attempts)}; CompletedCalls={RenderMilliseconds.Count}; LastGuardMs={Volatile.Read(ref lastGuardMilliseconds):F2}; {session.Diagnostic}";
        public ConcurrentQueue<double> RenderMilliseconds { get; } = new();
        public bool TryRenderFrame(GlassFrameState state, byte[] destination)
        {
            Interlocked.Increment(ref attempts);
            var guardClock = System.Diagnostics.Stopwatch.StartNew();
            guard();
            Volatile.Write(ref lastGuardMilliseconds, guardClock.Elapsed.TotalMilliseconds);
            LastState = state;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            bool rendered = session.TryRenderFrame(state, destination);
            RenderMilliseconds.Enqueue(clock.Elapsed.TotalMilliseconds);
            RenderCompleted = System.Diagnostics.Stopwatch.GetTimestamp();
            return rendered;
        }
        public void Dispose() { session.Dispose(); Disposed = true; }
    }
    private sealed class OwnedEngine : IConversionEngine
    {
        public IReadOnlyList<OperationDescriptor> Operations { get; } = new[] { "PNG", "WEBP", "HEIC", "TIFF", "AVIF", "BMP", "PDF", "JPG", "GIF" }
            .Select(format => new OperationDescriptor("image." + format.ToLowerInvariant(), format, OperationMode.Convert, "." + format.ToLowerInvariant(), false, false))
            .Concat(new[] { "ZIP", "TAR" }.Select(format => new OperationDescriptor("archive." + format.ToLowerInvariant(), format,
                OperationMode.Convert, "." + format.ToLowerInvariant(), false, false))).ToArray();
        public bool CanExecute(OperationRequest request) => true;
        public Task<ConversionResult> ExecuteAsync(OperationRequest request, IProgress<ConversionProgress>? progress, CancellationToken token) => throw new NotSupportedException();
    }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(nint window, out uint affinity);
}
