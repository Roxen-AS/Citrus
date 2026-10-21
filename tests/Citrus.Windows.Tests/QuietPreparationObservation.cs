using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Citrus.Core;
using Button = System.Windows.Controls.Button;

namespace Citrus.Windows.Tests;

/// <summary>Exercise actual resident notifications across overlapping jobs and a real options dialog.</summary>
internal static class QuietPreparationObservation
{
    public static async Task<bool> Run(App app, string root)
    {
        void Invoke(string method, params object[] arguments) => typeof(App).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, arguments);
        var engine = new HeldEngine(Path.Combine(root, "earlier.txt"));
        var queue = new JobQueue([engine]);
        queue.JobChanged += job => Invoke("JobChanged", job);
        Invoke("BeginQuietSubmission", new OperationDrop(engine.Operations[0], [Path.Combine(root, "quiet.srt")]));
        var earlier = queue.Enqueue(new(engine.Operations[0], [Path.Combine(root, "quiet.srt")], new(), root));
        Invoke("TrackSubmitted", new[] { earlier }, SubmissionOrigin.Wheel);
        bool survived = false;
        Exception? failure = null;
        var watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        watch.Tick += async (_, _) =>
        {
            var dialog = app.Windows.OfType<ToolOptionsWindow>().FirstOrDefault(window => window.IsVisible);
            if (dialog is null) return;
            watch.Stop();
            try
            {
                engine.Release.TrySetResult();
                await earlier.Completion;
                // Longer than successful status expiry: the newer preparation must still own its toast.
                await Task.Delay(2300);
                survived = app.Windows.OfType<JobToastWindow>().Any();
                ((Button)dialog.FindName("CreateButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception error) { failure = error; dialog.Close(); }
        };
        watch.Start();
        try
        {
            var operation = new Citrus.Conversion.ArchiveEngine().Operations.Single(o => o.Id == "archive.zip");
            await app.SubmitAsync(new(operation, [Path.Combine(root, "quiet.srt")]), origin: SubmissionOrigin.Wheel);
            var jobs = ((ItemsControl)((MainWindow)app.MainWindow).FindName("JobList")).Items.Cast<JobViewModel>().Select(model => model.Job).ToArray();
            await Task.WhenAll(jobs.Select(job => job.Completion));
            Assert.Equal(2, jobs.Length);
            Assert.All(jobs, job => Assert.Equal(ConversionJobState.Succeeded, job.State));
        }
        finally { watch.Stop(); engine.Release.TrySetResult(); await earlier.Completion; }
        if (failure is not null) throw failure;
        return survived && app.Windows.OfType<JobToastWindow>().Any();
    }

    private sealed class HeldEngine(string output) : IConversionEngine
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<OperationDescriptor> Operations { get; } = [new("test.held", "Earlier conversion", OperationMode.Convert, ".txt", false, false)];
        public bool CanExecute(OperationRequest request) => true;
        public async Task<ConversionResult> ExecuteAsync(OperationRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken)
        {
            await Release.Task.WaitAsync(cancellationToken);
            await File.WriteAllTextAsync(output, "Earlier conversion complete", cancellationToken);
            return new([output]);
        }
    }
}
