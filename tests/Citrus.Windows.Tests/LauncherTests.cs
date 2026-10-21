using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class LauncherTests(DesktopFixture desktop)
{
    [Fact]
    public Task Launcher_opens_workspace_on_drop_and_preserves_selection_across_navigation() => desktop.RunAsync(async () =>
    {
        string input = Path.GetTempFileName();
        using var window = new MainWindow((App)System.Windows.Application.Current, startInLauncher: true) { ShowActivated = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(window.IsLauncher);
            Assert.InRange(window.Width, 380, 480);
            window.AddFiles([input]);
            Assert.False(window.IsLauncher);
            var list = (System.Windows.Controls.ListBox)window.FindName("SelectedFiles");
            Assert.Single(list.Items);
            window.ShowLauncher(); window.ShowWorkspace();
            Assert.Single(list.Items);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("WorkspaceRoot")).Visibility);
        }
        finally { window.Dispose(); File.Delete(input); }
    });
}
