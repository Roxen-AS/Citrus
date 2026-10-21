using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Citrus.Conversion;
using Citrus.Windows.Presentation;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class VisualReworkTests(DesktopFixture desktop)
{
    [Theory]
    [InlineData(ThemeChoice.Light)]
    [InlineData(ThemeChoice.Dark)]
    public Task Bright_selection_and_primary_action_keep_readable_ink(ThemeChoice choice) => desktop.RunAsync(() =>
    {
        using var theme = new ThemeManager(Application.Current);
        try
        {
            theme.ApplyPalette(choice, true, false);
            foreach (var pair in new[] { ("Accent", "AccentText"), ("SelectedPetal", "SelectedPetalText"),
                ("PanelBackground", "PrimaryText"), ("PanelBackground", "SecondaryText"), ("CapsuleBackground", "SecondaryText") })
            {
                var surface = ((SolidColorBrush)Application.Current.FindResource(pair.Item1)).Color;
                var ink = ((SolidColorBrush)Application.Current.FindResource(pair.Item2)).Color;
                Assert.True(Contrast(surface, ink) >= (pair.Item1 == "SelectedPetal" ? 3 : 4.5), $"{choice}: {pair} lacks readable contrast.");
            }
            foreach (var pair in new[] { ("PetalBackground", "PrimaryText"), ("WheelHubBackground", "SecondaryText") })
            {
                var surface = Assert.IsAssignableFrom<GradientBrush>(Application.Current.FindResource(pair.Item1));
                var ink = ((SolidColorBrush)Application.Current.FindResource(pair.Item2)).Color;
                Assert.Equal(1, surface.Opacity);
                Assert.All(surface.GradientStops, stop =>
                {
                    Assert.Equal(255, stop.Color.A);
                    Assert.True(Contrast(stop.Color, ink) >= 4.5, $"{choice}: {pair} lacks readable contrast at {stop.Offset}.");
                });
            }
            var petal = new WheelPetal(new ImageEngine().Operations.Single(operation => operation.Id == "image.convert.png"), 0, 9);
            petal.ApplyTextScale(2);
            var label = petal.Children.OfType<TextBlock>().Single();
            Assert.Equal("PNG", label.Text);
            Assert.Equal(Visibility.Visible, label.Visibility);
            Assert.True(label.FontSize > 20);
            petal.SetSelected(true, false);
            Assert.Same(Application.Current.FindResource("SelectedPetalText"), label.Foreground);
        }
        finally { theme.Apply(ThemeChoice.Light, true); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(ThemeChoice.Light, 1d)]
    [InlineData(ThemeChoice.Dark, 1d)]
    [InlineData(ThemeChoice.Light, 2d)]
    public Task Empty_workspace_has_a_reachable_drop_action_and_no_empty_history(ThemeChoice choice, double textScale) => desktop.RunAsync(async () =>
    {
        using var theme = new ThemeManager(Application.Current);
        using var window = new MainWindow((App)Application.Current) { Width = 860, Height = 640, ShowActivated = false };
        try
        {
            theme.Apply(choice, true);
            theme.ApplyTypography(textScale);
            window.Configure(new Citrus.Core.OperationCatalog([new ImageEngine()]), []);
            window.ApplyTextScale(textScale);
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var empty = (Border)window.FindName("EmptyState");
            var add = ((StackPanel)empty.Child).Children.OfType<System.Windows.Controls.Button>().Single(button => Equals(button.Content, "Add files"));
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("JobsPanel")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("Inspector")).Visibility);
            add.BringIntoView();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            VisualArtifacts.AssertInside(add, (FrameworkElement)window.Content);
            VisualArtifacts.Capture((FrameworkElement)window.Content, $"workspace-empty-{choice}-text{textScale * 100:0}", 1, choice,
                new { textScale, captureScope = "WPF-only, original application rendering" });
        }
        finally { window.Dispose(); window.Close(); theme.ApplyTypography(1); theme.Apply(ThemeChoice.Light, true); }
    });

    [Theory]
    [InlineData(ThemeChoice.Light)]
    [InlineData(ThemeChoice.Dark)]
    public Task Large_text_history_and_warnings_leave_action_pinned_and_content_reachable(ThemeChoice choice) => desktop.RunAsync(async () =>
    {
        using var trace = new DesktopStageTrace($"workspace-stress-{choice}-text200", Dispatcher.CurrentDispatcher);
        trace.Mark("setup.before");
        using var files = new NativeFiles();
        string path = System.IO.Path.Combine(files.Root, "citrus.png");
        CitrusPreviewFixture.Save(path);
        using var theme = new ThemeManager(Application.Current);
        using var window = new MainWindow((App)Application.Current) { Width = 860, Height = 640, ShowActivated = false };
        try
        {
            trace.Mark("theme.before");
            theme.Apply(choice, true); theme.ApplyTypography(2);
            trace.Mark("theme.after");
            var engine = new ImageEngine();
            string[] warnings = ["An optional video encoder is unavailable. Install it to enable more output formats.",
                "An optional document tool is unavailable. Existing compatible operations remain available.",
                "An optional archive tool is unavailable. Check the installed tools before trying again."];
            trace.Mark("configure.before");
            window.Configure(new Citrus.Core.OperationCatalog([engine]), warnings);
            trace.Mark("configure.after");
            trace.Mark("add-files.before"); window.AddFiles([path]); trace.Mark("add-files.after");
            trace.Mark("text-scale.before"); window.ApplyTextScale(2); trace.Mark("text-scale.after");
            trace.Mark("show.before"); window.Show(); trace.Mark("show.after");
            trace.Mark("failed-job.before");
            var operation = engine.Operations.Single(value => value.Id == "image.convert.png");
            var job = new Citrus.Core.JobQueue([engine]).EnqueueFailure(new(operation, [path], new(), null),
                new System.IO.IOException("The selected destination is unavailable. Choose another folder and retry this job."));
            trace.Mark("failed-job.after");
            trace.Mark("add-jobs.before"); window.AddJobs([job]); trace.Mark("add-jobs.after");
            bool retried = false;
            trace.Mark("submission-failure.before");
            window.AddSubmissionFailure("This submission could not start because the chosen destination is unavailable. Your selected files are unchanged. Retry when the destination is ready.",
                () => { retried = true; return Task.CompletedTask; });
            trace.Mark("submission-failure.after");
            trace.Mark("report.before");
            window.Report("Some work needs attention. Review the failed job and the available tools below.");
            trace.Mark("report.after");
            trace.Mark("initial-yield.before");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            trace.Mark("initial-yield.after");
            var root = (FrameworkElement)window.Content;
            var body = (ScrollViewer)window.FindName("WorkspaceScroll");
            var run = (System.Windows.Controls.Button)window.FindName("RunButton");
            Capture("initial");
            Assert.True(((Expander)window.FindName("JobsPanel")).IsExpanded);
            Assert.True(body.ViewportHeight >= 160, $"Useful body viewport lost: {body.ViewportHeight} DIP.");
            Assert.True(run.IsEnabled && run.IsVisible);
            VisualArtifacts.AssertInside(run, root);
            var pinned = run.TransformToAncestor(root).Transform(new System.Windows.Point());

            trace.Mark("operations-scroll.before");
            var operations = (System.Windows.Controls.ListBox)window.FindName("Operations");
            body.ScrollToVerticalOffset(body.VerticalOffset + operations.TransformToAncestor(body).Transform(new System.Windows.Point()).Y);
            operations.SelectedIndex = 1; operations.ScrollIntoView(operations.SelectedItem);
            trace.Mark("operations-yield.before");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            trace.Mark("operations-yield.after");
            var selected = (FrameworkElement)operations.ItemContainerGenerator.ContainerFromIndex(1);
            Capture("operations");
            VisualArtifacts.AssertInside(selected, root);
            Assert.True(run.IsEnabled);
            AssertPinned();

            trace.Mark("history-scroll.before");
            var retries = Descendants((FrameworkElement)window.FindName("SubmissionFailures")).OfType<System.Windows.Controls.Button>();
            var retry = Assert.Single(retries);
            body.ScrollToVerticalOffset(body.VerticalOffset + retry.TransformToAncestor(body).Transform(new System.Windows.Point()).Y);
            trace.Mark("history-yield.before");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            trace.Mark("history-yield.after");
            Capture("history");
            VisualArtifacts.AssertInside(retry, root);
            trace.Mark("retry.before");
            retry.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            trace.Mark("retry.after");
            Assert.True(retried);
            AssertPinned();

            var warningText = (TextBlock)window.FindName("EngineStatus");
            Assert.Equal(string.Join("\n", warnings), warningText.Text);
            trace.Mark("warnings-scroll.before");
            body.ScrollToEnd();
            trace.Mark("warnings-yield.before");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            trace.Mark("warnings-yield.after");
            Capture("warnings");
            var warningBottom = warningText.TransformToAncestor(body).Transform(new System.Windows.Point(0, warningText.ActualHeight));
            Assert.InRange(warningBottom.Y, 0, body.ActualHeight + 1);
            AssertPinned();

            void AssertPinned()
            {
                Assert.Equal(pinned, run.TransformToAncestor(root).Transform(new System.Windows.Point()));
                VisualArtifacts.AssertInside(run, root);
            }
            void Capture(string state)
            {
                trace.Mark($"capture.{state}.before");
                VisualArtifacts.Capture(root, $"workspace-stress-{choice}-text200-{state}", 1, choice,
                    new { textScale = 2, failedJobs = 1, failedSubmissions = 1, warnings = warnings.Length, state, bodyViewportHeight = body.ViewportHeight });
                trace.Mark($"capture.{state}.after");
            }
        }
        finally
        {
            trace.Mark("cleanup.before");
            window.Dispose(); window.Close(); theme.ApplyTypography(1); theme.Apply(ThemeChoice.Light, true);
            trace.Mark("cleanup.after");
        }
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    [Fact]
    public void Fresh_settings_use_light_without_replacing_explicit_saved_choices()
    {
        using var files = new NativeFiles();
        var store = new SettingsStore(files.Root);
        Assert.Equal(ThemeChoice.Light, store.Load().Theme);
        foreach (var choice in new[] { ThemeChoice.System, ThemeChoice.Dark })
        {
            store.Save(new CitrusSettings { Theme = choice });
            Assert.Equal(choice, store.Load().Theme);
        }
    }

    private static double Contrast(Color a, Color b)
    {
        static double Linear(byte value) => value / 255d <= .04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + .055) / 1.055, 2.4);
        static double Luminance(Color c) => .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
}
