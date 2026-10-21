using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Citrus.Core;
using Application = System.Windows.Application;
using FontFamily = System.Windows.Media.FontFamily;
using Size = System.Windows.Size;
using ShapePath = System.Windows.Shapes.Path;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class WheelLabelLayoutTests(DesktopFixture desktop)
{
    [Theory]
    [InlineData(1d)]
    [InlineData(2.25d)]
    public Task Conversion_titles_and_tool_groups_stay_centered_at_every_canonical_target(double scale) => desktop.RunAsync(() =>
    {
        var operations = new[]
        {
            new OperationDescriptor("label.convert", "Convert to PNG", OperationMode.Convert, "png", false, false),
            new OperationDescriptor("image.resize", "Resize", OperationMode.Tools, null, false, false),
            new OperationDescriptor("label.multiline", "Copy\nmetadata", OperationMode.Tools, null, false, false)
        };
        foreach (var operation in operations)
        for (int count = 3; count <= 8; count++)
        for (int index = 0; index < count; index++)
        {
            var petal = new WheelPetal(operation, index, count);
            var label = petal.Children.OfType<TextBlock>().Single();
            label.FontFamily = (FontFamily)Application.Current.FindResource("AppFont");
            petal.ApplyTextScale(scale);
            petal.Measure(new Size(368, 368));
            petal.Arrange(new Rect(0, 0, 368, 368));
            var icon = petal.Children.OfType<ShapePath>().Last();
            double top = Canvas.GetTop(label), bottom = top + label.ActualHeight;
            if (icon.Visibility == Visibility.Visible)
            {
                Assert.Equal(5, top - Canvas.GetTop(icon) - icon.ActualHeight, 6);
                top = Canvas.GetTop(icon);
                Assert.Equal(Canvas.GetLeft(label) + label.ActualWidth / 2,
                    Canvas.GetLeft(icon) + icon.ActualWidth / 2, 6);
            }
            double radians = index * 2 * Math.PI / count;
            double expectedX = 184 + 118 * Math.Sin(radians), expectedY = 184 - 118 * Math.Cos(radians);
            Assert.Equal(expectedX, Canvas.GetLeft(label) + label.ActualWidth / 2, 6);
            Assert.True(Math.Abs((top + bottom) / 2 - expectedY) < .01,
                $"{operation.Id}, slot {index}/{count}, text scale {scale}: group center {(top + bottom) / 2:0.00} should be {expectedY:0.00}.");
            if (operation.Id == "label.multiline") Assert.True(label.ActualHeight > label.LineHeight * 1.5);
        }
        return Task.CompletedTask;
    });
}
