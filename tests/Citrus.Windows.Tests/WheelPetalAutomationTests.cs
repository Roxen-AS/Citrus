using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;
using Citrus.Core;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class WheelPetalAutomationTests(DesktopFixture desktop)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Actual_control_tree_preserves_hidden_and_shortened_operation_names(bool gif) => desktop.RunAsync(async () =>
    {
        var operation = gif
            ? new OperationDescriptor("image.convert.gif", "GIF (still image)", OperationMode.Convert, "gif", false, false)
            : new OperationDescriptor("image.resize", "Resize image to exact dimensions", OperationMode.Tools, null, false, false);
        var petal = new WheelPetal(operation, 0, 9);
        petal.ApplyTextScale(2);
        var label = petal.Children.OfType<TextBlock>().Single();
        if (gif) { Assert.Equal("GIF", label.Text); Assert.Equal(Visibility.Visible, label.Visibility); }
        else Assert.Equal(Visibility.Hidden, label.Visibility);

        var window = new Window { Content = petal, Width = 400, Height = 440, ShowActivated = false };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(petal);
            Assert.NotNull(peer);
            Assert.Equal(operation.Label, peer.GetName());
            Assert.Equal(AutomationControlType.Text, peer.GetAutomationControlType());
            Assert.True(peer.IsControlElement()); Assert.True(peer.IsContentElement());
            Assert.False(peer.IsKeyboardFocusable());
            Assert.False(petal.Focusable); Assert.False(petal.IsHitTestVisible);
            foreach (var pattern in Enum.GetValues<PatternInterface>()) Assert.Null(peer.GetPattern(pattern));

            // Inspect the actual Window peer's descendants, not merely the attached Name property.
            var root = FrameworkElementAutomationPeer.CreatePeerForElement(window);
            Assert.NotNull(root);
            Assert.Contains(ControlDescendants(root), child => ReferenceEquals(child, peer) && child.GetName() == operation.Label);
            Assert.Null(peer.GetChildren()); // Do not expose a second, shortened visual label as a sibling semantic target.
            petal.SetSelected(true, false);
            Assert.Equal(operation.Label, peer.GetName());
        }
        finally { window.Close(); }
    });

    private static IEnumerable<AutomationPeer> ControlDescendants(AutomationPeer peer)
    {
        foreach (var child in peer.GetChildren() ?? [])
        {
            if (child.IsControlElement()) yield return child;
            foreach (var descendant in ControlDescendants(child)) yield return descendant;
        }
    }
}
