using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Citrus.Core;
using Citrus.Windows.Presentation;
using DataObject = System.Windows.DataObject;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class BootstrapWheelVisibilityTests(DesktopFixture desktop)
{
    [Fact]
    public Task First_visible_wheel_has_a_blurred_backing_even_while_live_capture_is_not_ready() => desktop.RunAsync(async () =>
    {
        using var gate = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        var (wheel, path) = Create(gate, entered);
        try
        {
            Enter(wheel, path);
            await Until(() => entered.IsSet);
            var presentation = (Canvas)wheel.FindName("Presentation");
            var image = (System.Windows.Controls.Image)wheel.FindName("GlassBody");
            Assert.Equal(0, presentation.Opacity); Assert.Null(image.Source);
            gate.Set();
            await Until(() => image.Source is not null && presentation.Opacity == 1);
            Assert.Equal(BackdropState.LiveRefractiveGlass, wheel.MaterialState);
        }
        finally { gate.Set(); wheel.Dismiss(); wheel.Close(); await wheel.GlassCompletion.WaitAsync(TimeSpan.FromSeconds(5)); File.Delete(path); }
    });

    [Fact]
    public Task Releasing_before_the_first_visible_backing_cannot_execute_an_unseen_action() => desktop.RunAsync(async () =>
    {
        using var gate = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        var (wheel, path) = Create(gate, entered);
        var dropped = new List<OperationDrop>(); wheel.OperationDropped += dropped.Add;
        try
        {
            Enter(wheel, path);
            await Until(() => entered.IsSet && ((Canvas)wheel.FindName("Sectors")).Children.Count == 1);
            Assert.Equal(0, ((Canvas)wheel.FindName("Presentation")).Opacity);
            Raise(wheel, path, DragDrop.PreviewDragOverEvent, new(184, 184));
            Raise(wheel, path, DragDrop.PreviewDragOverEvent, new(184, 62));
            var drop = Raise(wheel, path, DragDrop.PreviewDropEvent, new(184, 62));
            Assert.Equal(DragDropEffects.None, drop.Effects);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Empty(dropped);
        }
        finally { gate.Set(); wheel.Dismiss(); wheel.Close(); await wheel.GlassCompletion.WaitAsync(TimeSpan.FromSeconds(5)); File.Delete(path); }
    });

    private static (WheelWindow Wheel, string Path) Create(ManualResetEventSlim gate, ManualResetEventSlim entered)
    {
        var constructor = typeof(WheelWindow).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .SingleOrDefault(candidate => candidate.GetParameters().Any(parameter => parameter.Name == "bootstrap"));
        Assert.NotNull(constructor);
        Func<GlassScene, int, int, CancellationToken, byte[]?> bootstrap = (scene, _, _, _) =>
        { entered.Set(); gate.Wait(TimeSpan.FromSeconds(5)); return Enumerable.Repeat((byte)135, scene.PixelWidth * scene.PixelHeight * 4).ToArray(); };
        var wheel = (WheelWindow)constructor.Invoke([new OperationCatalog([new Engine()]), null, null, null,
            (Func<GlassScene, int, int, ILiveGlassSession?>)((scene, _, _) => new Session(scene)), null, null, bootstrap, null]);
        wheel.ApplySettings(new() { SoundEnabled = false });
        wheel.ApplyMaterialPolicy(new(true, false, "Owned bootstrap fixture"), false, 1);
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".txt");
        File.WriteAllText(path, "owned bootstrap input");
        return (wheel, path);
    }
    private static void Enter(WheelWindow wheel, string path)
    { wheel.ShowAtPointer(OperationMode.Convert); Raise(wheel, path, DragDrop.PreviewDragEnterEvent, new(184, 184)); }
    private static DragEventArgs Raise(WheelWindow wheel, string path, RoutedEvent routed, System.Windows.Point point)
    {
        var surface = (Canvas)wheel.FindName("Surface");
        var data = new DataObject(DataFormats.FileDrop, new[] { path });
        var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Copy, surface, point], null)!;
        args.RoutedEvent = routed; surface.RaiseEvent(args); return args;
    }
    private static async Task Until(Func<bool> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds < 5000) await Task.Delay(10);
        Assert.True(condition());
    }
    private sealed class Session(GlassScene scene) : ILiveGlassSession
    { public GlassScene Scene => scene; public bool TryRenderFrame(GlassFrameState state, byte[] output) => false; public void Dispose() { } }
    private sealed class Engine : IConversionEngine
    {
        public IReadOnlyList<OperationDescriptor> Operations { get; } = [new("owned.visible", "Visible action", OperationMode.Convert, ".png", false, false)];
        public bool CanExecute(OperationRequest request) => true;
        public Task<ConversionResult> ExecuteAsync(OperationRequest request, IProgress<ConversionProgress>? progress, CancellationToken token) => throw new NotSupportedException();
    }
}
