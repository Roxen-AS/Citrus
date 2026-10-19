using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Citrus.Core.Presentation;
using Citrus.Windows.Presentation;
using Application = System.Windows.Application;

namespace Citrus.Windows.Tests;

/// <summary>Real production surface/session teardown. Routed admission is synthetic;
/// this class does not claim a physical Explorer drag or optical companion acceptance.</summary>
[Collection("Desktop")]
public sealed class GlassResourceLifetimeTests(DesktopFixture desktop)
{
    [Fact]
    public Task Confirm_cancel_reopen_sequence_has_no_bare_circle_or_orphan() => desktop.RunAsync(async () =>
    {
        using var scene = new GlassOwnedScene();
        scene.Show();
        Assert.Equal(1, ((Canvas)scene.Wheel.FindName("Presentation")).Opacity);
        Assert.Equal(BackdropState.Solid, scene.Wheel.MaterialState);
        Assert.True(scene.Wheel.MaterialPresentationPending);
        scene.Wheel.Dismiss();
        Assert.False(scene.Wheel.IsVisible);
        Assert.False(scene.Wheel.MaterialPresentationPending);
        Assert.Equal(0, scene.Wheel.CompanionHandle);
        scene.ConfirmWheel();
        await scene.WaitForPresentationAsync();
        await Drain(scene.Wheel);
        Assert.True(scene.Wheel.IsVisible);
        Assert.Equal(1, ((Canvas)scene.Wheel.FindName("Presentation")).Opacity);
        Assert.Equal(BackdropState.Solid, scene.Wheel.MaterialState);
        Assert.Equal(0, scene.Wheel.CompanionHandle); // Actual production false gate, not a hidden test override.
        var handle = scene.WheelHandle;
        scene.Wheel.Close();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.False(BackdropNativeMethods.IsWindow(handle));
        Assert.False(scene.Wheel.MaterialPresentationPending);
        Assert.Equal(0, scene.Wheel.CompanionHandle);
    });

    [Theory]
    [InlineData("HighContrast")]
    [InlineData("UserReduceTransparency")]
    public Task Policy_fallback_destroys_native_helpers_and_theme_reopen_does_not_revive_old_generation(string reason) => desktop.RunAsync(async () =>
    {
        using var scene = new GlassOwnedScene(); scene.Show();
        await scene.WaitForPresentationAsync();
        using var controller = new WheelBackdropController(nativeCompanionValidated: true);
        controller.Attach(scene.WheelHandle);
        var first = controller.Synchronize(1, scene.Bounds, scene.DpiScale, true, BackdropControllerTests.Eligible, false);
        nint helper = controller.CompanionHandle, owner = controller.LifetimeOwnerHandle;
        var fallback = controller.Synchronize(1, scene.Bounds, scene.DpiScale, true,
            BackdropControllerTests.Eligible with { RequestNativeAcrylic = false, Reason = reason }, false);
        Assert.Equal(BackdropState.Solid, fallback.State);
        Assert.Equal(reason, fallback.Reason);
        Assert.False(BackdropNativeMethods.IsWindow(helper));
        Assert.False(BackdropNativeMethods.IsWindow(owner));
        // Reduced motion alone remains independent of backdrop eligibility. This is a test decision,
        // not a system accessibility write; actual compositor acceptance remains separate.
        var reopened = controller.Synchronize(2, scene.Bounds, scene.DpiScale, true,
            BackdropControllerTests.Eligible with { Animate = false }, true);
        controller.Hide(1);
        Assert.Equal(reopened.IsNative, BackdropNativeMethods.IsWindowVisible(controller.CompanionHandle));
        nint newHelper = controller.CompanionHandle, newOwner = controller.LifetimeOwnerHandle;
        scene.Wheel.Close(); // SourceDisposed must release the controller even before explicit Dispose.
        Assert.False(controller.IsAttached);
        Assert.Equal(0, controller.CompanionHandle);
        Assert.Equal(0, controller.LifetimeOwnerHandle);
        Assert.False(BackdropNativeMethods.IsWindow(newHelper));
        Assert.False(BackdropNativeMethods.IsWindow(newOwner));
        controller.Dispose();
        GlassCompositorTests.Write(Artifact(reason + ".json"), new
        {
            First = first, Reopened = reopened, RequestedFallback = reason,
            HelperDestroyed = !BackdropNativeMethods.IsWindow(newHelper), OwnerDestroyed = !BackdropNativeMethods.IsWindow(newOwner),
            Scope = "Native configuration/lifetime, no screen capture or optical acceptance"
        });
    });

    [Fact]
    public Task Service_teardown_clears_pending_callbacks_and_retained_spring_owners() => desktop.RunAsync(async () =>
    {
        using var theme = new ThemeManager(Application.Current);
        using var services = new WindowPresentationService(Application.Current, theme, () => new() { ReduceTransparency = true });
        using var scene = new GlassOwnedScene();
        var settings = new SettingsWindow(new(), _ => Task.CompletedTask) { ShowActivated = false };
        try
        {
            Assert.Null(PressFeedback.GetAnimator(settings));
            services.Attach(scene.Wheel, null);
            services.Attach(settings, (MaterialSurface)settings.FindName("Chrome"));
            scene.Show(); settings.Show();
            var animator = services.Animator;
            var content = (FrameworkElement)settings.Content;
            animator.SetImmediate(content, "lifetime", 0, value => content.Opacity = value);
            animator.Retarget(content, "lifetime", 1, SpringParameters.Default, value => content.Opacity = value);
            // Systems with disabled animation settle immediately; never count that as an active-frame test.
            bool hadRenderingSubscription = animator.IsRenderingSubscribed;
            services.Dispose(); services.Dispose();
            Assert.Equal(0, animator.ActiveChannelCount);
            Assert.Equal(0, animator.RetainedChannelCount);
            Assert.False(animator.IsRenderingSubscribed);
            Assert.Null(PressFeedback.GetAnimator(scene.Wheel));
            Assert.Null(PressFeedback.GetAnimator(settings));
            Assert.False(scene.Wheel.MaterialPresentationPending);
            Assert.Equal(0, scene.Wheel.CompanionHandle);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(scene.Wheel.MaterialPresentationPending);
            GlassCompositorTests.Write(Artifact("service-teardown.json"), new
            {
                HadRenderingSubscription = hadRenderingSubscription,
                animator.ActiveChannelCount, animator.RetainedChannelCount, animator.IsRenderingSubscribed,
                Scope = "Direct production WindowPresentationService.Dispose; actual App.QuitAsync is separately exercised by resident-process tests"
            });
        }
        finally { settings.Close(); }
    });

    [Fact]
    public async Task After_quit_no_helper_hwnds_or_rendering_subscriptions()
    {
        using var files = new NativeFiles();
        files.File("glass.txt", "owned quit payload");
        var start = new System.Diagnostics.ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { typeof(LifecycleHost).Assembly.Location, "--resident-test", Guid.NewGuid().ToString("N"), files.Root, "--background", "--glass-lifetime" })
            start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new System.IO.IOException("Could not start owned resident.");
        var errors = process.StandardError.ReadToEndAsync();
        async Task<string> Read() => await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(45))
            ?? throw new System.IO.IOException("Owned resident exited before diagnostics.");
        try
        {
            Assert.StartsWith("READY:", await Read());
            await process.StandardInput.WriteLineAsync("glass-lifetime"); await process.StandardInput.FlushAsync();
            var beforeLine = await Read();
            Assert.StartsWith("GLASS_READY:", beforeLine);
            using var before = System.Text.Json.JsonDocument.Parse(beforeLine["GLASS_READY:".Length..]);
            Assert.True(before.RootElement.GetProperty("IsVisible").GetBoolean());
            Assert.True(before.RootElement.GetProperty("MaterialPresentationPending").GetBoolean());
            await process.StandardInput.WriteLineAsync("quit"); await process.StandardInput.FlushAsync();
            var output = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(0, process.ExitCode);
            Assert.Equal("", await errors);
            string remaining = await output;
            Assert.Contains("DISPOSED:True", remaining);
            string line = remaining.Split('\n').Single(value => value.StartsWith("GLASS_DISPOSED:", StringComparison.Ordinal));
            using var after = System.Text.Json.JsonDocument.Parse(line["GLASS_DISPOSED:".Length..]);
            foreach (var property in new[] { "WheelHwndAlive", "MaterialPresentationPending", "AnimatorAttached", "IsRenderingSubscribed" })
                Assert.False(after.RootElement.GetProperty(property).GetBoolean(), property);
            foreach (var property in new[] { "Companion", "ActiveChannelCount", "RetainedChannelCount" })
                Assert.Equal(0, after.RootElement.GetProperty(property).GetInt64());
            GlassCompositorTests.Write(Artifact("actual-quit.json"), new { Before = before.RootElement, After = after.RootElement });
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }

    [Fact]
    public Task Main_initial_fit_still_respects_actual_workarea() => desktop.RunAsync(() =>
    {
        using var home = new MainWindow((App)Application.Current) { ShowActivated = false };
        try
        {
            NativeMethods.GetCursorPos(out var pointer);
            var work = NativeMethods.WorkArea(pointer);
            home.Show(); home.UpdateLayout();
            var handle = new WindowInteropHelper(home).Handle;
            Assert.True(BackdropNativeMethods.GetWindowRect(handle, out var native));
            var bounds = native.ToScreenRect();
            Assert.True(bounds.Left >= work.Left && bounds.Top >= work.Top);
            Assert.True(bounds.Right <= work.Right && bounds.Bottom <= work.Bottom);
            Assert.Equal(0, BackdropNativeMethods.GetWindowLongPtr(handle, -20).ToInt64() & BackdropNativeMethods.WsExLayered);
            GlassCompositorTests.Write(Artifact("main-workarea.json"), new
            {
                ActualBounds = bounds, ActualWorkArea = work, Dpi = BackdropNativeMethods.GetDpiForWindow(handle),
                PhysicalMonitorTransitionExercised = false, RasterScale = false
            });
        }
        finally { home.Dispose(); home.Close(); }
        return Task.CompletedTask;
    });
    private static string Artifact(string name) => System.IO.Path.Combine(
        Environment.GetEnvironmentVariable("CITRUS_GLASS_ARTIFACTS") ?? System.IO.Path.Combine(AppContext.BaseDirectory, "TestResults", "glass-native"), "lifetime", name);
    private static async Task Drain(WheelWindow wheel)
    {
        for (int i = 0; i < 100 && wheel.MaterialPresentationPending; i++) await Task.Delay(10);
        Assert.False(wheel.MaterialPresentationPending);
    }
}
