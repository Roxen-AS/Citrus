using Citrus.Windows.Presentation;
using System.Windows.Threading;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class WheelBackdropLifecycleTests(DesktopFixture desktop)
{
    [Fact]
    public Task Companion_is_disabled_noactivate_nonlayered_and_not_owned_by_wheel() => desktop.RunAsync(async () =>
    {
        using var scene = new GlassOwnedScene();
        scene.Show();
        await scene.WaitForPresentationAsync();
        using var companion = new NativeBackdropWindow();
        var hwnd = companion.Handle;
        Assert.NotEqual(scene.WheelHandle, BackdropNativeMethods.GetWindow(hwnd, 4));
        Assert.Equal(companion.Owner, BackdropNativeMethods.GetWindow(hwnd, 4));
        Assert.NotEqual(0L, BackdropNativeMethods.GetWindowLongPtr(hwnd, -16).ToInt64() & BackdropNativeMethods.WsDisabled);
        long styles = BackdropNativeMethods.GetWindowLongPtr(hwnd, -20).ToInt64();
        Assert.NotEqual(0L, styles & BackdropNativeMethods.WsExNoActivate);
        Assert.Equal(0L, styles & BackdropNativeMethods.WsExLayered);
        Assert.Equal(scene.ForegroundBefore, BackdropNativeMethods.GetForegroundWindow());
        Assert.Equal(nint.Zero, BackdropNativeMethods.GetProp(hwnd, "OleDropTargetInterface"));
        Assert.False(BackdropNativeMethods.IsWindowVisible(companion.Owner));
    });

    [Fact]
    public Task WindowFromPoint_still_resolves_real_wheel_inside_entire_clip() => desktop.RunAsync(async () =>
    {
        using var scene = new GlassOwnedScene(); scene.Show(activateOwnedBackground: true);
        GlassCompositorTests.Write(System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("CITRUS_GLASS_ARTIFACTS") ?? System.IO.Path.Combine(AppContext.BaseDirectory, "TestResults", "glass-native"), "clip-fixture-activation.json"),
            new { scene.OriginalForeground, scene.BackgroundActivationAttempted, scene.BackgroundActivationReturned,
                scene.BackgroundActivationSucceeded, scene.BaselineForeground });
        await scene.WaitForPresentationAsync();
        using var controller = new WheelBackdropController(true);
        controller.Attach(scene.WheelHandle);
        controller.Synchronize(1, scene.Bounds, scene.DpiScale, true, BackdropControllerTests.Eligible, false);
        scene.AssertWheelOwnsClip(controller.CompanionHandle);
        Assert.True(scene.BackgroundActivationAttempted);
        Assert.Equal(scene.ForegroundBefore, BackdropNativeMethods.GetForegroundWindow());
    });

    [Fact]
    public Task Unconfirmed_has_no_visible_companion() => desktop.RunAsync(() =>
    {
        using var scene = new GlassOwnedScene(); scene.Show();
        using var controller = new WheelBackdropController(true); controller.Attach(scene.WheelHandle);
        controller.Synchronize(1, scene.Bounds, scene.DpiScale, false, BackdropControllerTests.Eligible, false);
        Assert.Equal(nint.Zero, controller.CompanionHandle);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Stale_generation_cannot_hide_new_pair() => desktop.RunAsync(async () =>
    {
        using var scene = new GlassOwnedScene(); scene.Show();
        await scene.WaitForPresentationAsync();
        using var controller = new WheelBackdropController(true); controller.Attach(scene.WheelHandle);
        controller.Synchronize(1, scene.Bounds, scene.DpiScale, true, BackdropControllerTests.Eligible, false);
        controller.Hide(1);
        var result = controller.Synchronize(2, scene.Bounds, scene.DpiScale, true, BackdropControllerTests.Eligible, false);
        controller.Hide(1);
        Assert.Equal(result.IsNative, BackdropNativeMethods.IsWindowVisible(controller.CompanionHandle));
        controller.Synchronize(1, scene.Bounds, scene.DpiScale, false, BackdropControllerTests.Eligible, false);
        Assert.Equal(result.IsNative, BackdropNativeMethods.IsWindowVisible(controller.CompanionHandle));
        controller.Hide(2);
        Assert.False(controller.Synchronize(2, scene.Bounds, scene.DpiScale, true, BackdropControllerTests.Eligible, false).IsNative);
    });

    [Fact]
    public Task Policy_switch_and_dispose_remove_helpers_and_hooks() => desktop.RunAsync(async () =>
    {
        using var scene = new GlassOwnedScene(); scene.Show();
        await scene.WaitForPresentationAsync();
        var controller = new WheelBackdropController(true); controller.Attach(scene.WheelHandle);
        controller.Synchronize(1, scene.Bounds, scene.DpiScale, true, BackdropControllerTests.Eligible, false);
        var old = controller.CompanionHandle;
        controller.Synchronize(1, scene.Bounds, scene.DpiScale, true, BackdropControllerTests.Eligible with { RequestNativeAcrylic = false, Reason = "HighContrast" }, false);
        Assert.False(BackdropNativeMethods.IsWindow(old));
        controller.Dispose(); controller.Dispose();
        Assert.False(controller.IsAttached);
        Assert.Equal(nint.Zero, controller.LifetimeOwnerHandle);
    });

    [Fact]
    public Task Unvalidated_companion_remains_solid_even_when_dwm_would_accept_it() => desktop.RunAsync(() =>
    {
        using var scene = new GlassOwnedScene(); scene.Show();
        using var controller = new WheelBackdropController(); controller.Attach(scene.WheelHandle);
        var result = controller.Synchronize(1, scene.Bounds, scene.DpiScale, true, BackdropControllerTests.Eligible, false);
        Assert.False(result.IsNative);
        Assert.Equal("CompanionNotValidated", result.Reason);
        Assert.Equal(nint.Zero, controller.CompanionHandle);
        return Task.CompletedTask;
    });
}
