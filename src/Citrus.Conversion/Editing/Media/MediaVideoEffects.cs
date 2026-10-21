using System.Collections.Immutable;
using Citrus.Core.Editing;

namespace Citrus.Conversion.Editing.Media;

public static class MediaVideoEffects
{
    public static List<string> Filters(ImmutableArray<TimedMask> masks,double timeOffset=0)
    {
        var filters=new List<string>();
        foreach(var mask in masks)
        {
            if(mask.Keys.Length!=1) throw new NotSupportedException("Use one fixed rectangle per timed redaction range. Automatic tracking is not enabled.");
            var bounds=mask.Keys[0].Bounds;
            int left=checked((int)Math.Floor(bounds.X)),top=checked((int)Math.Floor(bounds.Y));
            int width=checked((int)Math.Ceiling(bounds.X+bounds.Width)-left),height=checked((int)Math.Ceiling(bounds.Y+bounds.Height)-top);
            string clock=timeOffset==0?"t":"(t+"+MediaEffectFilters.Number(timeOffset)+")";
            string enabled=$"gte({clock},{MediaEffectFilters.Number(MediaEffectFilters.Seconds(mask.Range.Start))})*lt({clock},{MediaEffectFilters.Number(MediaEffectFilters.Seconds(mask.Range.End))})";
            string size=$"{width}:{height}",position=$"{left}:{top}";
            if(mask.Effect==MaskEffect.Solid)
                filters.Add($"drawbox=x={left}:y={top}:w={width}:h={height}:color=0x{mask.Fill.R:X2}{mask.Fill.G:X2}{mask.Fill.B:X2}@{MediaEffectFilters.Number(mask.Fill.A/255d)}:t=fill:enable='{enabled}'");
            else
            {
                int index=filters.Count;
                string effect=mask.Effect==MaskEffect.Gaussian?$"gblur=sigma={MediaEffectFilters.Number(Math.Max(.1,mask.Radius))}":$"scale={Math.Max(1,(int)(bounds.Width/Math.Max(2,mask.Radius)))}:{Math.Max(1,(int)(bounds.Height/Math.Max(2,mask.Radius)))}:flags=neighbor,scale={size}:flags=neighbor";
                filters.Add($"split[maskbase{index}][maskinput{index}];[maskinput{index}]crop={size}:{position}:exact=1,{effect}[maskfx{index}];[maskbase{index}][maskfx{index}]overlay={position}:enable='{enabled}'");
            }
        }
        return filters;
    }
}
