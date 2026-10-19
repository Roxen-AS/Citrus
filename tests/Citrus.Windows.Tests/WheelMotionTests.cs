using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Citrus.Core;
using Xunit.Abstractions;
using DataObject = System.Windows.DataObject;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using Point = System.Windows.Point;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class WheelMotionTests(DesktopFixture desktop, ITestOutputHelper output)
{
    [Fact]
    public Task Cancel_hides_both_hwnds_immediately_without_opacity_tail() => desktop.RunAsync(async () =>
    {
        output.WriteLine($"Native motion prerequisites: ClientAreaAnimation={SystemParameters.ClientAreaAnimation}; HighContrast={SystemParameters.HighContrast}");
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        await File.WriteAllTextAsync(path, "physical payload");
        var wheel = new WheelWindow(new OperationCatalog([new MotionEngine()]));
        wheel.ApplySettings(new CitrusSettings { ReduceMotion = false, SoundEnabled = false });
        var surface = (Canvas)wheel.FindName("Surface");
        var data = new DataObject(DataFormats.FileDrop, new[] { path });
        int drops = 0;
        wheel.OperationDropped += _ => drops++;
        try
        {
            wheel.ShowAtPointer(OperationMode.Convert);
            Raise(DragDrop.PreviewDragEnterEvent);
            for (int i = 0; i < 500 && ((TextBlock)wheel.FindName("Hint")).Text.StartsWith("Preparing"); i++) await Task.Delay(10);
            Assert.False(((TextBlock)wheel.FindName("Hint")).Text.StartsWith("Preparing"));
            await Task.Delay(200); // Let the genuine opening presentation finish first.
            Assert.True(wheel.IsVisible);
            wheel.Dismiss();
            Assert.False(wheel.IsVisible);
            Assert.False(wheel.MaterialPresentationPending);
            Assert.Equal(0, wheel.CompanionHandle);
            wheel.ApplySettings(new CitrusSettings { ReduceMotion = true, SoundEnabled = false });
            await Task.Delay(200);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(wheel.IsVisible);
            Assert.Equal(DragDropEffects.None, Raise(DragDrop.PreviewDropEvent).Effects);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(0, drops);
        }
        finally { wheel.Dismiss(); wheel.Close(); File.Delete(path); }

        DragEventArgs Raise(RoutedEvent routedEvent)
        {
            var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Copy, surface, new Point(184, 62)], null)!;
            args.RoutedEvent = routedEvent;
            surface.RaiseEvent(args);
            return args;
        }
    });

    private sealed class MotionEngine : IConversionEngine
    {
        public IReadOnlyList<OperationDescriptor> Operations { get; } = [new("test", "Test", OperationMode.Convert, null, false, false)];
        public bool CanExecute(OperationRequest request) => true;
        public Task<ConversionResult> ExecuteAsync(OperationRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Motion tests must never execute an operation.");
    }
}

/// <summary>Discovery records a real capability skip; no OS preferences or product motion flags are overridden.</summary>
public sealed class NativeAnimationFactAttribute : FactAttribute
{
    public NativeAnimationFactAttribute()
    {
        if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
            Skip = $"Native animation regression unverified: ClientAreaAnimation={SystemParameters.ClientAreaAnimation}, HighContrast={SystemParameters.HighContrast}. Requires an animation-capable Windows desktop; no OS preferences changed.";
    }
}
