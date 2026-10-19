using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Citrus.Conversion;
using Citrus.Core;
using Application = System.Windows.Application;
using DataObject = System.Windows.DataObject;
using DataFormats = System.Windows.DataFormats;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using Point = System.Windows.Point;
using Image = System.Windows.Controls.Image;
using Brushes = System.Windows.Media.Brushes;
using ShapePath = System.Windows.Shapes.Path;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class WheelRenderingTests(DesktopFixture desktop)
{
    [Theory]
    [InlineData(ThemeChoice.Light, 1d)] [InlineData(ThemeChoice.Light, 1.5d)] [InlineData(ThemeChoice.Light, 2d)]
    [InlineData(ThemeChoice.Dark, 1d)] [InlineData(ThemeChoice.Dark, 1.5d)] [InlineData(ThemeChoice.Dark, 2d)]
    public Task Real_wheel_states_render_with_fixed_bounds_and_clean_reuse(ThemeChoice theme, double scale) => desktop.RunAsync(async () =>
    {
        using var themes = new ThemeManager(Application.Current);
        themes.Apply(theme, reduceMotion: true);
        using var fixture = new RenderFixture();
        string prefix = $"wheel-{theme}-{scale * 100:0}pct-raster";
        try
        {
            await fixture.Enter([fixture.Root]); // Real admitted directory, empty Convert catalog.
            fixture.Capture(prefix + "-empty", scale, theme, expectPreview: false);
            Assert.Empty(fixture.Petals);
            Assert.Null(fixture.Preview.Source);

            fixture.Wheel.Dismiss();
            await fixture.Enter([fixture.Sample]);
            await fixture.WaitFor(() => fixture.Preview.Source is not null, "real bounded image preview");
            Assert.Equal(9, fixture.Petals.Length);
            fixture.Capture(prefix + "-page1", scale, theme);
            var original = fixture.Petals;
            var geometry = original.Select(petal => ((ShapePath)petal.Children[0]).Data).ToArray();
            var bounds = original.Select(VisualTreeHelper.GetDescendantBounds).ToArray();
            fixture.Hover(new Point(184, 184));
            Assert.Equal(DragDropEffects.Copy, fixture.Hover(new Point(184, 62)).Effects);
            fixture.Capture(prefix + "-hover", scale, theme);
            Assert.Equal("PNG", fixture.Text("Caption"));
            for (int i = 0; i < original.Length; i++)
            {
                Assert.Same(original[i], fixture.Petals[i]);
                Assert.Same(geometry[i], ((ShapePath)fixture.Petals[i].Children[0]).Data);
                Assert.Equal(bounds[i], VisualTreeHelper.GetDescendantBounds(fixture.Petals[i]));
            }
            Assert.True(original[0].ContainsPoint(new Point(184, 62)));
            Assert.DoesNotContain(original, petal => petal.ContainsPoint(new Point(184, 184)));
            Assert.DoesNotContain(original, petal => petal.ContainsPoint(new Point(184, 17)));

            // Use the actual hover-navigation timer, rather than injecting paging state.
            fixture.Hover(new Point(330, 390));
            await fixture.WaitFor(() => fixture.Text("Hint").StartsWith("2 /"), "400 ms page hover");
            fixture.Capture(prefix + "-page2", scale, theme);
            Assert.Equal(2, fixture.Petals.Length); // GZIP and PDF after nine real eligible formats.
            Assert.DoesNotContain(fixture.Petals, petal => original.Contains(petal));
            fixture.Hover(new Point(184, 184));
            fixture.Hover(new Point(184, 62));
            Assert.Equal("GZIP", fixture.Text("Caption"));

            fixture.Wheel.Dismiss();
            fixture.Wheel.ShowAtPointer(OperationMode.Convert);
            Assert.Equal(0, ((FrameworkElement)fixture.Wheel.FindName("Presentation")).Opacity);
            Assert.Empty(fixture.Petals);
            Assert.Null(fixture.Preview.Source);
            Assert.Equal("Center to cancel", fixture.Text("Caption"));
            fixture.Wheel.Dismiss();
            await fixture.Enter([fixture.Sample, fixture.Second]);
            await fixture.WaitFor(() => fixture.Preview.Source is not null, "batch preview");
            fixture.Capture(prefix + "-batch", scale, theme);
            Assert.Equal("2", fixture.Text("CountLabel"));
            Assert.Equal(Visibility.Visible, ((FrameworkElement)fixture.Wheel.FindName("CountBadge")).Visibility);
        }
        finally { themes.Apply(ThemeChoice.Light, reduceMotion: true); }
    });

    [Theory]
    [InlineData(1d)] [InlineData(1.25d)] [InlineData(1.5d)]
    [InlineData(1.75d)] [InlineData(2d)] [InlineData(2.25d)]
    public Task Actual_text_policy_keeps_hub_metadata_on_opaque_backing(double textScale) => desktop.RunAsync(async () =>
    {
        using var themes = new ThemeManager(Application.Current);
        try
        {
            foreach (var theme in new[] { ThemeChoice.Light, ThemeChoice.Dark })
            {
                themes.Apply(theme, true);
                using var fixture = new RenderFixture();
                string path = Path.Combine(fixture.Root, "citrus-with-a-long-descriptive-filename-for-preview.png");
                File.Copy(fixture.Sample, path);
                foreach (var mode in new[] { OperationMode.Convert, OperationMode.Actions })
                {
                    await fixture.Enter([path], mode);
                    await fixture.WaitFor(() => fixture.Preview.Source is not null, "real hub thumbnail");
                    fixture.Wheel.ApplyMaterialPolicy(new(false, false, "TextScaleFixture"), theme == ThemeChoice.Dark, textScale);
                    await fixture.WaitFor(() => !fixture.Wheel.MaterialPresentationPending, "material presentation after text policy");
                    fixture.Wheel.UpdateLayout();
                    VisualArtifacts.Capture(fixture.Surface, $"wheel-hub-{theme}-{mode}-text{textScale * 100:0}-raster100", 1, theme,
                        new { textScale, mode = mode.ToString(), actualPolicyApplied = true, rasterScale = 1 });
                    Assert.Equal(1, ((FrameworkElement)fixture.Wheel.FindName("Presentation")).Opacity);
                    Assert.True(fixture.Preview.IsVisible);
                    var visible = new List<Rect> { Bounds(fixture.Preview) };
                    foreach (string name in new[] { "Badge", "ModeLabel" })
                    {
                        var label = (TextBlock)fixture.Wheel.FindName(name);
                        if (!label.IsVisible) continue;
                        var bounds = Bounds(label);
                        foreach (var corner in new[] { bounds.TopLeft, bounds.TopRight, bounds.BottomLeft, bounds.BottomRight })
                            Assert.True(Math.Pow(corner.X - 184, 2) + Math.Pow(corner.Y - 184, 2) <= 78 * 78,
                                $"{name} escapes the neutral hub at text scale {textScale}: {bounds}");
                        Assert.All(visible, other => Assert.False(bounds.IntersectsWith(other), $"{name} overlaps adjacent hub content."));
                        visible.Add(bounds);
                    }
                    Assert.Contains(Path.GetFileName(path), System.Windows.Automation.AutomationProperties.GetName(fixture.Wheel));
                    Assert.Equal(368, fixture.Surface.ActualWidth); Assert.Equal(414, fixture.Surface.ActualHeight);
                    fixture.Wheel.Dismiss();
                    Rect Bounds(FrameworkElement element) => element.TransformToAncestor(fixture.Surface).TransformBounds(new Rect(new Point(), element.RenderSize));
                }
            }
        }
        finally { themes.Apply(ThemeChoice.Light, true); }
    });

    [Fact]
    public Task Warm_repeated_routed_hover_records_time_allocation_and_retained_identity() => desktop.RunAsync(async () =>
    {
        using var fixture = new RenderFixture();
        await fixture.Enter([fixture.Sample]);
        await fixture.WaitFor(() => fixture.Preview.Source is not null, "preview before measurement");
        fixture.Hover(new Point(184,184));
        var first = fixture.Args(DragDrop.PreviewDragOverEvent, new Point(184,62));
        var secondPosition = WheelGeometry.At(122, 360d / fixture.Petals.Length);
        var second = fixture.Args(DragDrop.PreviewDragOverEvent, new Point(secondPosition.X,secondPosition.Y));
        var neutral = fixture.Args(DragDrop.PreviewDragOverEvent, new Point(184,184));
        var cycle = new[] { first, second, neutral };
        var originals = fixture.Petals;
        var geometry = originals.Select(petal => ((ShapePath)petal.Children[0]).Data).ToArray();
        const int warmup = 600, iterations = 6000, sampleCount = 5;
        for (int i = 0; i < warmup; i++) fixture.Raise(cycle[i % cycle.Length]);
        var samples = new List<object>();
        for (int sample = 0; sample < sampleCount; sample++)
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            long allocated = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) fixture.Raise(cycle[i % cycle.Length]);
            long elapsed = Stopwatch.GetTimestamp() - started;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            samples.Add(new { sample, iterations, elapsedMilliseconds = elapsed * 1000d / Stopwatch.Frequency,
                allocatedBytesOnDispatcherThread = bytes, bytesPerHover = bytes / (double)iterations });
        }
        VisualArtifacts.Json("wheel-hover-performance", new
        {
            capturedUtc = DateTimeOffset.UtcNow, os = Environment.OSVersion.ToString(), runtime = Environment.Version.ToString(),
            warmup, sampleCount, targets = originals.Length, stopwatchFrequency = Stopwatch.Frequency, samples,
            highContrast = SystemParameters.HighContrast, clientAreaAnimation = SystemParameters.ClientAreaAnimation,
            motionEnabled = false, soundEnabled = false,
            scope = "Synchronous real WPF routed DragOver + hit testing + selection. Preallocated synthetic args; alternating two targets and neutral center.",
            exclusions = "No Explorer/COM initiation, event allocation, thumbnail decode, animation, rasterization, compositor, presentation latency or other-thread allocations. No FPS guarantee or timing threshold."
        });
        for (int i = 0; i < originals.Length; i++)
        {
            Assert.Same(originals[i],fixture.Petals[i]);
            Assert.Same(geometry[i],((ShapePath)fixture.Petals[i].Children[0]).Data);
        }
        Assert.Equal(DragDropEffects.Copy, first.Effects);
        Assert.Equal(DragDropEffects.Copy, second.Effects);
        Assert.Equal(DragDropEffects.None, neutral.Effects);
        Assert.Equal("Center to cancel", fixture.Text("Caption"));
    });

    private sealed class RenderFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "citrus-render-" + Guid.NewGuid().ToString("N"));
        public string Sample { get; }
        public string Second { get; }
        public WheelWindow Wheel { get; }
        public Canvas Surface => (Canvas)Wheel.FindName("Surface");
        public WheelPetal[] Petals => ((Canvas)Wheel.FindName("Sectors")).Children.Cast<WheelPetal>().ToArray();
        public Image Preview => (Image)Wheel.FindName("Preview");
        private DataObject data = new();
        public RenderFixture()
        {
            Directory.CreateDirectory(Root);
            Sample = Path.Combine(Root, "citrus-sample.png"); Second = Path.Combine(Root, "second.png");
            CitrusPreviewFixture.Save(Sample);
            File.Copy(Sample, Second);
            Wheel = new WheelWindow(new OperationCatalog([new ImageEngine(), new ArchiveEngine(), new PdfEngine(), new NativeActionCatalog()]));
            Wheel.ApplySettings(new CitrusSettings { ReduceMotion = true, SoundEnabled = false });
        }
        public string Text(string name) => ((TextBlock)Wheel.FindName(name)).Text;
        public async Task Enter(string[] paths, OperationMode mode = OperationMode.Convert)
        {
            data = new DataObject(DataFormats.FileDrop, paths);
            Wheel.ShowAtPointer(mode);
            Raise(Args(DragDrop.PreviewDragEnterEvent, new Point(184,184)));
            await WaitFor(() => !Text("Hint").StartsWith("Preparing"), "production catalog preparation");
            Assert.True(Wheel.IsVisible);
            Assert.Equal(1, ((FrameworkElement)Wheel.FindName("Presentation")).Opacity);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
        public async Task WaitFor(Func<bool> ready, string description)
        {
            var limit = Stopwatch.StartNew();
            while (!ready() && limit.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
            Assert.True(ready(), "Timed out waiting for " + description);
        }
        public DragEventArgs Args(RoutedEvent routedEvent, Point point)
        {
            // WPF has no public constructor; this is callback evidence, not real OLE initiation.
            var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Copy, Surface, point], null)!;
            args.RoutedEvent = routedEvent; return args;
        }
        public void Raise(DragEventArgs args) { args.Handled = false; args.Effects = DragDropEffects.None; Surface.RaiseEvent(args); }
        public DragEventArgs Hover(Point point) { var args = Args(DragDrop.PreviewDragOverEvent,point); Raise(args); return args; }
        public void Capture(string name, double scale, ThemeChoice theme, bool expectPreview = true)
        {
            VisualArtifacts.Capture(Surface,name,scale,theme); // Save before bounds assertions, for failure inspection.
            Assert.Equal(368, Surface.ActualWidth); Assert.Equal(414, Surface.ActualHeight);
            var glyph = (FrameworkElement)Wheel.FindName("FileGlyph");
            if (expectPreview)
            {
                Assert.NotNull(Preview.Source);
                Assert.True(Preview.IsVisible);
                Assert.Equal(Visibility.Hidden, glyph.Visibility);
                VisualArtifacts.AssertInside(Preview,Surface);
            }
            else
            {
                // A source-less WPF Image may have zero RenderSize; the visible file glyph owns this state.
                Assert.Null(Preview.Source);
                Assert.Equal(Visibility.Visible, glyph.Visibility);
                Assert.True(glyph.IsVisible);
                VisualArtifacts.AssertInside(glyph,Surface);
            }
            foreach (string child in new[] { "Caption", "Hint", "ModeLabel", "Badge" })
                VisualArtifacts.AssertInside((FrameworkElement)Wheel.FindName(child),Surface);
            foreach (var petal in Petals)
            {
                VisualArtifacts.AssertInside(petal,Surface);
                foreach (FrameworkElement child in petal.Children) VisualArtifacts.AssertInside(child,Surface);
            }
        }
        public void Dispose() { Wheel.Dismiss(); Wheel.Close(); Directory.Delete(Root,true); }
    }
}
