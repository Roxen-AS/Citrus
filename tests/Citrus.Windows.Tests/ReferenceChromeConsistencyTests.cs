using System.Windows;
using System.Windows.Controls;
using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class ReferenceChromeConsistencyTests(DesktopFixture desktop)
{
    [Theory]
    [InlineData("Edit Photo")]
    [InlineData("Metadata")]
    [InlineData("Trim Audio")]
    public Task Every_tool_header_uses_the_shared_title_size_even_for_legacy_content(string title) => desktop.RunAsync(() =>
    {
        var text = new TextBlock { Text = title, FontSize = 16 };
        var header = new ChromeHeader { Content = text };
        Assert.Equal((double)header.FindResource("EditorTitleSize"), text.FontSize);
        return Task.CompletedTask;
    });
}
