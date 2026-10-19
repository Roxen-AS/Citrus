using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Citrus.Core.Editing;

namespace Citrus.Windows.Editors.Media;

/// <summary>Reusable view; host owns controller disposal. No routes, queue or conversion submission.</summary>
public partial class MediaTransport : UserControl
{
    private readonly DispatcherTimer timer;
    private MediaPreviewController? controller;
    private CancellationTokenSource lifetime = new();
    private EditorDocument? document;
    private TimeRange range;
    private bool attached;
    public MediaTransport()
    {
        InitializeComponent();
        timer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }
    public MediaPreviewController? Controller
    {
        get => controller;
        set { Detach(); controller = value; if (IsLoaded) Attach(); }
    }
    public async Task LoadAsync(EditorDocument value, TimeRange selection, CancellationToken ct)
    {
        document = value; range = selection;
        Timeline.Minimum = Seconds(selection.Start); Timeline.Maximum = Seconds(selection.End);
        Timeline.Value = Timeline.Minimum; Preview.Source = null;
        if (controller is null) throw new InvalidOperationException("Assign the playback controller before loading media.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        await controller.PreparePausedAsync(value, selection, Original.IsChecked == true, linked.Token);
        Refresh();
    }
    private void Attach()
    {
        if (attached || controller is null) return;
        attached = true;
        lifetime.Dispose(); lifetime = new();
        controller.StateChanged += StateChanged;
        Refresh();
    }
    private void Detach()
    {
        timer.Stop();
        var detached = attached ? controller : null;
        attached = false;
        if (detached is not null) detached.StateChanged -= StateChanged;
        lifetime.Cancel();
        detached?.Stop();
    }
    private void StateChanged(object? sender, EventArgs e)
    {
        // State transitions are sparse. Pixel presentation is pulled by the active-only dispatcher timer.
        if (Dispatcher.CheckAccess()) Refresh();
        else if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(Refresh);
    }
    private void Refresh()
    {
        if (!attached || controller is null || !IsLoaded) return;
        if (controller.State == MediaPlaybackState.Playing) timer.Start(); else timer.Stop();
        if (!Timeline.IsMouseCaptureWithin && !Timeline.IsKeyboardFocusWithin) Timeline.Value = Seconds(controller.Position);
        Timecode.Text = TimeSpan.FromSeconds(Seconds(controller.Position)).ToString(@"hh\:mm\:ss\.fff");
        Status.Text = controller.State switch
        {
            MediaPlaybackState.Preparing => "Preparing local audition…",
            MediaPlaybackState.Failed => controller.Error?.Message ?? "Playback failed.",
            _ => $"{controller.State} · {(Original.IsChecked == true ? "Original effects bypass" : "Edited")}"
        };
        Waveform.Buckets = controller.Waveform;
        if (range.End.Denominator > 0 && range.Start.CompareTo(range.End) < 0)
        {
            double duration = Seconds(range.End) - Seconds(range.Start);
            Waveform.StartFraction = Math.Clamp((Seconds(controller.WaveformRange.Start) - Seconds(range.Start)) / duration, 0, 1);
            Waveform.EndFraction = Math.Clamp((Seconds(controller.WaveformRange.End) - Seconds(range.Start)) / duration, 0, 1);
        }
        controller.TryPresentFrame(frame =>
        {
            var pixels = MediaPresentation.Pixels(frame);
            var bitmap = BitmapSource.Create(frame.Size.Width, frame.Size.Height, 96, 96, PixelFormats.Bgra32, null, pixels, frame.Stride);
            bitmap.Freeze(); Preview.Source = bitmap;
        });
    }
    private async void PlayClicked(object sender, RoutedEventArgs e) => await Act(async () =>
    {
        if (controller is null || document is null) return;
        if (controller.State == MediaPlaybackState.Paused && controller.Position.CompareTo(range.End) < 0)
            await controller.ResumeAsync(lifetime.Token);
        else await controller.PlayAsync(document, range, Original.IsChecked == true, lifetime.Token);
    });
    private void PauseClicked(object sender, RoutedEventArgs e) { controller?.Pause(); Refresh(); }
    private void StopClicked(object sender, RoutedEventArgs e) { controller?.Stop(); Preview.Source = null; Refresh(); }
    private async void OriginalClicked(object sender, RoutedEventArgs e) => await Act(async () =>
    {
        if (controller is null || document is null) return;
        if (controller.Position.CompareTo(range.End) >= 0) await controller.PreparePausedAsync(document, range, Original.IsChecked == true, lifetime.Token);
        else await controller.SetOriginalAsync(Original.IsChecked == true, lifetime.Token);
    });
    private async void PreviousClicked(object sender, RoutedEventArgs e) => await Act(() => controller?.StepAsync(-1, lifetime.Token) ?? Task.CompletedTask);
    private async void NextClicked(object sender, RoutedEventArgs e) => await Act(() => controller?.StepAsync(1, lifetime.Token) ?? Task.CompletedTask);
    private async void SeekReleased(object sender, MouseButtonEventArgs e) => await Seek();
    private async void SeekKeyReleased(object sender, KeyEventArgs e)
    { if (e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown) await Seek(); }
    private Task Seek() => Act(async () =>
    {
        if (controller is null || document is null) return;
        var position = MediaPresentation.SeekPosition(Timeline.Value, range);
        if (position.CompareTo(range.End) >= 0)
        {
            // The range is half-open. The right edge pauses without issuing an invalid seek.
            controller.Pause(); Timeline.Value = Seconds(controller.Position); return;
        }
        await controller.SeekAsync(position, lifetime.Token);
        Refresh();
    });
    private async Task Act(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private static double Seconds(RationalTime value) => (double)value.Numerator / value.Denominator;
}
