using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Application = System.Windows.Application;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class ExpanderHeaderPresentationTests(DesktopFixture desktop)
{
    [Theory]
    [InlineData(1d, false)]
    [InlineData(2d, false)]
    [InlineData(2d, true)]
    public Task Dark_headers_paint_readable_ink_and_preserve_objects_access_keys_and_expansion(double scale, bool highContrast) => desktop.RunAsync(async () =>
    {
        using var theme = new ThemeManager(Application.Current);
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.SetResourceReference(TextElement.FontSizeProperty, "BodySize");
        var headers = new[] { "source.png", "_Batch settings", "Save location" };
        var expanders = headers.Select(header => new Expander
        {
            Header = header, Margin = new Thickness(0, 6, 0, 6),
            Content = new TextBlock { Text = "Existing editor options", Margin = new Thickness(24, 8, 0, 8) }
        }).ToArray();
        foreach (var expander in expanders) panel.Children.Add(expander);
        var custom = new TextBlock { Text = "Custom visual header", FontWeight = FontWeights.SemiBold };
        var objectHeader = new Expander { Header = custom, Margin = new Thickness(0, 6, 0, 6) };
        panel.Children.Add(objectHeader);
        var window = new Window { Content = panel, Width = 480, SizeToContent = SizeToContent.Height, ShowActivated = false };
        window.SetResourceReference(Window.BackgroundProperty, "PanelBackground");
        try
        {
            theme.ApplyPalette(ThemeChoice.Dark, true, highContrast);
            theme.ApplyTypography(scale);
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var expectedInk = (Brush)Application.Current.FindResource("PrimaryText");
            for (int i = 0; i < expanders.Length; i++)
            {
                // WPF generates plain TextBlock text or AccessText when the string has a mnemonic.
                // Inspect those actual painted visuals, not just Expander.Foreground.
                TextBlock[] paintedText;
                if (headers[i].Contains('_'))
                {
                    var access = Assert.Single(Descendants<AccessText>(expanders[i]), text => text.Text == headers[i]);
                    paintedText = Descendants<TextBlock>(access).ToArray();
                }
                else paintedText = [Assert.Single(Descendants<TextBlock>(expanders[i]), text => text.Text == headers[i])];
                Assert.NotEmpty(paintedText);
                Assert.All(paintedText, text =>
                {
                    Assert.Same(expectedInk, text.Foreground);
                    Assert.True(text.ActualWidth > 0 && text.ActualHeight > 0);
                });
                var peer = UIElementAutomationPeer.CreatePeerForElement(expanders[i])!;
                var expand = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetPattern(PatternInterface.ExpandCollapse));
                expand.Expand(); Assert.True(expanders[i].IsExpanded);
                expand.Collapse(); Assert.False(expanders[i].IsExpanded);
            }
            // AccessKeyManager's default registration scope is the PresentationSource, not Window.
            // An explicit scope also avoids relying on whichever OS window happens to be active.
            var source = PresentationSource.FromVisual(window);
            Assert.NotNull(source);
            var mnemonic = Assert.Single(Descendants<AccessText>(expanders[1]), text => text.Text == "_Batch settings");
            Assert.Same(source, PresentationSource.FromVisual(mnemonic));
            var route = new AccessKeyPressedEventArgs("B");
            mnemonic.RaiseEvent(route);
            var target = Assert.IsAssignableFrom<ToggleButton>(route.Target);
            Assert.Contains(target, Descendants<ToggleButton>(expanders[1]));
            Assert.True(AccessKeyManager.IsKeyRegistered(source, "B"));
            // ProcessKey is WPF's documented access-key dispatch API; no OS keyboard input is injected.
            AccessKeyManager.ProcessKey(source, "B", false);
            Assert.True(expanders[1].IsExpanded);
            Assert.False(expanders[0].IsExpanded); Assert.False(expanders[2].IsExpanded);
            Assert.False(objectHeader.IsExpanded);
            AccessKeyManager.ProcessKey(source, "B", false);
            Assert.False(expanders[1].IsExpanded);
            Assert.Same(custom, objectHeader.Header);
            Assert.Contains(custom, Descendants<TextBlock>(objectHeader));
            Assert.Equal(FontWeights.SemiBold, custom.FontWeight);
            Assert.Same(expectedInk, custom.Foreground);
            VisualArtifacts.Capture(panel, $"expander-Dark-text{scale * 100:0}{(highContrast ? "-HC" : "")}", 1, ThemeChoice.Dark,
                new { actualTextScale = scale, forcedHighContrastPalette = highContrast, customHeaderPreserved = true, accessKeyScope = source.GetType().FullName,
                    accessKeyDispatch = "Explicit WPF PresentationSource; Batch Expander only; no OS input" });
        }
        finally { window.Close(); theme.ApplyTypography(1); theme.Apply(ThemeChoice.Light, true); }
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
