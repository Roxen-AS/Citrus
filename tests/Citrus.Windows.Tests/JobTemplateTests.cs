using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Citrus.Core;
using Application = System.Windows.Application;
using ProgressBar = System.Windows.Controls.ProgressBar;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class JobTemplateTests(DesktopFixture desktop)
{
    [Fact]
    public Task Actual_history_template_displays_all_job_states_and_refreshes_read_only_progress() => desktop.RunAsync(async () =>
    {
        var engine = new GatedProgressEngine();
        var queue = new JobQueue([engine], maxConcurrency: 1);
        OperationRequest Request(string name) => new(engine.Operations[0], [name], new(), null);
        var running = queue.Enqueue(Request("running.png"));
        var submitted = new List<ConversionJob> { running };
        using var window = new MainWindow((App)Application.Current) { Width = 860, Height = 640, ShowActivated = false };
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var queued = queue.Enqueue(Request("queued.png")); submitted.Add(queued);
            var failed = queue.EnqueueFailure(Request("failed.png"), new InvalidOperationException("Expected failed-job fixture."));
            Assert.Equal(ConversionJobState.Running, running.State);
            Assert.Equal(ConversionJobState.Queued, queued.State);
            Assert.Equal(ConversionJobState.Failed, failed.State);
            window.Show(); window.AddJobs([queued, running, failed]);
            // Force the production DataTemplate to instantiate and bind now, not after the test returns.
            window.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var bars = Descendants((ItemsControl)window.FindName("JobList")).OfType<ProgressBar>()
                .ToDictionary(bar => ((JobViewModel)bar.DataContext).Job);
            Assert.Equal(3, bars.Count);
            foreach (var bar in bars.Values)
            {
                Assert.True(bar.ActualWidth > 0 && bar.ActualHeight > 0);
                Assert.Equal(BindingMode.OneWay, BindingOperations.GetBinding(bar, ProgressBar.ValueProperty)!.Mode);
            }
            Assert.False(typeof(JobViewModel).GetProperty(nameof(JobViewModel.Percent))!.CanWrite);
            Assert.Equal(0, bars[queued].Value); Assert.True(bars[queued].IsIndeterminate);
            Assert.Equal(25, bars[running].Value); Assert.False(bars[running].IsIndeterminate);
            Assert.Equal(0, bars[failed].Value); Assert.False(bars[failed].IsIndeterminate);

            engine.Report(.6); window.RefreshJobs(); window.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(60, bars[running].Value);
            Assert.Equal(.6, running.Progress!.Fraction);

            engine.Release.TrySetResult();
            await Task.WhenAll(submitted.Select(job => job.Completion)).WaitAsync(TimeSpan.FromSeconds(5));
            window.RefreshJobs(); window.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(ConversionJobState.Succeeded, running.State);
            Assert.Equal(ConversionJobState.Succeeded, queued.State);
            Assert.Equal(100, bars[running].Value); Assert.Equal(100, bars[queued].Value);
            Assert.False(bars[running].IsIndeterminate); Assert.False(bars[queued].IsIndeterminate);
            Assert.Equal(0, bars[failed].Value);
        }
        finally
        {
            engine.Release.TrySetResult();
            await Task.WhenAll(submitted.Select(job => job.Completion)).WaitAsync(TimeSpan.FromSeconds(5));
            window.Dispose(); window.Close();
        }
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class GatedProgressEngine : IConversionEngine
    {
        private IProgress<ConversionProgress>? progress;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<OperationDescriptor> Operations { get; } = [new("fixture.progress", "Progress fixture", OperationMode.Convert, ".png", false, false)];
        public bool CanExecute(OperationRequest request) => true;
        public void Report(double fraction) => progress!.Report(new(fraction, "Working"));
        public async Task<ConversionResult> ExecuteAsync(OperationRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken)
        {
            this.progress = progress;
            Report(.25); Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new([]);
        }
    }
}
