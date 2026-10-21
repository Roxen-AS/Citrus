using Citrus.Core.Editing;

namespace Citrus.Windows.Editors.Image;

/// <summary>Bounded native raster views use physical pixels rather than WPF DIPs.</summary>
internal static class ImagePreviewViewport
{
    public static SizeI NativeBounds(double widthDip,double heightDip,double scaleX,double scaleY)
    {
        CheckScale(scaleX);CheckScale(scaleY);
        return new(Edge(widthDip,scaleX),Edge(heightDip,scaleY));
    }
    public static PointD BitmapDpi(bool native,double scaleX,double scaleY)
    {
        CheckScale(scaleX);CheckScale(scaleY);
        return native?new(96*scaleX,96*scaleY):new(96,96);
    }
    private static int Edge(double dip,double scale)=>!double.IsFinite(dip)||dip<=0?1:(int)Math.Clamp(Math.Floor(dip*scale),1,512);
    private static void CheckScale(double scale){if(!double.IsFinite(scale)||scale<=0)throw new ArgumentOutOfRangeException(nameof(scale));}
}
