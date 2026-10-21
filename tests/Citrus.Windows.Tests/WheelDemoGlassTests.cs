using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Citrus.Core.Presentation;
using Citrus.Windows.Presentation;
using Image = System.Windows.Controls.Image;
using System.Reflection;
using System.Windows.Threading;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class WheelDemoGlassTests(DesktopFixture desktop)
{
    [Fact]
    public Task Duplicate_layout_requests_keep_the_initial_capture_running() => desktop.RunAsync(async () =>
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int created = 0;
        var demo = new WheelDemoWindow((scene, _, _) =>
        {
            created++;
            return new GatedSession(scene, entered, release);
        }, _ => new Lease());
        try
        {
            demo.ApplyMaterialPolicy(new(true, false, "Owned pending layout"), false);
            demo.Show();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var glass = Glass(demo);
            Task original = glass.Completion;
            QueuePlacement(demo);
            QueuePlacement(demo);
            Assert.True(glass.Running, "A layout notification must not cancel initial capture before its first frame.");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Same(original, glass.Completion);
            Assert.Equal(1, created);
            release.TrySetResult();
            await WaitUntil(() => Descendants(demo).OfType<Image>().Any(image => image.Source is not null));
        }
        finally
        {
            release.TrySetResult(); demo.Close();
            await demo.GlassCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        }
    });

    [Fact]
    public Task Queued_layout_keeps_live_blur_and_hiding_cancels_queued_work() => desktop.RunAsync(async () =>
    {
        int created = 0;
        var demo = new WheelDemoWindow((scene, x, y) => { created++; return new FakeSession(scene, x, y); }, _ => new Lease());
        try
        {
            demo.ApplyMaterialPolicy(new(true, false, "Owned live layout"), false);
            demo.Show();
            await WaitUntil(() => Descendants(demo).OfType<Image>().Any(image => image.Source is not null));
            var image = Descendants(demo).OfType<Image>().Single(value => value.Source is not null);
            var original = image.Source;
            int before = created;
            QueuePlacement(demo);
            QueuePlacement(demo);
            Assert.Same(original, image.Source);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Same(original, image.Source);
            Assert.Equal(before, created);
            demo.ApplyMaterialPolicy(new(true, true, "Owned equivalent policy refresh"), true);
            Assert.Same(original, image.Source);
            Assert.Equal(before, created);
            QueuePlacement(demo);
            demo.Hide();
            Assert.False(Glass(demo).Running);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await demo.GlassCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(Glass(demo).Running);
            Assert.Equal(before, created);
        }
        finally { demo.Close(); await demo.GlassCompletion.WaitAsync(TimeSpan.FromSeconds(5)); }
    });

    [Fact]
    public Task Moving_the_demo_keeps_its_wheel_glass_visible() => desktop.RunAsync(async () =>
    {
        FakeSession? latest = null;
        int acquisitions = 0;
        var trace = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var demo = new WheelDemoWindow((scene, x, y) =>
        {
            trace.Enqueue($"Create origin={x},{y} scale={scene.Scale:F6} pixels={scene.PixelWidth}x{scene.PixelHeight}");
            return latest = new FakeSession(scene, x, y);
        },
            _ => { acquisitions++; return new Lease(); });
        demo.SizeChanged += (_, e) => trace.Enqueue($"Size {e.PreviousSize} -> {e.NewSize}");
        demo.DpiChanged += (_, e) => trace.Enqueue($"DPI {e.OldDpi.DpiScaleX} -> {e.NewDpi.DpiScaleX}");
        demo.LocationChanged += (_, _) => trace.Enqueue($"Location {demo.Left:F3},{demo.Top:F3}");
        try
        {
            demo.ApplyMaterialPolicy(new(true, false, "Owned moving demo"), false); demo.Show();
            await WaitUntil(() => Descendants(demo).OfType<Image>().Any(image => image.Source is not null));
            var image = Descendants(demo).OfType<Image>().Single(value => value.Source is not null);
            var canvas = Descendants(demo).OfType<Canvas>().Single(value => value.Focusable);
            var viewer = Descendants(demo).OfType<ScrollViewer>().First(value => value.Content is StackPanel);
            viewer.ScrollChanged += (_, e) => trace.Enqueue($"Viewport {e.ViewportWidth:F3}x{e.ViewportHeight:F3} delta={e.ViewportWidthChange:F3},{e.ViewportHeightChange:F3}; offset={e.HorizontalChange:F3},{e.VerticalChange:F3}");
            int initialAcquisitions = acquisitions;
            trace.Enqueue($"Begin move after {initialAcquisitions} acquisitions");
            demo.Left += 12;
            Assert.NotNull(image.Source);
            var origin = canvas.PointToScreen(default);
            await WaitUntil(() => latest!.ScreenX == (int)Math.Round(origin.X) && latest.ScreenY == (int)Math.Round(origin.Y));
            Assert.True(initialAcquisitions == acquisitions, string.Join(Environment.NewLine, trace));
        }
        finally { demo.Close(); await demo.GlassCompletion.WaitAsync(TimeSpan.FromSeconds(5)); }
    });

    [Fact]
    public Task Demo_policy_keeps_opaque_fallback_without_starting_capture() => desktop.RunAsync(async () =>
    {
        int sessions = 0;
        var demo = new WheelDemoWindow((scene, _, _) => { sessions++; return new FakeSession(scene); }, _ => new Lease());
        try
        {
            demo.ApplyMaterialPolicy(new(false, false, "Owned opaque policy"), false);
            demo.Show();
            await Task.Delay(120);
            Assert.Equal(0, sessions);
            Assert.Equal(System.Windows.Media.Color.FromRgb(255, 85, 0), Assert.IsType<SolidColorBrush>(demo.Resources["SelectedPetal"]).Color);
        }
        finally { demo.Close(); await demo.GlassCompletion.WaitAsync(TimeSpan.FromSeconds(5)); }
    });

    [Fact]
    public Task Demo_retargets_viewport_translation_and_restarts_only_for_changed_raster_then_stops_when_hidden() => desktop.RunAsync(async () =>
    {
        var sessions = new List<FakeSession>();
        var leases = new List<Lease>();
        var demo = new WheelDemoWindow((scene, x, y) =>
        {
            var session = new FakeSession(scene, x, y);
            lock (sessions) sessions.Add(session);
            return session;
        }, _ => { var lease = new Lease(); leases.Add(lease); return lease; });
        try
        {
            demo.ApplyMaterialPolicy(new(true, false, "Owned native policy"), false);
            demo.Show();
            await WaitUntil(() => Descendants(demo).OfType<Image>().Any(image => image.Source is not null));
            Assert.Equal(System.Windows.Media.Color.FromRgb(255, 85, 0), ((SolidColorBrush)demo.Resources["SelectedPetal"]).Color);
            var canvas = Descendants(demo).OfType<Canvas>().Single(value => value.Width == Citrus.Core.WheelGeometry.Width && value.Focusable);
            canvas.RaiseEvent(new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(canvas)!, Environment.TickCount, Key.Right)
            { RoutedEvent = Keyboard.KeyDownEvent });
            var caption = Assert.IsType<TextBlock>(canvas.Children.OfType<Border>().Single(value => value.Child is TextBlock).Child);
            Assert.Equal("WEBP", caption.Text);
            lock (sessions) Assert.Equal(0, sessions[^1].LastAccent); // Selection is retained foreground, not a backing lens.
            FakeSession first;
            lock (sessions) first = sessions[0];
            Assert.Equal(368, first.Scene.WidthDip); Assert.Equal(368, first.Scene.HeightDip);
            int originalCount;
            lock (sessions) originalCount = sessions.Count;
            demo.Height = 400;
            demo.UpdateLayout();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            bool MatchesCurrentOrigin()
            {
                var actual = canvas.PointToScreen(new System.Windows.Point());
                lock (sessions) return sessions.Count > 0 && sessions[^1].ScreenX == (int)Math.Round(actual.X) &&
                    sessions[^1].ScreenY == (int)Math.Round(actual.Y);
            }
            await WaitUntil(MatchesCurrentOrigin);
            lock (sessions) Assert.Equal(originalCount, sessions.Count);
            int before;
            int beforeScrollY;
            lock (sessions) { before = sessions.Count; beforeScrollY = sessions[^1].ScreenY; }
            var viewer = Descendants(demo).OfType<ScrollViewer>().First(value => value.Content is StackPanel);
            Assert.True(viewer.ScrollableHeight > 0);
            viewer.ScrollToVerticalOffset(30);
            demo.UpdateLayout();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            await WaitUntil(() =>
            {
                lock (sessions) return sessions.Count == before && sessions[^1].ScreenY < beforeScrollY && MatchesCurrentOrigin();
            });
            lock (sessions)
            {
                var actual = canvas.PointToScreen(new System.Windows.Point());
                Assert.Equal((int)Math.Round(actual.X), sessions[^1].ScreenX);
                Assert.Equal((int)Math.Round(actual.Y), sessions[^1].ScreenY);
                double viewportScale = VisualTreeHelper.GetDpi(viewer).DpiScaleX;
                Assert.InRange(beforeScrollY - sessions[^1].ScreenY,
                    (int)Math.Floor(28 * viewportScale), (int)Math.Ceiling(32 * viewportScale));
            }
            var box = Assert.IsType<Viewbox>(canvas.Parent);
            box.Width *= .9;
            demo.UpdateLayout();
            QueuePlacement(demo);
            await WaitUntil(() => { lock (sessions) return sessions.Count == before + 1 && MatchesCurrentOrigin(); });
            lock (sessions) Assert.NotEqual(first.Scene.Scale, sessions[^1].Scene.Scale);
            demo.Hide();
            await demo.GlassCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            lock (sessions) Assert.All(sessions, session => Assert.True(session.Disposed));
            Assert.All(leases, lease => Assert.True(lease.Disposed));
            Assert.Equal(System.Windows.Media.Color.FromRgb(255, 85, 0), Assert.IsType<SolidColorBrush>(demo.Resources["SelectedPetal"]).Color);
        }
        finally { demo.Close(); await demo.GlassCompletion.WaitAsync(TimeSpan.FromSeconds(5)); }
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var next in Descendants(child)) yield return next;
        }
    }
    private static async Task WaitUntil(Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < 5000) await Task.Delay(10);
        Assert.True(condition());
    }
    private static LiveWheelGlass Glass(WheelDemoWindow demo) => (LiveWheelGlass)typeof(WheelDemoWindow)
        .GetField("glass", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(demo)!;
    private static void QueuePlacement(WheelDemoWindow demo) => typeof(WheelDemoWindow)
        .GetMethod("QueueGlassPlacement", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(demo, null);
    private sealed class Lease : IDisposable { public bool Disposed; public void Dispose() => Disposed = true; }
    private sealed class GatedSession(GlassScene scene, TaskCompletionSource entered, TaskCompletionSource release) : ILiveGlassSession, IRelocatableGlassSession
    {
        public GlassScene Scene => scene;
        public bool TryRenderFrame(GlassFrameState state, byte[] destination)
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
            Array.Fill(destination, (byte)255);
            return true;
        }
        public void Dispose() { }
        public bool TryMoveTo(int screenLeft, int screenTop) => true;
    }
    private sealed class FakeSession(GlassScene scene, int x = 0, int y = 0) : ILiveGlassSession, IRelocatableGlassSession
    {
        public GlassScene Scene { get; } = scene;
        private int screenX = x, screenY = y;
        public int ScreenX => Volatile.Read(ref screenX);
        public int ScreenY => Volatile.Read(ref screenY);
        public volatile int LastAccent;
        public volatile bool Disposed;
        public bool TryRenderFrame(GlassFrameState state, byte[] destination)
        {
            LastAccent = state.AccentRegion;
            Array.Fill(destination, (byte)255);
            return true;
        }
        public bool TryMoveTo(int left, int top)
        {
            Volatile.Write(ref screenX, left);
            Volatile.Write(ref screenY, top);
            return true;
        }
        public void Dispose() => Disposed = true;
    }
}
