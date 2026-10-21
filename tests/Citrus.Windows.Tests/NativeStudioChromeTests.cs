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
using Citrus.Core.Presentation;
using Citrus.Windows.Presentation;
using Xunit.Abstractions;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class NativeStudioChromeTests(DesktopFixture desktop, ITestOutputHelper output)
{
    [NativeLiquidGlassFact]
    public Task Settings_chrome_refracts_owned_desktop_and_releases_capture_when_hidden_or_minimized() => desktop.RunAsync(async () =>
    {
        string artifacts = Path.Combine(AppContext.BaseDirectory, "TestResults", "studio-chrome-native",
            DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));
        var fixturePoint = LowestDpiFixturePoint();
        var settings = new SettingsWindow(new CitrusSettings { ReduceMotion = true, SoundEnabled = false }, _ => Task.CompletedTask)
        { Height = 600 };
        var surface = (MaterialSurface)settings.FindName("Chrome");
        using var owned = new GlassOwnedScene(settings, fixturePoint);
        var dispatcher = Dispatcher.CurrentDispatcher;
        using var proofLifetime = new CancellationTokenSource();
        var phaseClock = System.Diagnostics.Stopwatch.StartNew();
        var diagnosticGate = new object();
        string lastPhase = "Created";
        int phaseCount = 0;
        Exception? primaryError = null;
        var sessions = new List<GuardedSession>();
        GuardedSession? active = null;
        ILiveGlassSession? Create(GlassScene scene, int x, int y)
        {
            Stage("FactoryGuard", $"{scene.PixelWidth}x{scene.PixelHeight}; padding={scene.PaddingPixels}");
            InvokeGuard(scene.PaddingPixels, "create");
            Stage("FactoryNativeCreate");
            var native = StudioGlassSession.TryCreate(scene, x, y);
            if (native is null) return null;
            Stage("FactoryNativeCreated", native.Diagnostic);
            active = new GuardedSession(native, () => InvokeGuard(scene.PaddingPixels, "frame"));
            sessions.Add(active);
            return active;
        }
        using var controller = new ChromeGlassController(settings, surface, Create);
        var live = (LiveWheelGlass)typeof(ChromeGlassController).GetField("glass", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
        int presentations = 0;
        var productPresented = live.FramePresented;
        live.FramePresented = (scene, pixels) => { productPresented?.Invoke(scene, pixels); presentations++; };
        var presentationWork = new List<double>();
        live.PresentationMeasured = (_, work) => presentationWork.Add(work);
        try
        {
            var fixtureWork = NativeMethods.WorkArea(fixturePoint);
            Stage("ShowFixture", $"Actual selected monitor scale={NativeMethods.MonitorScale(fixturePoint):F3}; point={fixturePoint.X},{fixturePoint.Y}; workarea={fixtureWork.Width}x{fixtureWork.Height}");
            owned.SetBroadPattern(false);
            owned.Show(activateOwnedBackground: true);
            settings.UpdateLayout();
            Stage("SettleFixtureDpi");
            int padding = await owned.CenterCaptureTargetWithPaddingAsync(
                scale => StudioGlassMath.CapturePaddingPixels(GlassMaterial.Citrus, scale));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Stage("AdmittedGuard", $"Surface={surface.ActualWidth:F2}x{surface.ActualHeight:F2}; scale={owned.DpiScale:F3}; padding={padding}");
            Guard(padding, "admitted");
            // Initial device work can create a driver overlay. Prime without reading any pixels,
            // then reassert only this test's own opaque background and target.
            var bounds = owned.Bounds;
            double width = surface.ActualWidth, height = surface.ActualHeight, scale = owned.DpiScale;
            var outline = surface.GlassOutline().ToArray();
            Stage("PrimeGeometryStart", $"{width:F2}x{height:F2}; scale={scale:F3}; outline={outline.Length}");
            var optical = await Task.Run(() => new GlassScene(width, height, scale, [GlassRegion.Polygon(1, outline)]));
            Stage("PrimeGeometryComplete");
            await Task.Run(() =>
            {
                Stage("PrimeCaptureStart");
                using var prime = StudioGlassSession.TryCreate(optical, (int)bounds.Left, (int)bounds.Top);
                Assert.NotNull(prime);
                Stage("PrimeCaptureCreated");
            });
            Stage("PrimeCaptureDisposed");
            Assert.True(NativeMethods.SetWindowPos(owned.BackgroundHandle, new nint(-1), 0, 0, 0, 0, 0x13));
            Assert.True(NativeMethods.SetWindowPos(owned.CaptureTargetHandle, new nint(-1), 0, 0, 0, 0, 0x13));
            Stage("ApplyLivePolicy");
            controller.Apply(new(true, false, "OwnedStudioChromeProof"), false);
            Stage("WaitFirstFrame");
            await WaitLive("first native Settings chrome frame");
            Stage("FirstFramePresented");
            Assert.True(GetWindowDisplayAffinity(owned.CaptureTargetHandle, out uint affinity)); Assert.Equal(0x11u, affinity);
            Assert.True(settings.AllowsTransparency);
            AssertRightClose(surface);

            byte[] first = SampleHeader(Frame(surface), owned.DpiScale);
            BitmapSource composedFirst = ComposeOwned("compose-a");
            AssertTransparentCorners(composedFirst);
            var scroll = ((DockPanel)surface.Child).Children.OfType<ScrollViewer>().Single();
            Assert.IsType<SolidColorBrush>(scroll.Background);
            Assert.Equal(255, ((SolidColorBrush)scroll.Background).Color.A);
            Point contentPoint = scroll.TransformToAncestor(surface).Transform(new Point(4, scroll.ActualHeight / 2));
            byte[] contentBefore = Pixel(composedFirst, contentPoint, owned.DpiScale);
            Save("owned-settings-broad-a", composedFirst);

            owned.SetBroadPattern(true);
            // White alpha.68 leaves an81.6/255 black-to-white source delta.
            // Retain at least75% despite blur and the title chrome's edge lighting.
            double minimumSourceChange = 255 * (1 - GlassMaterial.Citrus.TintAlpha) * .75;
            await WaitUntil(() => Difference(first, SampleHeader(Frame(surface), owned.DpiScale)) > minimumSourceChange,
                "changing owned background visible through Settings title chrome", Diagnostic);
            BitmapSource composedSecond = ComposeOwned("compose-b");
            AssertTransparentCorners(composedSecond);
            byte[] contentAfter = Pixel(composedSecond, contentPoint, owned.DpiScale);
            Assert.Equal(contentBefore, contentAfter);
            Save("owned-settings-broad-b", composedSecond);

            Stage("MoveVisibleSettings");
            var movingSession = active!;
            int beforeMovingSessions = sessions.Count, movementTransitions = 0;
            live.StageChanged = stage => { if (stage != GlassPresentationStage.Live) movementTransitions++; };
            var startBounds = owned.Bounds;
            int moveIndex = 0;
            foreach (int offset in new[] { 8, 16, -8, 0 })
            {
                int beforeMoveFrames = presentations;
                Assert.True(NativeMethods.SetWindowPos(owned.CaptureTargetHandle, new nint(-1),
                    (int)startBounds.Left + offset, (int)startBounds.Top, 0, 0, 0x11));
                Assert.Equal(BackdropState.LiveRefractiveGlass, surface.State);
                int expectedMoves = ++moveIndex;
                await WaitUntil(() => movingSession.Moves >= expectedMoves && presentations > beforeMoveFrames,
                    "live native chrome follows movement without resetting", Diagnostic);
                Assert.Same(movingSession, active);
                Assert.Equal(beforeMovingSessions, sessions.Count);
                Assert.False(movingSession.Disposed);
                Assert.True(GetWindowDisplayAffinity(owned.CaptureTargetHandle, out affinity)); Assert.Equal(0x11u, affinity);
            }
            Assert.Equal(0, movementTransitions);
            live.StageChanged = null;
            Save("owned-settings-after-move", ComposeOwned("compose-after-move"));
            Stage("MoveVisibleSettingsPassed", $"Relocations={movingSession.Moves}; material transitions={movementTransitions}");

            settings.Hide();
            Stage("HideSettings");
            await controller.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(BackdropState.Solid, surface.State);
            Assert.All(sessions, session => Assert.True(session.Disposed));
            Assert.True(GetWindowDisplayAffinity(owned.CaptureTargetHandle, out affinity)); Assert.Equal(0u, affinity);
            int beforeReopen = presentations;
            settings.Show();
            Stage("ShowSettings");
            // Hide/Show preserves the native placement. Let the real visibility
            // handler reopen capture without racing a second test-driven move.
            await WaitLive("Settings chrome reopens after Hide", beforeReopen);

            settings.WindowState = WindowState.Minimized;
            Stage("MinimizeSettings");
            await controller.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(BackdropState.Solid, surface.State);
            Assert.All(sessions, session => Assert.True(session.Disposed));
            Assert.True(GetWindowDisplayAffinity(owned.CaptureTargetHandle, out affinity)); Assert.Equal(0u, affinity);
            int beforeRestore = presentations;
            settings.WindowState = WindowState.Normal;
            Stage("RestoreSettings");
            await WaitLive("Settings chrome reopens after minimize", beforeRestore);
            Save("owned-settings-restored", ComposeOwned("compose-restored"));

            var render = sessions.SelectMany(session => session.RenderMilliseconds).ToArray();
            Directory.CreateDirectory(artifacts);
            File.WriteAllText(Path.Combine(artifacts, "studio-chrome-native-metadata.json"), JsonSerializer.Serialize(new
            {
                Window = nameof(SettingsWindow), Renderer = nameof(StudioGlassSession), RawSourceInjected = false,
                FixtureMonitorScale = NativeMethods.MonitorScale(fixturePoint), WindowScale = owned.DpiScale,
                PaddedOwnershipGuardBeforeEveryRender = true, PaddingPixels = padding,
                ChangingOwnedDesktopConfirmed = true, ExactComposedCornersTransparent = true,
                OpaqueFormContentStableAcrossBackgroundChange = true, CloseOnRight = true,
                HideReleasesAndShowReopens = true, MinimizeReleasesAndRestoreReopens = true,
                ProgrammaticNativeMoves = 4, MoveMaterialResets = movementTransitions,
                SameSessionAndExclusionRetainedDuringMovement = true,
                PresentedFrames = presentations, SessionsCreated = sessions.Count,
                WarmRenderAndReadbackMeanMilliseconds = render.Length <= 2 ? 0 : render.Skip(2).Average(),
                WarmUiPresentationMeanMilliseconds = presentationWork.Count <= 2 ? 0 : presentationWork.Skip(2).Average(),
                TimingLimitation = "Ownership guards and synchronous artifact saves disturb cadence. This is native correctness evidence, not a compositor-frame-rate measurement.",
                VisualAcceptance = "Technical and owned-scene native evidence; human material fidelity acceptance remains separate."
            }, new JsonSerializerOptions { WriteIndented = true }));
            // No more desktop reads are needed. Cancel any queued exhaustive
            // ownership probe before waiting for the render worker to drain.
            proofLifetime.Cancel();
            controller.Apply(new(false, false, "ProofFinished"), false);
            await controller.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.All(sessions, session => Assert.True(session.Disposed));
            Assert.True(GetWindowDisplayAffinity(owned.CaptureTargetHandle, out affinity)); Assert.Equal(0u, affinity);
            output.WriteLine($"Native Settings chrome changed with owned desktop; four movements retained live material/session/exclusion; opaque form content, transparent corners, right close, Hide/Show and Minimize/Restore passed. Evidence {artifacts}. No physical UI input was injected.");
        }
        catch (Exception error)
        {
            primaryError = error;
            Stage("PrimaryFailure", error + Environment.NewLine + Diagnostic(), force: true);
            throw;
        }
        finally
        {
            proofLifetime.Cancel();
            live.FramePresented = productPresented; live.PresentationMeasured = null; live.StageChanged = null;
            try
            {
                Stage("DisposeController", Diagnostic(), force: true);
                controller.Dispose();
                await controller.Completion.WaitAsync(TimeSpan.FromSeconds(15));
                Stage("ControllerDisposed", force: true);
            }
            catch (Exception error)
            {
                Stage("CleanupFailure", error + Environment.NewLine + Diagnostic(), force: true);
                if (primaryError is null) throw;
            }
        }

        void Stage(string phase, string? detail = null, bool force = false)
        {
            Volatile.Write(ref lastPhase, phase);
            lock (diagnosticGate)
            {
                if (++phaseCount > 128 && !force) return;
                Directory.CreateDirectory(artifacts);
                string record = JsonSerializer.Serialize(new { Phase = phase, ElapsedMilliseconds = phaseClock.Elapsed.TotalMilliseconds,
                    Thread = Environment.CurrentManagedThreadId, Detail = detail });
                File.AppendAllText(Path.Combine(artifacts, "phases.jsonl"), record + Environment.NewLine);
                output.WriteLine("STUDIO_CHROME_PHASE " + record);
            }
        }
        void InvokeGuard(int padding, string phase)
        {
            proofLifetime.Token.ThrowIfCancellationRequested();
            dispatcher.InvokeAsync(() =>
            {
                proofLifetime.Token.ThrowIfCancellationRequested();
                Guard(padding, phase);
            }, DispatcherPriority.Background, proofLifetime.Token).Task.GetAwaiter().GetResult();
            proofLifetime.Token.ThrowIfCancellationRequested();
        }

        void Guard(int padding, string phase)
        {
            if (!settings.IsVisible || settings.WindowState == WindowState.Minimized)
                throw new OperationCanceledException("Owned Settings became hidden before a capture read.");
            GlassCompositorCapture.RequireOwned(owned, 0, Path.Combine(artifacts, phase + ".preflight.json"), padding);
        }
        string Diagnostic() => $"Phase: {Volatile.Read(ref lastPhase)}; Controller: {live.LastFailure}; Session: {active?.Diagnostic}; Creation: {StudioGlassSession.LastCreationDiagnostic}";
        Task WaitLive(string description, int minimumFrames = 0) => WaitUntil(
            () => surface.State == BackdropState.LiveRefractiveGlass && presentations > minimumFrames, description, Diagnostic);
        BitmapSource ComposeOwned(string phase)
        {
            Guard(active!.Scene.PaddingPixels, phase);
            return Compose(surface, owned.DpiScale);
        }
        void Save(string name, BitmapSource bitmap)
        {
            // ComposeOwned already guarded acquisition. Encoding this immutable owned-window
            // bitmap neither samples the desktop nor needs a second full-pixel preflight.
            Directory.CreateDirectory(artifacts);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(artifacts, name + ".png")); encoder.Save(file);
        }
    }, TimeSpan.FromMinutes(5));

    private static BitmapSource Frame(MaterialSurface surface) =>
        (BitmapSource)typeof(MaterialSurface).GetField("opticalFrame", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(surface)!;
    private static BitmapSource Compose(MaterialSurface surface, double scale)
    {
        surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth * scale),
            (int)Math.Ceiling(surface.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(surface); bitmap.Freeze(); return bitmap;
    }

    private static NativeMethods.Point LowestDpiFixturePoint()
    {
        var candidates = new List<(NativeMethods.Point Point, uint Dpi, double Area)>();
        foreach (var monitor in DesktopGlassCapturePool.Monitors())
        {
            var point = new NativeMethods.Point { X = monitor.Left + monitor.Width / 2, Y = monitor.Top + monitor.Height / 2 };
            if (NativeMethods.GetDpiForMonitor(monitor.Handle, 0, out uint dpi, out _) < 0 || dpi == 0) continue;
            var work = NativeMethods.WorkArea(point);
            candidates.Add((point, dpi, work.Width * work.Height));
        }
        Assert.NotEmpty(candidates);
        return candidates.OrderBy(candidate => candidate.Dpi).ThenByDescending(candidate => candidate.Area).First().Point;
    }
    private static byte[] Pixel(BitmapSource bitmap, Point point, double scale)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect((int)(point.X * scale), (int)(point.Y * scale), 1, 1), pixel, 4, 0);
        return pixel;
    }
    private static byte[] SampleHeader(BitmapSource bitmap, double scale)
    {
        var result = new byte[9]; var pixel = new byte[4];
        for (int i = 0; i < result.Length; i++)
        {
            int x = (int)Math.Round(bitmap.PixelWidth * (.2 + i * .075));
            bitmap.CopyPixels(new Int32Rect(x, (int)(28 * scale), 1, 1), pixel, 4, 0);
            result[i] = pixel[0];
        }
        return result;
    }
    private static double Difference(byte[] a, byte[] b) => a.Zip(b, (left, right) => Math.Abs(left - right)).Average();
    private static void AssertTransparentCorners(BitmapSource bitmap)
    {
        var pixel = new byte[4];
        foreach (var point in new[] { new Int32Rect(0, 0, 1, 1), new Int32Rect(bitmap.PixelWidth - 1, 0, 1, 1),
            new Int32Rect(0, bitmap.PixelHeight - 1, 1, 1), new Int32Rect(bitmap.PixelWidth - 1, bitmap.PixelHeight - 1, 1, 1) })
        { bitmap.CopyPixels(point, pixel, 4, 0); Assert.Equal(0, pixel[3]); }
    }
    private static void AssertRightClose(MaterialSurface surface)
    {
        var header = ((DockPanel)surface.Child).Children.OfType<ChromeHeader>().Single(); header.ApplyTemplate();
        var close = Assert.IsType<Button>(header.Template.FindName("PART_Close", header));
        Point center = close.PointToScreen(new Point(close.ActualWidth / 2, close.ActualHeight / 2));
        Point midpoint = header.PointToScreen(new Point(header.ActualWidth / 2, header.ActualHeight / 2));
        Assert.True(center.X > midpoint.X);
    }
    private static async Task WaitUntil(Func<bool> condition, string description, Func<string> diagnostic)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        // The exhaustive physical-pixel ownership guard runs on the dispatcher before
        // each read. Its time belongs to this proof, not the production frame budget.
        while (!condition() && watch.ElapsedMilliseconds < 90000) await Task.Delay(15);
        Assert.True(condition(), description + ": " + diagnostic());
    }
    private sealed class GuardedSession(StudioGlassSession session, Action guard) : ILiveGlassSession, IRelocatableGlassSession
    {
        public GlassScene Scene => session.Scene;
        public bool Disposed;
        public volatile int Moves;
        public string Diagnostic => session.Diagnostic;
        public ConcurrentQueue<double> RenderMilliseconds { get; } = new();
        public bool TryMoveTo(int screenLeft, int screenTop)
        {
            guard();
            bool moved = session.TryMoveTo(screenLeft, screenTop);
            if (moved) Interlocked.Increment(ref Moves);
            return moved;
        }
        public bool TryRenderFrame(GlassFrameState state, byte[] destination)
        {
            guard();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            bool rendered = session.TryRenderFrame(state, destination);
            RenderMilliseconds.Enqueue(clock.Elapsed.TotalMilliseconds);
            return rendered;
        }
        public void Dispose() { session.Dispose(); Disposed = true; }
    }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(nint window, out uint affinity);
}
