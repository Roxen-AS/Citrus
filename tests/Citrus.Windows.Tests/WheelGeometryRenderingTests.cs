using Citrus.Core;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit.Abstractions;
using Point = System.Windows.Point;
using ShapePath = System.Windows.Shapes.Path;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public class WheelGeometryRenderingTests(DesktopFixture desktop, ITestOutputHelper output)
{
    [Theory]
    [InlineData(0)][InlineData(1)][InlineData(2)][InlineData(6)][InlineData(7)][InlineData(9)]
    public Task Visible_outlines_and_hit_regions_agree_for_corners_gutters_and_ring(int count) => desktop.RunAsync(() =>
    {
        var operations = Enumerable.Range(0, count).Select(i => new OperationDescriptor("test" + i,"Test",OperationMode.Convert,null,false,false)).ToArray();
        var wheel = new WheelWindow(new OperationCatalog([]));
        try
        {
            // Populate real retained page content without initiating native input. Invoke the exact
            // target method used by DragOver/Drop, rather than a second mathematical approximation.
            typeof(WheelWindow).GetField("operations", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(wheel, operations);
            typeof(WheelWindow).GetMethod("RenderSectors", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(wheel, null);
            var hitTarget = typeof(WheelWindow).GetMethod("HitTarget", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var petals = ((Canvas)wheel.FindName("Sectors")).Children.Cast<WheelPetal>().ToArray();
            var outlines = petals.Select(petal => ((ShapePath)petal.Children[0]).Data).ToArray();
            foreach (double radius in new[] { 77d,79,82,86,122,158,162,165,167 })
            for (double angle=0;angle<360;angle+=.75)
            {
                var point = WheelGeometry.At(radius,angle);
                var nativePoint = new Point(point.X, point.Y);
                int rendered = Array.FindIndex(outlines,g => g.FillContains(nativePoint,.001,ToleranceType.Absolute));
                int target = (int)hitTarget.Invoke(wheel, [nativePoint])!;
                Assert.True(rendered == target, $"count={count}, radius={radius}, angle={angle}, point={point}, drawn={rendered}, hit={target}");
                int portable = WheelGeometry.HitTest(point.X,point.Y,count);
                if (portable != rendered)
                    output.WriteLine($"Boundary diagnostic: count={count}, radius={radius}, angle={angle}, point={point}, WPF={rendered}, portable={portable}, edgeDistance={EdgeDistance(point,count):R} DIP. Retained WPF fill owns desktop targeting.");
            }
        }
        finally { wheel.Close(); }
        return Task.CompletedTask;
    });

    private static double EdgeDistance(WheelPoint point, int count)
    {
        double minimum = double.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var vertices = WheelGeometry.Petal(i, count);
            for (int j = 0, previous = vertices.Count - 1; j < vertices.Count; previous = j++)
            {
                var a = vertices[previous]; var b = vertices[j];
                double dx = b.X - a.X, dy = b.Y - a.Y, lengthSquared = dx * dx + dy * dy;
                double t = lengthSquared == 0 ? 0 : Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared, 0, 1);
                double x = point.X - a.X - t * dx, y = point.Y - a.Y - t * dy;
                minimum = Math.Min(minimum, Math.Sqrt(x * x + y * y));
            }
        }
        return minimum;
    }
}
