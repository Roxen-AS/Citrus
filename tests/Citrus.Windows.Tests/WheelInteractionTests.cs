using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Citrus.Core;
using DataObject = System.Windows.DataObject;
using IDataObject = System.Windows.IDataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using DataFormats = System.Windows.DataFormats;
using Point = System.Windows.Point;

namespace Citrus.Windows.Tests;

/// <summary>Real WPF windows and FileDrop data on an STA. Routed callbacks exercise the target;
/// native Explorer/OLE initiation, HWND hit testing and hook ordering require Windows acceptance.</summary>
[Collection("Desktop")]
public sealed class WheelInteractionTests(DesktopFixture desktop)
{
    [Theory]
    [InlineData("release")]
    [InlineData("new-button-down")]
    [InlineData("disable-and-reenable")]
    [InlineData("escape")]
    [InlineData("dispose")]
    public Task Queued_mode_request_cannot_reopen_after_invalidation(string invalidation) => desktop.RunAsync(async () =>
    {
        using var monitor = new GestureMonitor(Dispatcher.CurrentDispatcher);
        var state = (GestureState)typeof(GestureMonitor).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(monitor)!;
        state.ButtonDown(0, 0);
        state.Move(1000, 1000);
        int requests = 0, escapes = 0;
        monitor.ModeRequested += _ => requests++;
        monitor.EscapePressed += () => escapes++;
        var dispatch = typeof(GestureMonitor).GetMethod("Dispatch", BindingFlags.Instance | BindingFlags.NonPublic)!;
        dispatch.Invoke(monitor, [OperationMode.Convert]);
        switch (invalidation)
        {
            case "release": state.ButtonUp(); break;
            case "new-button-down": state.ButtonDown(2000, 2000); break;
            case "disable-and-reenable": monitor.SetEnabled(false); monitor.SetEnabled(true); break;
            case "escape":
                typeof(GestureMonitor).GetMethod("DispatchCancellation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(monitor, null);
                state.ButtonUp(); // release after Escape must not suppress its cancellation callback
                break;
            case "dispose": monitor.Dispose(); break;
        }
        await Dispatcher.Yield(DispatcherPriority.Background);
        Assert.Equal(0, requests);
        Assert.Equal(invalidation == "escape" ? 1 : 0, escapes);
    });

    [Fact]
    public Task Current_drag_mode_request_is_dispatched() => desktop.RunAsync(async () =>
    {
        using var monitor = new GestureMonitor(Dispatcher.CurrentDispatcher);
        var state = (GestureState)typeof(GestureMonitor).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(monitor)!;
        state.ButtonDown(0, 0);
        state.Move(1000, 1000);
        var requested = new List<OperationMode>();
        monitor.ModeRequested += requested.Add;
        typeof(GestureMonitor).GetMethod("Dispatch", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(monitor, [OperationMode.Convert]);
        await Dispatcher.Yield(DispatcherPriority.Background);
        Assert.Equal(OperationMode.Convert, Assert.Single(requested));
    });

    [Fact]
    public Task Threshold_candidate_and_non_file_entry_never_show_menu_content() => desktop.RunAsync(() =>
    {
        using var fixture = new WheelFixture();
        fixture.Wheel.ShowAtPointer(OperationMode.Convert);
        Assert.True(fixture.Wheel.IsVisible); // A real target HWND must exist before OLE can enter.
        Assert.All(fixture.Surface.Children.Cast<UIElement>(), child => Assert.Equal(0, child.Opacity));
        var text = new DataObject(DataFormats.Text, "not a physical FileDrop");
        Assert.Equal(DragDropEffects.None, fixture.Raise(DragDrop.PreviewDragEnterEvent, text).Effects);
        Assert.False(fixture.Wheel.IsVisible);
        fixture.Wheel.ShowAtPointer(OperationMode.Actions);
        Assert.All(fixture.Surface.Children.Cast<UIElement>(), child => Assert.Equal(0, child.Opacity));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Physical_FileDrop_data_object_reveals_targets_only_after_entry() => desktop.RunAsync(() =>
    {
        using var fixture = new WheelFixture();
        fixture.Wheel.ShowAtPointer(OperationMode.Convert);
        var physical = new DataObject(DataFormats.FileDrop, new[] { fixture.A, fixture.B });
        fixture.Raise(DragDrop.PreviewDragEnterEvent, physical);
        Assert.True(fixture.Wheel.IsVisible);
        Assert.True(fixture.Sectors.Opacity > 0);
        Assert.NotEmpty(fixture.Sectors.Children.Cast<UIElement>());
        Assert.Equal(DragDropEffects.Copy, fixture.Raise(DragDrop.PreviewDragOverEvent, physical, new Point(168, 50)).Effects);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(DragDropEffects.None)]
    [InlineData(DragDropEffects.Move)]
    [InlineData(DragDropEffects.Link)]
    public Task FileDrop_without_copy_permission_never_reveals_the_wheel(DragDropEffects effects) => desktop.RunAsync(() =>
    {
        using var fixture = new WheelFixture();
        fixture.Wheel.ShowAtPointer(OperationMode.Convert);
        Assert.Equal(DragDropEffects.None, fixture.Raise(DragDrop.PreviewDragEnterEvent, fixture.Data, effects: effects).Effects);
        Assert.False(fixture.Wheel.IsVisible);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Repeated_hover_and_mode_switches_do_not_extract_payload_or_recheck_engines() => desktop.RunAsync(() =>
    {
        using var fixture = new WheelFixture();
        fixture.Enter();
        Assert.Equal(1, fixture.Data.Reads);
        int checks = fixture.Engine.Checks;
        Assert.True(checks > 0);
        fixture.Data.ThrowOnRead = true;
        for (int i = 0; i < 100; i++)
            Assert.Equal(DragDropEffects.Copy, fixture.Raise(DragDrop.PreviewDragOverEvent, fixture.Data, new Point(168, 50)).Effects);
        fixture.Wheel.ShowAtPointer(OperationMode.Tools);
        fixture.Wheel.ShowAtPointer(OperationMode.Actions);
        fixture.Wheel.ShowAtPointer(OperationMode.Convert);
        Assert.Equal(checks, fixture.Engine.Checks);
        Assert.Equal(1, fixture.Data.Reads);
        Assert.True(fixture.Wheel.IsVisible);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("expanded")]
    [InlineData("reordered")]
    [InlineData("shrunk")]
    public Task Changed_payload_cannot_replace_the_first_selection_or_complete_drop(string change) => desktop.RunAsync(async () =>
    {
        using var fixture = new WheelFixture();
        fixture.Enter();
        var changed = change switch
        {
            "expanded" => new[] { fixture.A, fixture.B, fixture.C },
            "reordered" => new[] { fixture.B, fixture.A },
            _ => new[] { fixture.A }
        };
        fixture.Data.Paths = changed;
        fixture.Raise(DragDrop.PreviewDragOverEvent, fixture.Data, new Point(168, 50));
        var dropped = new List<OperationDrop>();
        fixture.Wheel.OperationDropped += dropped.Add;
        Assert.Equal(DragDropEffects.None, fixture.Raise(DragDrop.PreviewDropEvent, fixture.Data, new Point(168, 50)).Effects);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Empty(dropped);
        Assert.False(fixture.Wheel.IsVisible);
        Assert.Equal(2, fixture.Data.Reads); // first entry + freshly read final drop only
    });

    [Fact]
    public Task Directory_actions_convert_actions_retains_ownership_and_cached_targets() => desktop.RunAsync(() =>
    {
        using var fixture = new WheelFixture();
        fixture.Data.Paths = [fixture.Root];
        fixture.Enter(OperationMode.Actions);
        Assert.NotEmpty(fixture.Sectors.Children.Cast<UIElement>());
        int checks = fixture.Engine.Checks;
        fixture.Wheel.ShowAtPointer(OperationMode.Convert);
        fixture.Raise(DragDrop.PreviewDragOverEvent, fixture.Data, new Point(168, 50));
        Assert.True(fixture.Wheel.IsVisible);
        Assert.Empty(fixture.Sectors.Children.Cast<UIElement>());
        fixture.Wheel.ShowAtPointer(OperationMode.Actions);
        Assert.NotEmpty(fixture.Sectors.Children.Cast<UIElement>());
        Assert.Equal(checks, fixture.Engine.Checks);
        Assert.Equal(1, fixture.Data.Reads);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Confirmed_drop_survives_global_release_and_is_dispatched_once_after_callback() => desktop.RunAsync(async () =>
    {
        using var fixture = new WheelFixture();
        fixture.Enter();
        fixture.Data.Paths[0] = fixture.C; // mutating the original array cannot mutate admission
        fixture.Data.Paths = [fixture.A, fixture.B];
        fixture.Raise(DragDrop.PreviewDragOverEvent, fixture.Data, new Point(168, 50));
        fixture.Wheel.ObserveGlobalRelease();
        await Dispatcher.Yield(DispatcherPriority.Background);
        Assert.True(fixture.Wheel.IsVisible);
        var dropped = new List<OperationDrop>();
        fixture.Wheel.OperationDropped += dropped.Add;
        Assert.Equal(DragDropEffects.Copy, fixture.Raise(DragDrop.PreviewDropEvent, fixture.Data, new Point(168, 50)).Effects);
        Assert.Empty(dropped);
        Assert.False(fixture.Wheel.IsVisible);
        fixture.Raise(DragDrop.PreviewDropEvent, fixture.Data, new Point(168, 50));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(new[] { fixture.A, fixture.B }, Assert.Single(dropped).Paths);
    });

    [Fact]
    public Task Source_disappearing_after_admission_is_rejected_on_drop() => desktop.RunAsync(() =>
    {
        using var fixture = new WheelFixture();
        fixture.Enter();
        fixture.Raise(DragDrop.PreviewDragOverEvent, fixture.Data, new Point(168, 50));
        File.Delete(fixture.A);
        Assert.Equal(DragDropEffects.None, fixture.Raise(DragDrop.PreviewDropEvent, fixture.Data, new Point(168, 50)).Effects);
        Assert.False(fixture.Wheel.IsVisible);
        return Task.CompletedTask;
    });

    private sealed class WheelFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "citrus-wheel-" + Guid.NewGuid().ToString("N"));
        public string A { get; }
        public string B { get; }
        public string C { get; }
        public CountingData Data { get; }
        public CountingEngine Engine { get; } = new();
        public WheelWindow Wheel { get; }
        public Canvas Surface => (Canvas)Wheel.FindName("Surface");
        public Canvas Sectors => (Canvas)Wheel.FindName("Sectors");
        public WheelFixture()
        {
            Directory.CreateDirectory(Root);
            A = Path.Combine(Root, "a.txt"); B = Path.Combine(Root, "b.txt"); C = Path.Combine(Root, "c.txt");
            foreach (string path in new[] { A, B, C }) File.WriteAllText(path, "physical file");
            Data = new CountingData([A, B]);
            Wheel = new WheelWindow(new OperationCatalog([Engine]));
        }
        public void Enter(OperationMode mode = OperationMode.Convert)
        {
            Wheel.ShowAtPointer(mode);
            Raise(DragDrop.PreviewDragEnterEvent, Data);
            Assert.True(Wheel.IsVisible);
            Assert.True(Sectors.Opacity > 0);
        }
        public DragEventArgs Raise(RoutedEvent routedEvent, IDataObject data, Point? point = null,
            DragDropEffects effects = DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link)
        {
            // WPF exposes no public constructor. Keep callback construction strictly in test code.
            var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [data, DragDropKeyStates.LeftMouseButton | DragDropKeyStates.ShiftKey | DragDropKeyStates.ControlKey,
                    effects, Surface, point ?? new Point(168, 162)], null)!;
            args.RoutedEvent = routedEvent;
            Surface.RaiseEvent(args);
            Assert.True(args.Handled);
            return args;
        }
        public void Dispose() { Wheel.Dismiss(); Wheel.Close(); Directory.Delete(Root, true); }
    }

    private sealed class CountingEngine : IConversionEngine
    {
        public int Checks { get; private set; }
        public IReadOnlyList<OperationDescriptor> Operations { get; } = Enum.GetValues<OperationMode>()
            .Select(mode => new OperationDescriptor(mode.ToString(), mode.ToString(), mode, null, false, false)).ToArray();
        public bool CanExecute(OperationRequest request) { Checks++; return true; }
        public Task<ConversionResult> ExecuteAsync(OperationRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Target tests must never execute file operations.");
    }

    private sealed class CountingData(string[] paths) : IDataObject
    {
        public string[] Paths { get; set; } = paths;
        public int Reads { get; private set; }
        public bool ThrowOnRead { get; set; }
        public object GetData(string format, bool autoConvert)
        {
            Reads++;
            if (ThrowOnRead) throw new InvalidOperationException("Unexpected data extraction on hover.");
            return Paths;
        }
        public object GetData(string format) => GetData(format, false);
        public object GetData(Type format) => GetData(DataFormats.FileDrop, false);
        public bool GetDataPresent(string format, bool autoConvert) => format == DataFormats.FileDrop;
        public bool GetDataPresent(string format) => GetDataPresent(format, false);
        public bool GetDataPresent(Type format) => format == typeof(string[]);
        public string[] GetFormats(bool autoConvert) => [DataFormats.FileDrop];
        public string[] GetFormats() => GetFormats(false);
        public void SetData(string format, object data, bool autoConvert) => throw new NotSupportedException();
        public void SetData(string format, object data) => throw new NotSupportedException();
        public void SetData(Type format, object data) => throw new NotSupportedException();
        public void SetData(object data) => throw new NotSupportedException();
    }
}
