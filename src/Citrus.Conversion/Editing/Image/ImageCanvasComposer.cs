using System.Collections.Immutable;
using Citrus.Core.Editing;
using ImageMagick;
using PointD = Citrus.Core.Editing.PointD;

namespace Citrus.Conversion.Editing.Image;

/// <summary>One shared photo/canvas composition for viewport and original-resolution export.</summary>
internal static class ImageCanvasComposer
{
    internal static MagickImage Compose(MagickImage photo, CanvasSpec canvas, SizeI photoSize, ImmutableDictionary<Guid,string> sources, double scale, CancellationToken ct)
    {
        ImageGeometryMapper.CheckSize(canvas.Size);
        int width=Math.Max(1,(int)Math.Floor(canvas.Size.Width*scale)), height=Math.Max(1,(int)Math.Floor(canvas.Size.Height*scale));
        var pixels=new byte[checked(width*height*4)];
        switch(canvas.Background)
        {
            case SolidBackground solid: Fill(pixels,solid.Color,ct); break;
            case GradientBackground gradient:
                double radians=gradient.Angle*Math.PI/180, dx=Math.Cos(radians),dy=Math.Sin(radians), extent=Math.Abs(dx)+Math.Abs(dy);
                for(int y=0;y<height;y++) {ct.ThrowIfCancellationRequested(); for(int x=0;x<width;x++)
                {
                    double p=Math.Clamp(.5+(((x+.5)/width-.5)*dx+((y+.5)/height-.5)*dy)/Math.Max(1e-9,extent),0,1);
                    var a=gradient.Stops[0]; var b=gradient.Stops[^1];
                    for(int n=1;n<gradient.Stops.Length;n++) if(p<=gradient.Stops[n].Position){a=gradient.Stops[n-1];b=gradient.Stops[n];break;}
                    double t=Math.Clamp((p-a.Position)/Math.Max(1e-9,b.Position-a.Position),0,1);
                    int i=(y*width+x)*4; pixels[i]=Lerp(a.Color.R,b.Color.R,t);pixels[i+1]=Lerp(a.Color.G,b.Color.G,t);pixels[i+2]=Lerp(a.Color.B,b.Color.B,t);pixels[i+3]=Lerp(a.Color.A,b.Color.A,t);
                }} break;
        }
        var output=new MagickImage(pixels,new PixelReadSettings((uint)width,(uint)height,StorageType.Char,PixelMapping.RGBA));
        try
        {
            if(canvas.Background is ImageBackground background)
            {
                using var image=ImageRasterSource.Load(sources[background.SourceId],ct);
                Fit(image,new(0,0,width,height),background.Fit,background.Anchor,out int x,out int y);
                if(background.BlurSigma>0)image.GaussianBlur(0,background.BlurSigma*scale);
                if(background.Opacity<1) image.Evaluate(Channels.Alpha,EvaluateOperator.Multiply,background.Opacity);
                output.Composite(image,x,y,CompositeOperator.Over);
            }
            var placement=canvas.Placement;
            int px=(int)Math.Round(placement.X*scale),py=(int)Math.Round(placement.Y*scale);
            int pw=Math.Max(1,(int)Math.Round(placement.Width*scale)),ph=Math.Max(1,(int)Math.Round(placement.Height*scale));
            using var layer=(MagickImage)photo.Clone();
            Fit(layer,new(0,0,pw,ph),canvas.Fit,new(.5,.5),out int ox,out int oy);
            using var placed=new MagickImage(MagickColors.Transparent,(uint)pw,(uint)ph);
            placed.Composite(layer,ox,oy,CompositeOperator.Over);
            byte[] rgba=placed.GetPixels().ToByteArray(PixelMapping.RGBA)!;
            double radius=Math.Min(canvas.CornerRadius*scale,Math.Min(pw,ph)/2d);
            for(int y=0;y<ph;y++){ct.ThrowIfCancellationRequested();for(int x=0;x<pw;x++)
            {
                double qx=Math.Max(Math.Abs(x+.5-pw/2d)-(pw/2d-radius),0),qy=Math.Max(Math.Abs(y+.5-ph/2d)-(ph/2d-radius),0);
                double coverage=radius==0?1:Math.Clamp(radius+.5-Math.Sqrt(qx*qx+qy*qy),0,1);
                rgba[(y*pw+x)*4+3]=(byte)Math.Round(rgba[(y*pw+x)*4+3]*coverage);
            }}
            using var rounded=new MagickImage(rgba,new PixelReadSettings((uint)pw,(uint)ph,StorageType.Char,PixelMapping.RGBA));
            if(canvas.Shadow is {} shadow)
            {
                byte[] shadowPixels=(byte[])rgba.Clone();
                for(int i=0;i<shadowPixels.Length;i+=4){shadowPixels[i]=shadow.Color.R;shadowPixels[i+1]=shadow.Color.G;shadowPixels[i+2]=shadow.Color.B;shadowPixels[i+3]=(byte)(shadowPixels[i+3]*shadow.Color.A/255);}
                using var silhouette=new MagickImage(shadowPixels,new PixelReadSettings((uint)pw,(uint)ph,StorageType.Char,PixelMapping.RGBA));
                using var shadowCanvas=new MagickImage(MagickColors.Transparent,(uint)width,(uint)height);
                shadowCanvas.Composite(silhouette,px+(int)Math.Round(shadow.X*scale),py+(int)Math.Round(shadow.Y*scale),CompositeOperator.Over);
                if(shadow.Sigma>0)shadowCanvas.GaussianBlur(0,shadow.Sigma*scale);
                output.Composite(shadowCanvas,CompositeOperator.Over);
            }
            output.Composite(rounded,px,py,CompositeOperator.Over);
            foreach(string name in photo.ProfileNames)if(photo.GetProfile(name) is {} profile)output.SetProfile(profile);
            ct.ThrowIfCancellationRequested();return output;
        }
        catch{output.Dispose();throw;}
    }
    private static byte Lerp(byte a,byte b,double t)=>(byte)Math.Clamp(Math.Round(a+(b-a)*t),0,255);
    private static void Fill(byte[] p,Rgba32 c,CancellationToken ct){for(int i=0;i<p.Length;i+=4){if((i&65535)==0)ct.ThrowIfCancellationRequested();p[i]=c.R;p[i+1]=c.G;p[i+2]=c.B;p[i+3]=c.A;}}
    private static void Fit(MagickImage image,RectD bounds,ImageFit fit,PointD anchor,out int x,out int y)
    {
        double sx=bounds.Width/image.Width,sy=bounds.Height/image.Height;
        double scale=fit==ImageFit.Cover?Math.Max(sx,sy):Math.Min(sx,sy);
        int w=Math.Max(1,(int)Math.Round(fit==ImageFit.Stretch?bounds.Width:image.Width*scale)),h=Math.Max(1,(int)Math.Round(fit==ImageFit.Stretch?bounds.Height:image.Height*scale));
        image.Resize(new MagickGeometry((uint)w,(uint)h){IgnoreAspectRatio=true});
        x=(int)Math.Round(bounds.X+(bounds.Width-w)*anchor.X);y=(int)Math.Round(bounds.Y+(bounds.Height-h)*anchor.Y);
    }
}
