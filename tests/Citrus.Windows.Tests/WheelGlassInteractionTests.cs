using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Citrus.Core;
using Citrus.Core.Presentation;
using Citrus.Windows.Presentation;
using DataObject = System.Windows.DataObject;
using DataFormats = System.Windows.DataFormats;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using Point = System.Windows.Point;

namespace Citrus.Windows.Tests;

/// <summary>Production WPF routed FileDrop callbacks; native Explorer/OLE and compositor proof remain separate.</summary>
[Collection("Desktop")]
public sealed class WheelGlassInteractionTests(DesktopFixture desktop)
{
    [Theory]
    [InlineData(6, 6)] [InlineData(7, 7)] [InlineData(9, 9)] [InlineData(11, 9)]
    public Task Confirmed_shell_is_complete_before_native_helper_appears(int count, int firstPageCount) => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture(count);
        fixture.Wheel.ShowAtPointer(OperationMode.Convert);
        Assert.Equal(0, fixture.Presentation.Opacity);
        Assert.Equal(0, fixture.Wheel.CompanionHandle);
        fixture.Enter();
        Assert.Equal(1, fixture.Presentation.Opacity);
        Assert.True(fixture.Presentation.RenderTransform.Value.IsIdentity);
        Assert.Equal(BackdropState.Solid, fixture.Wheel.MaterialState);
        Assert.Equal(0, fixture.Wheel.CompanionHandle);
        await fixture.Ready();
        Assert.Equal(firstPageCount, fixture.Petals.Length);
        Assert.Equal(Enumerable.Range(0, firstPageCount).Select(index => $"Convert.{index:D2}"), fixture.OperationIds);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(BackdropState.Solid, fixture.Wheel.MaterialState); // Production gate is deliberately unproven.
    });

    [Fact]
    public Task Reopen_invalidates_pending_show_and_cleanup() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture(7);
        fixture.Wheel.ShowAtPointer(OperationMode.Convert); fixture.Enter();
        Assert.False(fixture.Wheel.MaterialPresentationPending);
        Assert.Equal(1, fixture.Presentation.Opacity);
        fixture.Wheel.Dismiss();
        Assert.False(fixture.Wheel.MaterialPresentationPending);
        Assert.False(fixture.Wheel.IsVisible);
        fixture.Wheel.ShowAtPointer(OperationMode.Actions);
        Assert.Equal(0, fixture.Presentation.Opacity);
        fixture.Enter(); await fixture.Ready();
        await Task.Delay(150);
        Assert.True(fixture.Wheel.IsVisible);
        Assert.Equal(1, fixture.Presentation.Opacity);
        Assert.Equal(0, fixture.Wheel.CompanionHandle);
    });

    [Theory]
    [InlineData(6)] [InlineData(7)] [InlineData(9)]
    public Task All_hit_points_equal_drawn_petals_during_spring_frames(int count) => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture(count);
        fixture.Wheel.ShowAtPointer(OperationMode.Convert); fixture.Enter(); await fixture.Ready();
        using var animator = new SpringAnimator();
        PressFeedback.SetAnimator(fixture.Wheel, animator);
        try
        {
            for (int frame = 0; frame < 8; frame++)
            {
                for (int index = 0; index < fixture.Petals.Length; index++)
                {
                    var petal = fixture.Petals[index];
                    petal.SetSelected(frame % 2 == 0, true);
                    var center = WheelGeometry.At(122, index * 360d / fixture.Petals.Length);
                    Assert.True(petal.ContainsPoint(new Point(center.X, center.Y)));
                    Assert.True(petal.RenderTransform.Value.IsIdentity);
                    Assert.True(fixture.Presentation.RenderTransform.Value.IsIdentity);
                    var fill = (System.Windows.Shapes.Path)petal.Children[1];
                    Assert.Equal(frame % 2 == 0 ? 1 : 0, fill.Opacity);
                    Assert.False(fill.HasAnimatedProperties);
                }
                await Task.Delay(16);
            }
        }
        finally { foreach (var petal in fixture.Petals) petal.CancelEmphasis(); PressFeedback.SetAnimator(fixture.Wheel, null); }
    });

    [Fact]
    public Task Large_text_wheel_keeps_fixed_targets_and_accessible_full_names() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture(9);
        fixture.Wheel.ShowAtPointer(OperationMode.Convert); fixture.Enter(); await fixture.Ready();
        var before = fixture.Petals;
        fixture.Wheel.ApplyMaterialPolicy(new(false, false, "TextTest"), false, 2);
        Assert.InRange(fixture.Wheel.Width, WheelPresentationMetrics.Width - 1, WheelPresentationMetrics.Width + 1);
        Assert.InRange(fixture.Wheel.Height, WheelPresentationMetrics.Height - 1, WheelPresentationMetrics.Height + 1);
        Assert.Equal(Visibility.Collapsed, ((TextBlock)fixture.Wheel.FindName("Hint")).Visibility);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)fixture.Wheel.FindName("NavigationPlate")).Visibility);
        Assert.True(((TextBlock)fixture.Wheel.FindName("Caption")).FontSize > 16.5);
        Assert.True(((RowDefinition)fixture.Wheel.FindName("CaptionRow")).Height.IsStar);
        Assert.Equal(before, fixture.Petals);
        Assert.Equal(Enumerable.Range(0, 9).Select(index => $"Convert.{index:D2}"), fixture.OperationIds);
        foreach (var petal in before)
        {
            Assert.StartsWith("Operation ", System.Windows.Automation.AutomationProperties.GetName(petal));
            Assert.Equal(Visibility.Visible, petal.Children.OfType<TextBlock>().Single().Visibility);
        }
    });

    [Fact]
    public Task Mode_page_changes_preserve_geometry_anchor_payload_and_arming() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture(11);
        fixture.Wheel.ShowAtPointer(OperationMode.Convert); fixture.Enter(); await fixture.Ready();
        var hwnd = new WindowInteropHelper(fixture.Wheel).Handle;
        BackdropNativeMethods.GetWindowRect(hwnd, out var before);
        fixture.Raise(DragDrop.PreviewDragOverEvent, new Point(184, 184));
        var next = (FrameworkElement)fixture.Wheel.FindName("Next");
        var surface = (FrameworkElement)fixture.Wheel.FindName("Surface");
        fixture.Raise(DragDrop.PreviewDragOverEvent, next.TransformToAncestor(surface).Transform(new Point(next.ActualWidth / 2, next.ActualHeight / 2)));
        for (int i = 0; i < 100 && fixture.Petals.Length != 2; i++) await Task.Delay(10);
        Assert.Equal(2, fixture.Petals.Length);
        Assert.Equal(new[] { "Convert.09", "Convert.10" }, fixture.OperationIds);
        var session = (WheelSession)typeof(WheelWindow).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Wheel)!;
        var paths = session.Paths;
        fixture.Wheel.ShowAtPointer(OperationMode.Actions);
        Assert.Same(paths, session.Paths);
        BackdropNativeMethods.GetWindowRect(hwnd, out var after);
        Assert.Equal(before.ToScreenRect(), after.ToScreenRect());
        Assert.Equal(DragDropEffects.None, fixture.Raise(DragDrop.PreviewDragOverEvent, new Point(184, 184)).Effects);
        Assert.Equal(0, fixture.Wheel.CompanionHandle);
    });

    [Fact]
    public Task No_companion_windowpos_calls_on_repeated_dragover() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture(9);
        fixture.Wheel.ShowAtPointer(OperationMode.Convert); fixture.Enter(); await fixture.Ready();
        var source = HwndSource.FromHwnd(new WindowInteropHelper(fixture.Wheel).Handle);
        int moves = 0;
        nint Observe(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
        { if (message is 0x46 or 0x47) moves++; return 0; }
        source.AddHook(Observe);
        try
        {
            for (int i = 0; i < 100; i++) fixture.Raise(DragDrop.PreviewDragOverEvent, new Point(184, 62));
            Assert.Equal(0, moves);
            Assert.Equal(0, fixture.Wheel.CompanionHandle); // Gate false: no helper can receive native moves either.
        }
        finally { source.RemoveHook(Observe); }
    });

    [Fact]
    public Task Dpi_update_hides_mismatched_helper_until_final_placement() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture(7);
        fixture.Wheel.ShowAtPointer(OperationMode.Convert); fixture.Enter(); await fixture.Ready();
        var hwnd = new WindowInteropHelper(fixture.Wheel).Handle;
        Assert.True(BackdropNativeMethods.GetWindowRect(hwnd, out var original));
        NativeMethods.SetWindowPos(hwnd, 0, original.Left + 1, original.Top, original.Right - original.Left, original.Bottom - original.Top, 0x14);
        Assert.Equal(BackdropState.Solid, fixture.Wheel.MaterialState);
        Assert.Equal(0, fixture.Wheel.CompanionHandle);
        Assert.False(fixture.Wheel.MaterialPresentationPending);
        Assert.Equal(1, fixture.Presentation.Opacity);
        fixture.Wheel.Dismiss();
        Assert.False(fixture.Wheel.MaterialPresentationPending);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.False(fixture.Wheel.IsVisible);
        // This exercises actual HWND placement invalidation, not a claim of changing physical monitor DPI.
    });

    [Fact]
    public Task Policy_refresh_does_not_replay_audio_or_change_selection() => desktop.RunAsync(async () =>
    {
        var player = new ReadyPlayer();
        using var readyAudio = new FeedbackAudio(Task.FromResult<FeedbackAudio.IPlayer>(player), TimeProvider.System);
        using var fixture = new Fixture(7, readyAudio);
        fixture.Wheel.ShowAtPointer(OperationMode.Convert); fixture.Enter(); await fixture.Ready();
        var audio = (FeedbackAudio)typeof(WheelWindow).GetField("audio", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Wheel)!;
        // Ensure a genuine selection transition even if the physical pointer has
        // already settled over the first target while this fixture was preparing.
        var other = WheelGeometry.At(118, 360d / 7);
        fixture.Raise(DragDrop.PreviewDragOverEvent, new Point(other.X, other.Y));
        audio.Enabled = true;
        fixture.Raise(DragDrop.PreviewDragOverEvent, new Point(184, 62));
        var caption = ((TextBlock)fixture.Wheel.FindName("Caption")).Text;
        var epoch = typeof(FeedbackAudio).GetField("epoch", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var selection = typeof(FeedbackAudio).GetField("lastSelection", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var oldEpoch = epoch.GetValue(audio); var oldSelection = selection.GetValue(audio);
        Assert.NotEqual(long.MinValue, (long)oldSelection!);
        Assert.Equal(1, player.Starts);
        await Task.Delay(60);
        fixture.Wheel.ApplyMaterialPolicy(new(false, false, "PolicyRefresh"), true, 2);
        Assert.Equal(oldEpoch, epoch.GetValue(audio)); Assert.Equal(oldSelection, selection.GetValue(audio));
        Assert.Equal(1, player.Starts);
        Assert.True(audio.Enabled);
        Assert.Equal(caption, ((TextBlock)fixture.Wheel.FindName("Caption")).Text);
        Assert.Equal(1, ((System.Windows.Shapes.Path)fixture.Petals[0].Children[1]).Opacity);
    });

    private sealed class Fixture : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        private readonly DataObject data;
        public WheelWindow Wheel { get; }
        public Canvas Presentation => (Canvas)Wheel.FindName("Presentation");
        public WheelPetal[] Petals => ((Canvas)Wheel.FindName("Sectors")).Children.OfType<WheelPetal>().ToArray();
        public IEnumerable<string> OperationIds => ((IReadOnlyList<OperationDescriptor>)typeof(WheelWindow)
            .GetField("pageOperations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Wheel)!).Select(operation => operation.Id);
        public Fixture(int count, FeedbackAudio? audio = null)
        {
            File.WriteAllText(path, "owned physical payload"); data = new(DataFormats.FileDrop, new[] { path });
            Wheel = new(new OperationCatalog([new Engine(count)]), audio);
            Wheel.ApplySettings(new() { SoundEnabled = false });
        }
        public void Enter() => Raise(DragDrop.PreviewDragEnterEvent, new Point(184,184));
        public DragEventArgs Raise(RoutedEvent routedEvent, Point point)
        {
            var surface = (Canvas)Wheel.FindName("Surface");
            var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Copy, surface, point], null)!;
            args.RoutedEvent = routedEvent; surface.RaiseEvent(args); return args;
        }
        public async Task Ready()
        {
            for (int i = 0; i < 500 && ((TextBlock)Wheel.FindName("Hint")).Text.StartsWith("Preparing"); i++) await Task.Delay(10);
            Assert.NotEmpty(Petals);
        }
        public void Dispose() { Wheel.Dismiss(); Wheel.Close(); File.Delete(path); }
    }
    private sealed class ReadyPlayer : FeedbackAudio.IPlayer
    {
        public int Starts { get; private set; }
        public void Play() => Starts++;
        public void Stop() { }
        public void Dispose() { }
    }
    private sealed class Engine(int count) : IConversionEngine
    {
        public IReadOnlyList<OperationDescriptor> Operations { get; } = Enum.GetValues<OperationMode>().SelectMany(mode => Enumerable.Range(0, count)
            .Select(i => new OperationDescriptor($"{mode}.{i:D2}", $"Operation {i}", mode, null, false, false))).ToArray();
        public bool CanExecute(OperationRequest request) => true;
        public Task<ConversionResult> ExecuteAsync(OperationRequest request, IProgress<ConversionProgress>? progress, CancellationToken token) => throw new NotSupportedException();
    }
}
