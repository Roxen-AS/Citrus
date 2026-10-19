using Citrus.Core;
using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;
using ShapePath = System.Windows.Shapes.Path;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public class WheelGeometryRenderingTests(DesktopFixture desktop)
{
    [Theory]
    [InlineData(1)][InlineData(2)][InlineData(6)][InlineData(7)][InlineData(9)]
    public Task Visible_outlines_and_hit_regions_agree_for_corners_gutters_and_ring(int count) => desktop.RunAsync(() =>
    {
        var operation = new OperationDescriptor("test","Test",OperationMode.Convert,null,false,false);
        var outlines = Enumerable.Range(0,count).Select(i => ((ShapePath)new WheelPetal(operation,i,count).Children[0]).Data).ToArray();
        foreach (double radius in new[] { 77d,79,82,86,122,158,162,165,167 })
        for (double angle=0;angle<360;angle+=.75)
        {
            var point = WheelGeometry.At(radius,angle);
            int rendered = Array.FindIndex(outlines,g => g.FillContains(new Point(point.X,point.Y),.001,ToleranceType.Absolute));
            Assert.Equal(rendered,WheelGeometry.HitTest(point.X,point.Y,count));
        }
        return Task.CompletedTask;
    });
}
