using Citrus.Core;

namespace Citrus.Core.Tests;

public class RoundedWheelTests
{
    [Theory]
    [InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)]
    [InlineData(6)][InlineData(7)][InlineData(8)]
    public void Actual_count_spaces_every_target_and_leaves_four_degree_gutters(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var center = At(122, i * 360d / count);
            Assert.Equal(i, WheelGeometry.HitTest(center.X, center.Y, count));
            var gap = At(122, (i + .5) * 360 / count);
            Assert.Equal(-1, WheelGeometry.HitTest(gap.X, gap.Y, count));
        }
    }

    [Fact]
    public void Rounded_corners_are_neutral_but_neighboring_petal_interior_is_selectable()
    {
        var corner = At(165, -23.5);
        Assert.Equal(-1, WheelGeometry.HitTest(corner.X, corner.Y, 7));
        var inside = At(155, -14);
        Assert.Equal(0, WheelGeometry.HitTest(inside.X, inside.Y, 7));
        Assert.Equal(-1, WheelGeometry.HitTest(184,184,7));
    }

    [Fact]
    public void Eight_targets_fit_a_page_and_partial_pages_are_reachable()
    {
        var paging = new WheelPaging();
        paging.UpdateHover(1,0,8);
        Assert.Equal(0,paging.UpdateHover(1,400,8));
        paging.UpdateHover(1,800,19);
        Assert.Equal(2,paging.UpdateHover(1,1200,19));
        Assert.Equal(2,paging.UpdateHover(1,1600,19));
    }

    private static (double X,double Y) At(double radius,double degrees) =>
        (184 + radius * Math.Sin(degrees*Math.PI/180),184-radius*Math.Cos(degrees*Math.PI/180));
}
