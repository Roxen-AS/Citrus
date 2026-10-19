using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Citrus.Core;
using Citrus.Core.Presentation;
using Citrus.Windows.Presentation;

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
    private bool preparing, reduceMotion, acceptsEntry, presentationReleased;
    private readonly WheelBackdropController backdrop = new(nativeCompanionValidated: false);
    private MaterialDecision materialDecision = new(false, false, "NoResidentServices");
    private bool dark;
    private double textScale = 1;
    private EventHandler? pendingMaterial;
    private DispatcherOperation? pendingPlacement;
    internal BackdropState MaterialState => Shell.State;
    internal nint CompanionHandle => backdrop.CompanionHandle;
    internal bool MaterialPresentationPending => pendingMaterial is not null;
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
            backdrop.Attach(handle);
        };
        Surface.PreviewDragEnter += OnPreviewDragEnter;
        Surface.PreviewDragOver += OnPreviewDragOver;
        Surface.PreviewDrop += OnPreviewDrop;
        Surface.PreviewDragLeave += (_, e) => { e.Handled = true; Dismiss(); };
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(25), DispatcherPriority.Input, Tick, Dispatcher);
        timer.Stop();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible || presentationReleased) return;
            acceptsEntry = false; session.Cancel(generation); timer.Stop(); CancelWork();
            CancelMaterialPresentation(); pendingPlacement?.Abort(); pendingPlacement = null;
            backdrop.Hide(generation); Shell.State = BackdropState.Solid; CancelEmphasis();
        };
        backdrop.Invalidated += MaterialInvalidated;
        Closed += (_, _) => { ReleasePresentation(); CancelWork(); if (ownsAudio) this.audio.Dispose(); };
        (Application.Current as App)?.AttachPresentation(this);
    }

    public void ApplySettings(CitrusSettings settings)
    {
        reduceMotion = settings.ReduceMotion;
        audio.Enabled = settings.SoundEnabled;
        if (!Motion) CancelEmphasis();
    }

    internal void ApplyMaterialPolicy(MaterialDecision decision, bool dark, double textScale)
    {
        if (presentationReleased) return;
        materialDecision = decision; this.dark = dark; this.textScale = Math.Clamp(textScale, 1, 2.25);
        // Refreshes do not produce audio or reset payload, selection, anchor or arming.
        CancelEmphasis();
        ApplyTextScale();
        MaterialInvalidated();
    }

    private void ApplyTextScale()
    {
        bool compact = textScale > 1.25;
        Caption.FontSize = 14 * Math.Min(textScale, 2);
        Caption.LineHeight = compact ? 34 : 20;
        CaptionRow.Height = new GridLength(compact ? 38 : 20);
        HintRow.Height = new GridLength(compact ? 0 : 18);
        Hint.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Badge.FontSize = 12 * textScale; ModeLabel.FontSize = 11 * textScale;
        // These two disjoint rectangles sit wholly inside the opaque radius-78 hub.
        // Keep the requested font size; omit secondary metadata if its line no longer fits.
        FitHubMetadata(Badge, allowEllipsis: true);
        FitHubMetadata(ModeLabel, allowEllipsis: false);
        foreach (var petal in petals) petal.ApplyTextScale(textScale);
        System.Windows.Automation.AutomationProperties.SetName(this, $"Citrus {mode}: {paths.Count} files. {Badge.Text}. {Caption.Text}");
    }

    private static void FitHubMetadata(TextBlock label, bool allowEllipsis)
    {
        var measured = new FormattedText(label.Text, System.Globalization.CultureInfo.CurrentUICulture, label.FlowDirection,
            new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch), label.FontSize,
            Brushes.Black, VisualTreeHelper.GetDpi(label).PixelsPerDip);
        label.Visibility = measured.Height <= label.Height && (allowEllipsis || measured.WidthIncludingTrailingWhitespace <= label.Width)
            ? Visibility.Visible : Visibility.Collapsed;
        label.ToolTip = label.Text; // Full text also remains in the wheel's automation name.
    }

    private void CancelEmphasis()
    {
        foreach (var petal in petals) petal.CancelEmphasis();
        PressFeedback.GetAnimator(this)?.CancelOwner(Preview, true);
        Preview.Opacity = 1;
    }

    private void CancelMaterialPresentation()
    {
        if (pendingMaterial is not null) CompositionTarget.Rendering -= pendingMaterial;
        pendingMaterial = null;
    }

    private void MaterialInvalidated()
    {
        CancelMaterialPresentation();
        Shell.State = BackdropState.Solid;
        if (presentationReleased || handle == 0) return;
        // Unlike Hide, unconfirmed synchronization does not cancel this generation's future eligibility.
        backdrop.Synchronize(generation, default, 1, false, materialDecision, dark);
        QueueMaterialPresentation();
    }

    private void QueueMaterialPresentation()
    {
        if (presentationReleased || !session.IsConfirmed || !IsVisible || Presentation.Opacity != 1 || pendingMaterial is not null) return;
        long expectedGeneration = generation, expectedEpoch = presentationEpoch;
        bool solidFramePresented = false;
        EventHandler callback = null!;
        callback = (_, _) =>
        {
            if (expectedGeneration != generation || expectedEpoch != presentationEpoch || !session.IsConfirmed || !IsVisible)
            { if (ReferenceEquals(pendingMaterial, callback)) CancelMaterialPresentation(); return; }
            // Rendering fires before composition: allow one complete solid frame before native show/switch.
            if (!solidFramePresented) { solidFramePresented = true; return; }
            CancelMaterialPresentation();
            if (!BackdropNativeMethods.GetWindowRect(handle, out var rect)) return;
            double scale = Math.Max(96, NativeMethods.GetDpiForWindow(handle)) / 96d;
            Shell.State = backdrop.Synchronize(expectedGeneration, rect.ToScreenRect(), scale, true, materialDecision, dark).State;
        };
        pendingMaterial = callback;
        CompositionTarget.Rendering += callback;
    }

    internal void ReleasePresentation()
    {
        if (presentationReleased) return;
        presentationReleased = true; acceptsEntry = false;
        session.Cancel(generation); timer.Stop(); CancelWork(); audio.Stop(); ++presentationEpoch;
        CancelMaterialPresentation(); pendingPlacement?.Abort(); pendingPlacement = null;
        CancelEmphasis();
        backdrop.Invalidated -= MaterialInvalidated;
        backdrop.Dispose();
        Shell.State = BackdropState.Solid;
        if (handle != 0) HwndSource.FromHwnd(handle)?.RemoveHook(WindowMessage);
    }

    public void ShowAtPointer(OperationMode requestedMode)
    {
        Dispatcher.VerifyAccess();
        if (presentationReleased) return;
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
            CancelMaterialPresentation();
            pendingPlacement?.Abort(); pendingPlacement = null;
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
        ApplyTextScale();
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
            pendingPlacement?.Abort();
            pendingPlacement = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (!presentationReleased && IsVisible && generation == placedGeneration) Place(Math.Max(96, NativeMethods.GetDpiForWindow(handle)) / 96.0);
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
        session.Cancel(generation);
        timer.Stop();
        CancelWork(); CancelMaterialPresentation();
        pendingPlacement?.Abort(); pendingPlacement = null;
        audio.Stop(); CancelEmphasis();
        preparing = false; operations = []; availableOperations.Clear();
        armed = false; selected = -1; navigation = 0;
        ++presentationEpoch;
        backdrop.Hide(generation);
        Shell.State = BackdropState.Solid;
        SetPresentationVisible(false);
        Sectors.Children.Clear(); petals.Clear(); Preview.Source = null;
        Hide();
    }

    private void CancelWork()
    {
        presentationWork?.Cancel(); presentationWork?.Dispose(); presentationWork = null;
    }

    private void SetPresentationVisible(bool visible)
    {
        // Full geometry and opaque structural content are immediate; no target transforms or hide tail.
        Presentation.Opacity = visible ? 1 : 0;
        if (visible) QueueMaterialPresentation();
        else { CancelMaterialPresentation(); Shell.State = BackdropState.Solid; }
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
            if (image is not null && Motion && PressFeedback.GetAnimator(this) is { } animator)
            {
                animator.SetImmediate(Preview, "reveal", 0, value => Preview.Opacity = Math.Clamp(value, 0, 1));
                animator.Retarget(Preview, "reveal", 1, SpringParameters.Reveal, value => Preview.Opacity = Math.Clamp(value, 0, 1));
            }
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
        int next = armed && navigation == 0 ? HitTarget(point) : -1;
        if (next != selected) Select(next);
        if (selected >= 0 || navigation != 0) e.Effects = DragDropEffects.Copy;
    }

    private int HitTarget(Point point)
    {
        // Use the retained renderer's exact fill convention, including rounded edges. No rebuilt paths.
        for (int i = 0; i < petals.Count; i++) if (petals[i].ContainsPoint(point)) return i;
        return -1;
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
            int index = armed && NavigationAt(point) == 0 ? HitTarget(point) : -1;
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
        foreach (var petal in petals) petal.CancelEmphasis();
        Sectors.Children.Clear(); petals.Clear();
        var items = operations.Skip(paging.Page * WheelGeometry.MaxTargetsPerPage).Take(WheelGeometry.MaxTargetsPerPage).ToArray();
        for (int i=0;i<items.Length;i++)
        {
            var petal = new WheelPetal(items[i],i,items.Length);
            petals.Add(petal); Sectors.Children.Add(petal);
            petal.ApplyTextScale(textScale);
        }
        Caption.Text = "Center to cancel";
        Previous.Visibility = Next.Visibility = operations.Count > WheelGeometry.MaxTargetsPerPage ? Visibility.Visible : Visibility.Hidden;
        Previous.Child.Opacity = paging.Page == 0 ? .35 : 1;
        Next.Child.Opacity = (paging.Page+1)*WheelGeometry.MaxTargetsPerPage >= operations.Count ? .35 : 1;
        if (operations.Count > WheelGeometry.MaxTargetsPerPage) Hint.Text = $"{paging.Page+1} / {(operations.Count+WheelGeometry.MaxTargetsPerPage-1)/WheelGeometry.MaxTargetsPerPage} · hover arrows";
    }
}
