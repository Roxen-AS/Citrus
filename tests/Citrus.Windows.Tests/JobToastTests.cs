using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class JobToastTests(DesktopFixture desktop)
{
    [Fact]
    public Task Progress_and_completion_do_not_activate_or_dispatch_work() => desktop.RunAsync(async () =>
    {
        using var host = new OwnedForegroundHost();
        nint foreground = await host.EstablishAsync();
        int views = 0, reveals = 0;
        bool allowed = true;
        using var toast = new JobToastWindow(() => views++, _ => { reveals++; return Task.CompletedTask; }, () => allowed);
        try
        {
            toast.Update("Working", true);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            nint handle = new WindowInteropHelper(toast).Handle;
            Assert.NotEqual(0, NativeMethods.GetWindowLongPtr(handle, -20) & 0x08000000);
            Assert.False(toast.ShowActivated);
            Assert.Equal(foreground, GetForegroundWindow());
            toast.Update("Ready", false, ["committed-output.png"]);
            Assert.Equal(foreground, GetForegroundWindow());
            Assert.Equal(0, views); Assert.Equal(0, reveals);
            allowed = false; toast.Update("Another job", true);
            Assert.False(toast.IsVisible); // An active wheel blocks notification presentation.
        }
        finally { toast.Close(); }
    });
    [Fact]
    public Task A_visible_toast_reanchors_when_a_long_error_increases_its_height() => desktop.RunAsync(async () =>
    {
        using var host = new OwnedForegroundHost();
        nint foreground = await host.EstablishAsync();
        using var toast = new JobToastWindow(() => { }, _ => Task.CompletedTask, () => true);
        try
        {
            toast.Update("Checking inputs…", true);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(foreground, GetForegroundWindow());
            nint handle = new WindowInteropHelper(toast).Handle;
            Assert.True(GetWindowRect(handle, out var before));
            toast.Update(string.Join(" ", Enumerable.Repeat("The selected file could not be read. View jobs to recover your work.", 8)), false, ["output.png"]);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(GetWindowRect(handle, out var after));
            Assert.True(after.Bottom - after.Top > before.Bottom - before.Top);
            var work = NativeMethods.WorkArea(new NativeMethods.Point { X = (before.Left + before.Right) / 2, Y = (before.Top + before.Bottom) / 2 });
            Assert.InRange(after.Left, work.Left, work.Right);
            Assert.InRange(after.Top, work.Top, work.Bottom);
            Assert.InRange(after.Right, work.Left, work.Right);
            Assert.InRange(after.Bottom, work.Top, work.Bottom);
            var reveal = (System.Windows.Controls.Button)toast.FindName("RevealButton");
            var actionBottom = reveal.PointToScreen(new System.Windows.Point(reveal.ActualWidth, reveal.ActualHeight));
            Assert.InRange(actionBottom.Y, work.Top, work.Bottom);
            Assert.Equal(foreground, GetForegroundWindow());
        }
        finally { toast.Close(); }
    });
    private sealed class OwnedForegroundHost : IDisposable
    {
        private readonly System.Windows.Window window = new()
        {
            Title = "Citrus toast nonactivation fixture", Width = 320, Height = 160,
            ShowActivated = false, ShowInTaskbar = false,
            Content = new System.Windows.Controls.TextBlock { Text = "Owned foreground for toast assertions" }
        };
        public async Task<nint> EstablishAsync()
        {
            window.Show();
            bool activated = window.Activate(); // One normal owned-window attempt; never force or retry.
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            nint handle = new WindowInteropHelper(window).Handle;
            Assert.NotEqual(0, handle);
            Assert.True(activated, "The owned foreground fixture must activate before testing a nonactivating toast.");
            Assert.Equal(handle, GetForegroundWindow());
            return handle;
        }
        public void Dispose() => window.Close();
    }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeMethods.Rect bounds);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
