namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class QuietCompletionTests(DesktopFixture desktop)
{
    [Fact]
    public Task Successful_status_expires_while_a_new_pending_update_cancels_expiration() => desktop.RunAsync(async () =>
    {
        using var toast = new JobToastWindow(() => { }, _ => Task.CompletedTask, () => false);
        bool closed = false; toast.Closed += (_, _) => closed = true;
        toast.Update("Ready", false, ["output.png"], autoDismiss: true);
        await Task.Delay(400);
        toast.Update("Working", true);
        await Task.Delay(1900);
        Assert.False(closed);
        toast.Update("Ready", false, ["output.png"], autoDismiss: true);
        for (int i = 0; i < 35 && !closed; i++) await Task.Delay(100);
        Assert.True(closed);
    });
}
