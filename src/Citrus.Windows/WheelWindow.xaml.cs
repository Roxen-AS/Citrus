using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Citrus.Core;
using System.Windows.Media.Animation;

namespace Citrus.Windows;

public sealed record OperationDrop(OperationDescriptor Operation, IReadOnlyList<string> Paths);

public partial class WheelWindow : Window
{
    private readonly OperationCatalog catalog;
    private static readonly SemaphoreSlim catalogWorkers = new(2);
    private readonly IFilePreviewService previews;
    private readonly FeedbackAudio audio;
    private readonly bool ownsAudio;
    private CancellationTokenSource? presentationWork;
    private readonly List<WheelPetal> petals = [];
    private bool preparing, reduceMotion, acceptsEntry;
    private long presentationEpoch;
    private bool Motion => !reduceMotion && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
    private readonly Dictionary<OperationMode, IReadOnlyList<OperationDescriptor>> availableOperations = [];
    private readonly WheelSession session = new();
    private readonly DispatcherTimer timer;
    private WheelPaging paging = new();
    private IReadOnlyList<OperationDescriptor> operations = [];
    private IReadOnlyList<string> paths => session.Paths;
    private OperationMode mode => session.Mode;
    private long generation;
    private int selected = -1, navigation;
    private bool armed;
    private nint handle;
    public event Action<OperationDrop>? OperationDropped;

    public WheelWindow(OperationCatalog catalog, FeedbackAudio? audio = null, IFilePreviewService? previewService = null)
    {
        this.catalog = catalog;
        previews = previewService ?? new FilePreviewService();
        this.audio = audio ?? new FeedbackAudio();
        ownsAudio = audio is null;
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLongPtr(handle, -20);
            NativeMethods.SetWindowLongPtr(handle, -20, style | 0x08000000 | 0x00000080); // NOACTIVATE | TOOLWINDOW; preserve existing styles.
            HwndSource.FromHwnd(handle)?.AddHook(WindowMessage);
        };
        Surface.PreviewDragEnter += OnPreviewDragEnter;
        Surface.PreviewDragOver += OnPreviewDragOver;
        Surface.PreviewDrop += OnPreviewDrop;
        Surface.PreviewDragLeave += (_, e) => { e.Handled = true; Dismiss(); };
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(25), DispatcherPriority.Input, Tick, Dispatcher);
        timer.Stop();
        Closed += (_, _) => { CancelWork(); if (ownsAudio) this.audio.Dispose(); };
    }

    public void ApplySettings(CitrusSettings settings)
    {
        reduceMotion = settings.ReduceMotion;
        audio.Enabled = settings.SoundEnabled;
        if (!Motion) { StopPresentationAnimation(); if (session.IsConfirmed) Presentation.Opacity = 1; }
    }

    public void ShowAtPointer(OperationMode requestedMode)
    {
        Dispatcher.VerifyAccess();
        acceptsEntry = true;
        NativeMethods.GetCursorPos(out var pointer);
        bool newSession = session.RequestMode(Environment.TickCount64, requestedMode, pointer.X, pointer.Y);
        generation = session.Generation;
        paging = new();
        selected = -1;
        navigation = 0;
        armed = false;
        if (newSession)
        {
            CancelWork();
            presentationEpoch++;
            preparing = false;
            availableOperations.Clear();
            Preview.Source = null;
            FileGlyph.Visibility = Visibility.Visible;
            Caption.Text = "Center to cancel";
            SetPresentationVisible(false);
        }
        // Prepare content before showing a real capture HWND. Threshold/marquee candidates have
        // only Surface's nonzero-alpha background: OLE can target them, but no menu is displayed.
        operations = availableOperations.GetValueOrDefault(mode) ?? [];
        ModeLabel.Text = mode.ToString().ToUpperInvariant();
        UpdatePayloadLabel();
        RenderSectors();
        if (!session.IsConfirmed) SetPresentationVisible(false);
        if (newSession)
        {
            new WindowInteropHelper(this).EnsureHandle();
            Place(NativeMethods.MonitorScale(pointer));
            Show(); // ShowActivated=false; no Focus, Activate or mouse capture.
            Place(Math.Max(96, NativeMethods.GetDpiForWindow(handle)) / 96.0);
        }
        // A mode change keeps native placement and OLE payload ownership; only selection is rearmed.
        var initial = Surface.PointFromScreen(new Point(pointer.X, pointer.Y));
        session.ResetPointer(initial.X, initial.Y);
        timer.Start();
    }

    private void UpdatePayloadLabel()
    {
        CountBadge.Visibility = paths.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        CountLabel.Text = paths.Count.ToString();
        Badge.Text = paths.Count == 0 ? "Drag a file here" : preparing ? "Checking available tools" : operations.Count == 0 ? "No supported operations" :
            paths.Count == 1 ? Path.GetFileName(paths[0]) : $"{paths.Count} files";
        Hint.Text = preparing ? "Preparing…" : paths.Count > 0 && operations.Count == 0 ? "Release to cancel" : "Hold · glide · release";
    }

    private void Place(double scale)
    {
        var anchor = new NativeMethods.Point { X = (int)session.Anchor.X, Y = (int)session.Anchor.Y };
        var bounds = WheelGeometry.Place(anchor.X, anchor.Y, NativeMethods.WorkArea(anchor), scale);
        NativeMethods.SetWindowPos(handle, new nint(-1), (int)bounds.Left, (int)bounds.Top,
            (int)bounds.Width, (int)bounds.Height, 0x10); // SWP_NOACTIVATE
    }

    private nint WindowMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x21) { handled = true; return 3; } // MA_NOACTIVATE
        if (message == 0x02E0) // WM_DPICHANGED: WPF updates its transforms; clamp native placement afterward.
        {
            long placedGeneration = generation;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (IsVisible && generation == placedGeneration) Place(Math.Max(96, NativeMethods.GetDpiForWindow(handle)) / 96.0);
            }));
        }
        return 0;
    }

    /// <summary>Called by the mouse observer: capture identity now, then schedule only unconfirmed cleanup.</summary>
    public void ObserveGlobalRelease()
    {
        long releasedGeneration = generation;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (session.GlobalRelease(releasedGeneration)) Dismiss();
        }));
    }

    public void Dismiss()
    {
        acceptsEntry = false;
        double dismissalOpacity = Presentation.Opacity;
        bool animate = dismissalOpacity > 0 && Motion;
        session.Cancel(generation);
        timer.Stop();
        CancelWork();
        audio.Stop();
        preparing = false;
        operations = [];
        availableOperations.Clear();
        armed = false;
        selected = -1;
        navigation = 0;
        long dismissEpoch = ++presentationEpoch;
        StopPresentationAnimation();
        if (animate)
        {
            // Ownership is already canceled. This tail is presentation only; callbacks cannot submit.
            var fade = new DoubleAnimation(dismissalOpacity, 0, (Duration)FindResource("DismissDuration"));
            fade.Completed += (_, _) => { if (presentationEpoch == dismissEpoch) FinishDismiss(); };
            Presentation.BeginAnimation(OpacityProperty,fade);
        }
        else FinishDismiss();
    }

    private void FinishDismiss()
    {
        SetPresentationVisible(false);
        Sectors.Children.Clear(); petals.Clear(); Preview.Source = null;
        Hide();
    }

    private void CancelWork()
    {
        presentationWork?.Cancel(); presentationWork?.Dispose(); presentationWork = null;
    }

    private void StopPresentationAnimation()
    {
        Presentation.BeginAnimation(OpacityProperty,null);
        PresentationScale.BeginAnimation(ScaleTransform.ScaleXProperty,null);
        PresentationScale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
        PresentationScale.ScaleX = PresentationScale.ScaleY = 1;
    }

    private void SetPresentationVisible(bool visible)
    {
        // Surface remains a real nonzero-alpha OLE target, independent of presentation transforms.
        StopPresentationAnimation();
        Presentation.Opacity = visible ? 1 : 0;
        if (visible && Motion)
        {
            var duration = (Duration)FindResource("OpenDuration");
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Presentation.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,duration) { EasingFunction = ease });
            PresentationScale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(.94,1,duration) { EasingFunction = ease });
            PresentationScale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(.94,1,duration) { EasingFunction = ease });
        }
    }

    private bool ReadPhysicalPayload(DragEventArgs e, out string[] payload, out FileAttributes[] attributes)
    {
        Dispatcher.VerifyAccess(); // Never allow COM IDataObject or DragEventArgs to leave this callback/STA.
        payload = [];
        attributes = [];
        if ((e.AllowedEffects & DragDropEffects.Copy) == 0) return false;
        try
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop, false) || e.Data.GetData(DataFormats.FileDrop, false) is not string[] raw || raw.Length == 0) return false;
            payload = raw.ToArray();
            if (payload.Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))) return false;
            attributes = payload.Select(File.GetAttributes).ToArray();
            // Actions is the physical availability policy, including directories. Mode eligibility
            // is separate, so switching a folder to Convert/Tools cannot destroy OLE ownership.
            return attributes.All(value => DropValidation.AttributesAllowed(value, OperationMode.Actions));
        }
        catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException) { return false; }
    }

    private void OnPreviewDragEnter(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (!IsVisible || !acceptsEntry) return;
        if (!session.IsConfirmed)
        {
            if (!ReadPhysicalPayload(e, out var payload, out var attributes)) { Dismiss(); return; }
            session.ConfirmPayload(payload); // First admission alone owns the immutable ordered snapshot.
            preparing = true;
            presentationWork = new CancellationTokenSource();
            operations = [];
            paging = new();
            selected = -1;
            navigation = 0;
            var entry = e.GetPosition(Surface);
            session.ResetPointer(entry.X, entry.Y);
            UpdatePayloadLabel();
            RenderSectors();
            SetPresentationVisible(true);
            audio.Play(FeedbackCue.Open);
            _ = PrepareAsync(payload, attributes, generation, presentationWork.Token);
            _ = PreviewAsync(payload[0], generation, presentationWork.Token);
        }
        OnPreviewDragOver(sender, e);
    }

    private async Task PrepareAsync(string[] payload, FileAttributes[] attributes, long expected, CancellationToken token)
    {
        try
        {
            await catalogWorkers.WaitAsync(token);
            Dictionary<OperationMode,IReadOnlyList<OperationDescriptor>> prepared;
            try
            {
                prepared = await Task.Run(() =>
                {
                    var result = new Dictionary<OperationMode,IReadOnlyList<OperationDescriptor>>();
                    foreach (var candidateMode in Enum.GetValues<OperationMode>())
                    {
                        token.ThrowIfCancellationRequested();
                        result[candidateMode] = attributes.All(value => DropValidation.AttributesAllowed(value,candidateMode))
                            ? catalog.GetOperations(payload,candidateMode) : [];
                    }
                    return result;
                },token);
            }
            finally { catalogWorkers.Release(); }
            if (token.IsCancellationRequested || !session.IsConfirmed || expected != generation) return;
            foreach (var pair in prepared) availableOperations[pair.Key] = pair.Value;
            preparing = false;
            operations = availableOperations[mode]; // Mode may have changed while the worker was busy.
            selected = -1; armed = false;
            NativeMethods.GetCursorPos(out var pointer);
            var local = Surface.PointFromScreen(new Point(pointer.X,pointer.Y));
            session.ResetPointer(local.X,local.Y);
            UpdatePayloadLabel(); RenderSectors();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            if (!token.IsCancellationRequested && session.IsConfirmed && expected == generation)
            { preparing = false; operations = []; UpdatePayloadLabel(); Hint.Text = "Unavailable · release to cancel"; }
        }
    }

    private async Task PreviewAsync(string path, long expected, CancellationToken token)
    {
        try
        {
            var image = await previews.LoadAsync(path,192,token);
            if (token.IsCancellationRequested || !session.IsConfirmed || expected != generation) return;
            Preview.Source = image;
            FileGlyph.Visibility = image is null ? Visibility.Visible : Visibility.Hidden;
        }
        catch (OperationCanceledException) { }
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        // The steady hover path uses only cached admission, operations and local coordinates.
        // Neither COM extraction nor filesystem/catalog work belongs here.
        if (!session.IsConfirmed) return;
        if ((e.AllowedEffects & DragDropEffects.Copy) == 0) { Dismiss(); return; }
        if (preparing) return;
        var point = e.GetPosition(Surface);
        armed = session.ObservePointer(point.X, point.Y);
        navigation = armed ? NavigationAt(point) : 0;
        int next = armed && navigation == 0 ? WheelGeometry.HitTest(point.X, point.Y, Math.Min(WheelGeometry.MaxTargetsPerPage, operations.Count - paging.Page * WheelGeometry.MaxTargetsPerPage)) : -1;
        if (next != selected) Select(next);
        if (selected >= 0 || navigation != 0) e.Effects = DragDropEffects.Copy;
    }

    private int NavigationAt(Point point)
    {
        if (operations.Count <= WheelGeometry.MaxTargetsPerPage || point.Y is < 370 or > 410) return 0;
        if (point.X is >= 18 and <= 58 && paging.Page > 0) return -1;
        if (point.X is >= 310 and <= 350 && (paging.Page + 1) * WheelGeometry.MaxTargetsPerPage < operations.Count) return 1;
        return 0;
    }

    private void OnPreviewDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        OperationDrop? accepted = null;
        if (session.IsConfirmed && ReadPhysicalPayload(e, out var payload, out var attributes) &&
            payload.SequenceEqual(paths, StringComparer.OrdinalIgnoreCase) &&
            attributes.All(value => DropValidation.AttributesAllowed(value, mode)))
        {
            var point = e.GetPosition(Surface);
            int index = armed && NavigationAt(point) == 0 ? WheelGeometry.HitTest(point.X, point.Y, Math.Min(WheelGeometry.MaxTargetsPerPage, operations.Count - paging.Page * WheelGeometry.MaxTargetsPerPage)) : -1;
            if (index >= 0 && session.TryCompleteDrop(generation))
            {
                var operation = operations[paging.Page * WheelGeometry.MaxTargetsPerPage + index];
                accepted = new(operation, Array.AsReadOnly(payload));
                e.Effects = DragDropEffects.Copy;
            }
        }
        Dismiss();
        // OLE returns before any option dialog, jobs, clipboard work or other application callbacks.
        if (accepted is { } drop) Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => OperationDropped?.Invoke(drop)));
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (session.ShouldExpire(generation, Environment.TickCount64)) { Dismiss(); return; }
        int old = paging.Page;
        paging.UpdateHover(navigation, Environment.TickCount64, operations.Count);
        if (old != paging.Page) { selected = -1; RenderSectors(); }
    }

    private void Select(int next)
    {
        if (selected >= 0 && selected < petals.Count) petals[selected].SetSelected(false,Motion);
        selected = next;
        if (selected >= 0)
        {
            petals[selected].SetSelected(true,Motion);
            Caption.Text = operations[paging.Page * WheelGeometry.MaxTargetsPerPage + selected].Label;
            audio.Play(FeedbackCue.Selection);
        }
        else Caption.Text = "Center to cancel";
    }

    private void RenderSectors()
    {
        Sectors.Children.Clear(); petals.Clear();
        var items = operations.Skip(paging.Page * WheelGeometry.MaxTargetsPerPage).Take(WheelGeometry.MaxTargetsPerPage).ToArray();
        for (int i=0;i<items.Length;i++)
        {
            var petal = new WheelPetal(items[i],i,items.Length);
            petals.Add(petal); Sectors.Children.Add(petal);
        }
        Caption.Text = "Center to cancel";
        Previous.Visibility = Next.Visibility = operations.Count > WheelGeometry.MaxTargetsPerPage ? Visibility.Visible : Visibility.Hidden;
        Previous.Opacity = paging.Page == 0 ? .35 : 1;
        Next.Opacity = (paging.Page+1)*WheelGeometry.MaxTargetsPerPage >= operations.Count ? .35 : 1;
        if (operations.Count > WheelGeometry.MaxTargetsPerPage) Hint.Text = $"{paging.Page+1} / {(operations.Count+WheelGeometry.MaxTargetsPerPage-1)/WheelGeometry.MaxTargetsPerPage} · hover arrows";
    }
}
