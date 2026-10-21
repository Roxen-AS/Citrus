using Citrus.Core.Editing;

namespace Citrus.Windows.Editors.Image;

public static class CropGestureGeometry
{
    public static RectD Drag(RectD start, SizeI plane, int corner, double dx, double dy, double? ratio)
    {
        // A subpixel numeric crop still has at least one rendered pixel; handles edit that rounded rectangle.
        double l = Math.Floor(start.X), t = Math.Floor(start.Y), r = Math.Ceiling(start.X + start.Width), b = Math.Ceiling(start.Y + start.Height);
        start = new(l,t,r-l,b-t);
        if (corner < 0) return start with { X = Math.Clamp(Math.Round(start.X + dx), 0, plane.Width - start.Width), Y = Math.Clamp(Math.Round(start.Y + dy), 0, plane.Height - start.Height) };
        if(corner>=4)
        {
            switch(corner)
            {
                case 4:t=Math.Clamp(Math.Round(t+dy),0,b-1);break;
                case 5:r=Math.Clamp(Math.Round(r+dx),l+1,plane.Width);break;
                case 6:b=Math.Clamp(Math.Round(b+dy),t+1,plane.Height);break;
                case 7:l=Math.Clamp(Math.Round(l+dx),0,r-1);break;
                default:throw new ArgumentOutOfRangeException(nameof(corner));
            }
            if(ratio is >0)
            {
                if(corner is 4 or 6)
                {
                    double center=start.X+start.Width/2;
                    double edgeWidth=Math.Min(2*Math.Min(center,plane.Width-center),(b-t)*ratio.Value);
                    l=center-edgeWidth/2;r=center+edgeWidth/2;
                }
                else
                {
                    double center=start.Y+start.Height/2;
                    double edgeHeight=Math.Min(2*Math.Min(center,plane.Height-center),(r-l)/ratio.Value);
                    t=center-edgeHeight/2;b=center+edgeHeight/2;
                }
                // Keep the moved edge anchored while fitting the orthogonal dimension.
                if(corner==4)t=b-(r-l)/ratio.Value;else if(corner==6)b=t+(r-l)/ratio.Value;
                else if(corner==5)r=l+(b-t)*ratio.Value;else l=r-(b-t)*ratio.Value;
            }
            return new(l,t,r-l,b-t);
        }
        bool left = corner is 0 or 3, top = corner is 0 or 1;
        double anchorX = left ? start.X + start.Width : start.X, anchorY = top ? start.Y + start.Height : start.Y;
        double x = Math.Clamp((left ? start.X : start.X + start.Width) + dx, left ? 0 : anchorX + 1, left ? anchorX - 1 : plane.Width);
        double y = Math.Clamp((top ? start.Y : start.Y + start.Height) + dy, top ? 0 : anchorY + 1, top ? anchorY - 1 : plane.Height);
        double width = Math.Abs(x - anchorX), height = Math.Abs(y - anchorY);
        if (ratio is > 0)
        {
            double maxWidth = left ? anchorX : plane.Width - anchorX, maxHeight = top ? anchorY : plane.Height - anchorY;
            width = Math.Min(Math.Max(1, width), Math.Min(maxWidth, maxHeight * ratio.Value)); height = width / ratio.Value;
        }
        width = Math.Clamp(Math.Round(width),1,plane.Width); height = Math.Clamp(Math.Round(height),1,plane.Height);
        return new(Math.Clamp(Math.Round(left ? anchorX - width : anchorX),0,plane.Width-width), Math.Clamp(Math.Round(top ? anchorY - height : anchorY),0,plane.Height-height),width,height);
    }
}
