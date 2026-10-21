using System.Diagnostics;
using Citrus.Core;
using Citrus.Core.Editing;
using ImageMagick;

namespace Citrus.Conversion.Editing.Image;

internal static class ImageCompressionEncoder
{
    internal static async Task<CompressionOutcome> WriteAsync(MagickImage image,string output,ExportPolicy policy,long original,IProgress<ConversionProgress>? progress,CancellationToken ct)
    {
        long? target=policy.Compression switch {ByteGoal b=>b.MaximumBytes,ReductionGoal r=>(long)decimal.Floor(original*(100-r.Percent)/100),_=>null};
        if(target is <=0)throw new ArgumentException("The requested target is smaller than one byte.");
        if(policy.Compression is QualityGoal quality)
        {
            image.Quality=(uint)quality.Value;await Encode(image,output,policy.FormatId,ct).ConfigureAwait(false);
            return new(original,new FileInfo(output).Length,null,true,1);
        }
        var watch=Stopwatch.StartNew();long temporary=0,bestBytes=long.MaxValue;int attempts=0;
        string candidate=output+".candidate";bool success=false;
        try
        {
            int[] choices=policy.FormatId=="png"?[0,256,128,64,32,16]:policy.FormatId is "heic" or "heif"&&!image.IsOpaque?[100]:[100,90,80,70,60,50,40,30,20];
            foreach(int choice in choices)
            {
                ct.ThrowIfCancellationRequested();
                if(attempts>=12||watch.Elapsed>TimeSpan.FromSeconds(120)||temporary>2L*1024*1024*1024)break;
                attempts++;progress?.Report(new(null,$"Measuring compression candidate {attempts}"));
                using var copy=(MagickImage)image.Clone();
                if(policy.FormatId=="png")
                {
                    copy.Settings.SetDefine(MagickFormat.Png,"compression-level","9");
                    if(choice>0)
                    {
                        copy.Quantize(new QuantizeSettings{Colors=(uint)choice,DitherMethod=DitherMethod.No});
                        if(image.HasAlpha)copy.Composite(image,CompositeOperator.CopyAlpha);
                    }
                }
                else copy.Quality=(uint)choice;
                await Encode(copy,candidate,policy.FormatId,ct).ConfigureAwait(false);
                long length=new FileInfo(candidate).Length;temporary=checked(temporary+length);bestBytes=Math.Min(bestBytes,length);
                if(length<=target)
                {
                    using var decoded=ImageRasterSource.LoadEncoded(candidate,policy.FormatId,ct);
                    if(decoded.Width!=image.Width||decoded.Height!=image.Height)throw new InvalidDataException("Compression changed image dimensions.");
                    File.Move(candidate,output,true);success=true;return new(original,length,target,true,attempts);
                }
                File.Delete(candidate);
                if(policy.FormatId is "bmp" or "tif" or "tiff" or "gif")break;
            }
            throw new InvalidOperationException($"Couldn’t reach {target:N0} bytes with the supported settings; best measured size was {bestBytes:N0} bytes. Dimensions and transparency were preserved. Choose a larger target or export by quality.");
        }
        finally{if(File.Exists(candidate))File.Delete(candidate);if(!success&&File.Exists(output))File.Delete(output);}
    }
    private static Task Encode(MagickImage image,string output,string format,CancellationToken ct)=>format is "heic" or "heif"?HeicCodec.WriteAsync(image,output,(int)image.Quality,ct):image.WriteAsync(output,ImageRasterSource.Format(format),ct);
}
