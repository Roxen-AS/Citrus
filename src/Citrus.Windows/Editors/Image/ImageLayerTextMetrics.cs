using System.Security.Cryptography;
using Citrus.Conversion.Editing.Image;
using Citrus.Core.Editing;
using SkiaSharp;

namespace Citrus.Windows.Editors.Image;

/// <summary>Text hit region uses the shared scalar wrapping contract, computed on a worker before gestures.</summary>
public static class ImageLayerTextMetrics
{
    public static RectD Measure(FontArtifact artifact,FontSpec spec,TextGeometry text)
    {
        var issues=FontTextValidator.Validate(artifact,text.Text);
        if(!issues.IsEmpty)throw new ArgumentException(string.Join(" ",issues.Select(i=>i.Message)));
        using var input=new FileStream(artifact.OriginalPath,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(input.Length is <=0 or >64*1024*1024)throw new IOException("Font file exceeds the static font limit. Choose its face again.");
        byte[] bytes=new byte[(int)input.Length];input.ReadExactly(bytes);if(input.ReadByte()!=-1)throw new IOException("Font changed while measuring. Choose its face again.");
        if(bytes.LongLength!=artifact.Length||!Convert.ToHexString(SHA256.HashData(bytes)).Equals(artifact.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("Font content changed. Choose its exact face again.");
        using var data=SKData.CreateCopy(bytes);using var face=SKTypeface.FromData(data,artifact.FaceIndex)??throw new IOException("Font face unavailable.");using var font=new SKFont(face,(float)spec.SizePixels){Subpixel=true,Hinting=SKFontHinting.None,Edging=SKFontEdging.Antialias};
        float x=0;int lines=1;
        foreach(var rune in text.Text.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n').EnumerateRunes())
        {
            if(rune.Value==10){x=0;lines++;continue;}float advance=font.MeasureText(rune.ToString());if(x>0&&x+advance>text.WrapWidth){x=0;lines++;}x+=advance;
        }
        return new(text.Origin.X,text.Origin.Y,text.WrapWidth,Math.Max(1,(lines-1)*spec.LineHeight+font.Metrics.Descent-font.Metrics.Ascent));
    }
}
