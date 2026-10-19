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
        nint foreground = GetForegroundWindow();
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
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
