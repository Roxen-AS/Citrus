using Citrus.Core;

namespace Citrus.Core.Tests;

public sealed class NineTargetWheelTests
{
    [Fact]
    public void All_nine_reference_video_tools_have_distinct_selectable_centers_and_neutral_gutters()
    {
        Assert.Equal(9, WheelGeometry.MaxTargetsPerPage);
        for (int index = 0; index < 9; index++)
        {
            var center = WheelGeometry.At(120, index * 40);
            Assert.Equal(index, WheelGeometry.HitTest(center.X, center.Y, 9));
            var gap = WheelGeometry.At(120, index * 40 + 20);
            Assert.Equal(-1, WheelGeometry.HitTest(gap.X, gap.Y, 9));
        }
    }
}
