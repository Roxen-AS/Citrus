using Citrus.Core.Editing;
using Citrus.Windows.Editors;

namespace Citrus.Windows.Tests;

public sealed class ImageNativePreviewTests
{
    [Theory]
    [InlineData(1)] [InlineData(1.5)] [InlineData(2)]
    public void Native_pixel_ROI_and_bitmap_density_follow_monitor_scale(double scale)
    {
        var type=typeof(EditorHostWindow).Assembly.GetType("Citrus.Windows.Editors.Image.ImagePreviewViewport");Assert.NotNull(type);
        var bounds=Assert.IsType<SizeI>(type.GetMethod("NativeBounds")!.Invoke(null,[200.9,110.7,scale,scale]));
        Assert.Equal(new SizeI((int)Math.Floor(200.9*scale),(int)Math.Floor(110.7*scale)),bounds);
        var dpi=Assert.IsType<PointD>(type.GetMethod("BitmapDpi")!.Invoke(null,[true,scale,scale]));
        Assert.Equal(new PointD(96*scale,96*scale),dpi);
        // Natural image DIPs multiplied by the monitor scale produce one physical
        // screen pixel for each decoded pixel, independently on both axes.
        Assert.Equal(137,137*96/dpi.X*scale,8);
        Assert.Equal(93,93*96/dpi.Y*scale,8);
        var fit=Assert.IsType<PointD>(type.GetMethod("BitmapDpi")!.Invoke(null,[false,scale,scale]));Assert.Equal(new PointD(96,96),fit);
    }
    [Fact]
    public void Native_pixel_ROI_remains_bounded_and_supports_nonuniform_monitor_scale()
    {
        var type=typeof(EditorHostWindow).Assembly.GetType("Citrus.Windows.Editors.Image.ImagePreviewViewport");Assert.NotNull(type);
        Assert.Equal(new SizeI(512,512),type.GetMethod("NativeBounds")!.Invoke(null,[2000d,2000d,1.5,2d]));
        Assert.Equal(new PointD(144,192),type.GetMethod("BitmapDpi")!.Invoke(null,[true,1.5,2d]));
    }
}
