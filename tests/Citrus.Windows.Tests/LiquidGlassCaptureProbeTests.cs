using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Citrus.Core;
using Citrus.Windows.Presentation;
using Xunit.Abstractions;

namespace Citrus.Windows.Tests;

public sealed class NativeLiquidGlassFactAttribute : FactAttribute
{
    public NativeLiquidGlassFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CITRUS_RUN_LIQUID_GLASS_TESTS") != "1")
            Skip = "Opt-in owned-scene desktop refraction capability proof.";
    }
}

[Collection("Desktop")]
public sealed class LiquidGlassCaptureProbeTests(DesktopFixture desktop, ITestOutputHelper output)
{
    [NativeLiquidGlassFact]
    public Task Layered_wheel_capture_exclusion_reveals_changing_owned_background_without_feedback() => desktop.RunAsync(async () =>
    {
        NativeMethods.GetCursorPos(out var pointer);
        var work = NativeMethods.WorkArea(pointer);
        double scale = NativeMethods.MonitorScale(pointer);
        var target = new WheelWindow(new OperationCatalog([])) { Left = work.Left / scale + 30, Top = work.Top / scale + 30 };
        using var scene = new GlassOwnedScene(target);
        scene.Show(activateOwnedBackground:true);
        // Seed the real placement session before a mixed-monitor WM_DPICHANGED can
        // run. A generically shown WheelWindow otherwise has its default (0,0) anchor.
        target.ShowAtPointer(OperationMode.Convert);
        var surface = (System.Windows.Controls.Canvas)target.FindName("Surface");
        var data = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { scene.SamplePath });
        var admission = (System.Windows.DragEventArgs)Activator.CreateInstance(typeof(System.Windows.DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [data, System.Windows.DragDropKeyStates.LeftMouseButton, System.Windows.DragDropEffects.Copy, surface, new System.Windows.Point(184,184)], null)!;
        admission.RoutedEvent = System.Windows.DragDrop.PreviewDragEnterEvent;
        surface.RaiseEvent(admission);
        target.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        nint hwnd = scene.CaptureTargetHandle;
        Assert.True((BackdropNativeMethods.GetWindowLongPtr(hwnd,-20).ToInt64() & BackdropNativeMethods.WsExLayered) != 0);
        Assert.True(GetWindowDisplayAffinity(hwnd,out uint original), "Display affinity readback failed: " + Marshal.GetLastWin32Error());
        using var exclusion = CaptureExclusion.TryAcquire([hwnd, target.DragCaptureHandle]);
        Assert.NotNull(exclusion);
        try
        {
            Assert.True(GetWindowDisplayAffinity(hwnd,out uint active)); Assert.Equal(0x11u,active);
            string root = Path.Combine(AppContext.BaseDirectory,"TestResults","liquid-glass-capability",
                DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N")[..8]);
            output.WriteLine("Owned capability evidence: " + root);
            var preflight = GlassCompositorCapture.RequireOwned(scene,0,Path.Combine(root,"dda-create.preflight.json"));
            var crop = preflight.Crop;
            int size = (int)Math.Min(crop.Width,crop.Height);
            using var source = await Task.Run(() => DesktopGlassSource.TryCreate((int)crop.Left,(int)crop.Top,size));
            Assert.NotNull(source);
            // A graphics-driver overlay may appear when D3D starts. Place only our own opaque
            // fixture and wheel above it once, then require the unchanged ownership guard per read.
            Assert.True(NativeMethods.SetWindowPos(scene.BackgroundHandle,new nint(-1),0,0,0,0,0x13));
            Assert.True(NativeMethods.SetWindowPos(hwnd,new nint(-1),0,0,0,0,0x13));
            scene.SetBroadPattern(false); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            byte[] first = await ReadOwnedFrameAsync(scene,source!,root,"a");
            scene.SetBroadPattern(true); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            byte[] second = await ReadOwnedFrameAsync(scene,source!,root,"b");
            double difference = 0; int samples = 0;
            for (int y = (int)(120*scene.DpiScale); y < (int)(240*scene.DpiScale); y+=4)
            for (int x = (int)(120*scene.DpiScale); x < (int)(240*scene.DpiScale); x+=4)
            {
                int offset = (y*size+x)*4;
                difference += (Math.Abs(first[offset]-second[offset])+Math.Abs(first[offset+1]-second[offset+1])+Math.Abs(first[offset+2]-second[offset+2]))/3d;
                samples++;
            }
            Assert.True(difference/samples > 210,$"Excluded wheel must reveal changing owned background, not a black hole or its own pixels: {difference/samples:0.00}.");
            output.WriteLine($"Actual layered WPF exclusion + D3D11 GPU ROI capture revealed owned background. Mean inversion difference {difference/samples:0.00}/255; Windows {Environment.OSVersion.Version.Build}; CPU readback {first.Length} bytes per frame. Desktop frame pixels were not saved.");
        }
        finally { exclusion?.Dispose(); Assert.True(GetWindowDisplayAffinity(hwnd,out uint restored)); Assert.Equal(original,restored); }
    });

    private static async Task<byte[]> ReadOwnedFrameAsync(GlassOwnedScene scene,DesktopGlassSource source,string root,string phase)
    {
        int size = (int)Math.Min(scene.Bounds.Width,scene.Bounds.Height);
        byte[] pixels = new byte[size*size*4];
        bool acquired = false;
        // Drain stale frames after DWM has presented the new owned pattern. Guard every CPU read.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            await Task.Delay(75);
            GlassCompositorCapture.RequireOwned(scene,0,Path.Combine(root,$"dda-{phase}.preflight.json"));
            Assert.True(BackdropNativeMethods.DwmFlush() >= 0);
            acquired |= await Task.Run(() => source.TryReadFrame(pixels));
        }
        Assert.True(acquired,"No fresh Desktop Duplication frame was available.");
        return pixels;
    }

    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint window,uint affinity);
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(nint window,out uint affinity);
}
