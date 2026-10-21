using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

public sealed class CompactWindowSizingTests
{
    [Theory]
    [InlineData(1280,672)]
    [InlineData(1024,540)]
    [InlineData(1920d/1.5,1008d/1.5)]
    [InlineData(2048d/2,1080d/2)]
    public void Large_editor_leaves_visible_desktop_margin_on_laptop_work_areas(double width,double height)
    {
        var result=CompactWindowSizing.Fit(900,900,640,560,width,height);
        Assert.InRange(result.Width,1,width*.84);
        Assert.InRange(result.Height,1,height*.86);
        Assert.True(result.MinWidth<=result.Width&&result.MinHeight<=result.Height);
    }

    [Fact]
    public void Small_preferred_window_stays_small_instead_of_expanding_to_budget()
    {
        var result=CompactWindowSizing.Fit(400,500,340,360,1920,1040);
        Assert.Equal(400,result.Width);Assert.Equal(500,result.Height);
        Assert.Equal(340,result.MinWidth);Assert.Equal(360,result.MinHeight);
    }

    [Fact]
    public void Oversized_minimum_cannot_force_full_screen_initial_presentation()
    {
        var result=CompactWindowSizing.Fit(900,900,860,640,683,364);
        Assert.Equal(683*.84,result.Width,8);Assert.Equal(364*.86,result.Height,8);
        Assert.Equal(result.Width,result.MinWidth);Assert.Equal(result.Height,result.MinHeight);
    }

    [Theory]
    [InlineData(double.NaN,540)]
    [InlineData(double.PositiveInfinity,540)]
    [InlineData(1024,0)]
    [InlineData(-1,540)]
    public void Invalid_work_area_is_rejected(double width,double height) =>
        Assert.Throws<ArgumentOutOfRangeException>(()=>CompactWindowSizing.Fit(520,620,420,440,width,height));

    [Fact]
    public void Smaller_wheel_preserves_existing_displayed_label_size()
    {
        Assert.Equal(.78,WheelPresentationMetrics.Scale);
        Assert.Equal(368*.78,WheelPresentationMetrics.Width);
        Assert.Equal(454*.78,WheelPresentationMetrics.Height);
        Assert.True(WheelPresentationMetrics.LabelCompensation*WheelPresentationMetrics.Scale>=.85-1e-10);
    }
}
