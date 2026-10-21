using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Citrus.Windows.Presentation;
using Brushes = System.Windows.Media.Brushes;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class StudioChromeTests(DesktopFixture desktop)
{
    [Fact]
    public Task Moving_visible_chrome_keeps_its_live_material_and_capture_exclusion() => desktop.RunAsync(async () =>
    {
        var surface = new MaterialSurface { Child = new TextBlock { Text = "Moving optical chrome" } };
        var window = new System.Windows.Window { Content = surface, Width = 220, Height = 140, ShowInTaskbar = false };
        int excluded = 0, acquisitions = 0, created = 0;
        MovingSession? moving = null;
        using var chrome = new ChromeGlassController(window, surface, (scene, x, y) =>
        {
            Interlocked.Increment(ref created); return moving = new MovingSession(scene, x, y);
        }, _ =>
        {
            Interlocked.Increment(ref acquisitions); Interlocked.Increment(ref excluded);
            return new Lease(() => Interlocked.Decrement(ref excluded));
        });
        try
        {
            window.Show(); chrome.Apply(new(true, false, "Owned moving session"), false);
            await Until(() => surface.State == BackdropState.LiveRefractiveGlass);
            int initialAcquisitions = acquisitions;
            int beforeFrames = moving!.Frames;
            for (int i = 0; i < 12; i++)
            {
                window.Left += 2; window.Top += 1;
                Assert.Equal(BackdropState.LiveRefractiveGlass, surface.State);
                Assert.Equal(1, excluded);
                await Task.Delay(10); // Continuous movement never reaches the old 45-ms debounce.
                Assert.Equal(BackdropState.LiveRefractiveGlass, surface.State);
            }
            Assert.Equal(initialAcquisitions, acquisitions);
            var origin = surface.PointToScreen(default);
            await Until(() => moving.X == (int)Math.Round(origin.X) && moving.Y == (int)Math.Round(origin.Y));
            Assert.True(moving.Frames > beforeFrames);
            Assert.Equal(1, created);
            Assert.False(moving.Disposed);
        }
        finally { window.Close(); await chrome.Completion; }
    });

    [Fact]
    public Task Chrome_stops_hidden_and_minimized_sessions_then_reopens_without_stale_frames() => desktop.RunAsync(async () =>
    {
        var surface = new MaterialSurface { Child = new TextBlock { Text = "Owned chrome lifecycle" } };
        var window = new System.Windows.Window { Content = surface, Width = 240, Height = 160, ShowInTaskbar = false };
        var sessions = new List<FakeSession>(); int excluded = 0;
        using var chrome = new ChromeGlassController(window, surface, (scene, _, _) =>
        {
            var session = new FakeSession(scene); lock (sessions) sessions.Add(session); return session;
        }, _ => { Interlocked.Increment(ref excluded); return new Lease(() => Interlocked.Decrement(ref excluded)); });
        try
        {
            window.Show(); chrome.Apply(new(true, false, "OwnedFakeSession"), false);
            await Until(() => surface.State == BackdropState.LiveRefractiveGlass);
            window.Hide(); await chrome.Completion;
            Assert.Equal(BackdropState.Solid, surface.State); Assert.Equal(0, excluded);
            lock (sessions) Assert.All(sessions, session => Assert.True(session.Disposed));
            window.Show(); await Until(() => surface.State == BackdropState.LiveRefractiveGlass);
            window.WindowState = System.Windows.WindowState.Minimized; await chrome.Completion;
            Assert.Equal(BackdropState.Solid, surface.State); Assert.Equal(0, excluded);
            window.WindowState = System.Windows.WindowState.Normal;
            await Until(() => surface.State == BackdropState.LiveRefractiveGlass);
            chrome.Apply(new(false, false, "ReduceTransparency"), false); await chrome.Completion;
            Assert.Equal(BackdropState.Solid, surface.State); Assert.Equal(0, excluded);
            lock (sessions) Assert.All(sessions, session => Assert.True(session.Disposed));
        }
        finally { window.Close(); await chrome.Completion; }
    });

    [Fact]
    public void Delayed_B_build_cannot_make_a_replacement_A_request_reuse_the_wrong_scene()
    {
        var cache = new ChromeGlassController.SceneCache();
        Citrus.Core.WheelPoint[] a = [new(0,0), new(100,0), new(100,100), new(0,100)];
        Citrus.Core.WheelPoint[] b = [new(0,0), new(200,0), new(200,100), new(0,100)];
        cache.Create((100, 100, 1, new(0)), a)();
        var delayedB = cache.Create((200, 100, 1, new(0)), b);
        var replacementA = cache.Create((100, 100, 1, new(0)), a);
        Assert.Equal(200, delayedB().PixelWidth);
        Assert.Equal(100, replacementA().PixelWidth);
    }

    [Fact]
    public Task Transparent_scroll_content_and_icons_adapt_contrast_and_restore_their_base_brushes() => desktop.RunAsync(async () =>
    {
        var label = new TextBlock { Text = "Convert formats", Foreground = Brushes.Black };
        var icon = new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,0 L16,16"), Stroke = Brushes.Black, Width = 16, Height = 16 };
        var stack = new StackPanel(); stack.Children.Add(label); stack.Children.Add(icon);
        var surface = new MaterialSurface { Width = 160, Height = 100, Child = new ScrollViewer { Content = stack, Background = Brushes.Transparent } };
        var window = new System.Windows.Window { Content = surface, Width = 180, Height = 140, ShowInTaskbar = false };
        using var chrome = new ChromeGlassController(window, surface);
        window.Show(); window.UpdateLayout();
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        chrome.Apply(new(false, false, "OwnedContrastTest"), false);
        surface.Measure(new System.Windows.Size(160, 100)); surface.Arrange(new System.Windows.Rect(0, 0, 160, 100)); surface.UpdateLayout();
        var pixels = new byte[160 * 100 * 4]; for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        chrome.ProtectLabels(new GlassScene(160, 100, 1, [GlassRegion.RoundedRect(1, 0, 0, 160, 100, 0)]), pixels);
        await Until(() => ((SolidColorBrush)label.Foreground).Color == Colors.White && ((SolidColorBrush)icon.Stroke).Color == Colors.White);
        Assert.True(Colors.White == ((SolidColorBrush)label.Foreground).Color,
            $"Label size {label.RenderSize}; protected labels {((System.Collections.IDictionary)typeof(ChromeGlassController).GetField("labels", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(chrome)!).Count}; animated={System.Windows.DependencyPropertyHelper.GetValueSource(label, TextBlock.ForegroundProperty).IsAnimated}");
        Assert.Equal(Colors.White, ((SolidColorBrush)icon.Stroke).Color);
        for (int y = 0; y < 100; y++)
        for (int x = 0; x < 160; x++)
        { int p = (y * 160 + x) * 4; pixels[p] = pixels[p + 1] = pixels[p + 2] = (byte)(x % 2 == 0 ? 0 : 255); }
        chrome.ProtectLabels(new GlassScene(160, 100, 1, [GlassRegion.RoundedRect(1, 0, 0, 160, 100, 0)]), pixels);
        await Until(() => icon.Effect is System.Windows.Media.Effects.DropShadowEffect);
        Assert.NotNull(Assert.IsType<System.Windows.Media.Effects.DropShadowEffect>(icon.Effect));
        chrome.Dispose();
        Assert.Equal(Colors.Black, ((SolidColorBrush)label.Foreground).Color);
        Assert.Equal(Colors.Black, ((SolidColorBrush)icon.Stroke).Color);
        Assert.Null(icon.Effect);
        window.Close();
    });

    [Fact]
    public Task Optical_chrome_is_contour_clipped_and_readable_content_remains_opaque() => desktop.RunAsync(() =>
    {
        var content = new Border { Width = 50, Height = 40, Background = Brushes.White };
        var surface = new MaterialSurface { Width = 100, Height = 100, CornerRadius = new(24), Child = content };
        surface.Measure(new System.Windows.Size(100, 100)); surface.Arrange(new System.Windows.Rect(0, 0, 100, 100));
        var pixels = new byte[100 * 100 * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 255; pixels[i + 3] = 255; }
        surface.PresentGlass(BitmapSource.Create(100, 100, 96, 96, PixelFormats.Pbgra32, null, pixels, 400));
        surface.UpdateLayout();
        var image = new RenderTargetBitmap(100, 100, 96, 96, PixelFormats.Pbgra32); image.Render(surface);
        image.CopyPixels(pixels, 400, 0);
        Assert.Equal(0, pixels[3]); // Entire native corner stays empty.
        Assert.Equal(255, pixels[(50 * 100 + 50) * 4 + 2]); // Ordinary content stays white.
        Assert.Equal(0, pixels[(15 * 100 + 50) * 4 + 2]); // Clear optical region is still blue.
        surface.ClearGlass(); Assert.Equal(BackdropState.Solid, surface.State);
        return Task.CompletedTask;
    });

    private static async Task Until(Func<bool> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds < 5000) await Task.Delay(10);
        Assert.True(condition());
    }
    private sealed class Lease(Action release) : IDisposable { public void Dispose() => release(); }
    private sealed class MovingSession(GlassScene scene, int x, int y) : ILiveGlassSession, IRelocatableGlassSession
    {
        public GlassScene Scene => scene;
        public volatile int X = x, Y = y, Frames;
        public bool Disposed;
        public bool TryMoveTo(int screenLeft, int screenTop) { X = screenLeft; Y = screenTop; return true; }
        public bool TryRenderFrame(GlassFrameState state, byte[] destination)
        { Interlocked.Increment(ref Frames); Array.Fill(destination, (byte)255); return true; }
        public void Dispose() => Disposed = true;
    }
    private sealed class FakeSession(GlassScene scene) : ILiveGlassSession
    {
        public GlassScene Scene => scene;
        public bool Disposed;
        public bool TryRenderFrame(GlassFrameState state, byte[] destination)
        {
            Array.Fill(destination, (byte)255); return true;
        }
        public void Dispose() => Disposed = true;
    }
}
