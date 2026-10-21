using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Citrus.Core.Editing;

namespace Citrus.Conversion.Editing.Media;

public sealed record MediaLoudnessMeasurement(double IntegratedLufs,double RangeLu,double TruePeakDbTp,double Threshold,double Offset)
{
    private static readonly SemaphoreSlim Workers=new(2);
    public string Filter(LoudnessSpec target)=>$"loudnorm=I={F(target.IntegratedLufs)}:LRA={F(target.LoudnessRangeLu)}:TP={F(target.TruePeakDbTp)}:measured_I={F(IntegratedLufs)}:measured_LRA={F(RangeLu)}:measured_TP={F(TruePeakDbTp)}:measured_thresh={F(Threshold)}:offset={F(Offset)}:linear=true";
    public static async Task<MediaLoudnessMeasurement> MeasureAsync(MediaProcess process,IReadOnlyList<string> inputArguments,LoudnessSpec target,CancellationToken ct)
    {
        await Workers.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            var start=new ProcessStartInfo(process.FfmpegPath){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true,WorkingDirectory=Path.GetDirectoryName(process.FfmpegPath)!};
            foreach(string arg in new[]{"-hide_banner","-nostdin","-nostats","-loglevel","info","-protocol_whitelist","file,pipe"}.Concat(inputArguments).Concat(new[]{"-vn","-sn","-dn","-af",$"loudnorm=I={F(target.IntegratedLufs)}:LRA={F(target.LoudnessRangeLu)}:TP={F(target.TruePeakDbTp)}:print_format=json","-f","null","-"})) start.ArgumentList.Add(arg);
            using var child=new Process{StartInfo=start};child.Start();
            void Kill(){try{if(!child.HasExited)child.Kill(entireProcessTree:true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}
            using var cancel=ct.Register(Kill);
            var text=new StringBuilder();
            async Task ReadError()
            {
                try
                {
                    char[] buffer=new char[4096];int count;
                    while((count=await child.StandardError.ReadAsync(buffer.AsMemory(),ct).ConfigureAwait(false))>0)
                    {if(text.Length+count>65536)throw new IOException("Loudness diagnostics exceeded their bounded output.");text.Append(buffer,0,count);}
                }
                catch{Kill();throw;}
            }
            try
            {
                await Task.WhenAll(ReadError(),child.StandardOutput.BaseStream.CopyToAsync(Stream.Null,ct),child.WaitForExitAsync(ct)).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();if(child.ExitCode!=0)throw new MediaProcessException("Audio loudness measurement failed. "+text);
                string log=text.ToString();int begin=log.LastIndexOf('{'),end=log.LastIndexOf('}');
                if(begin<0||end<begin)throw new InvalidDataException("The engine returned no loudness measurement.");
                using var json=JsonDocument.Parse(log[begin..(end+1)]);
                double Number(string key)
                {
                    if(!double.TryParse(json.RootElement.GetProperty(key).GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out double number)||!double.IsFinite(number))throw new InvalidOperationException("This source is silent or cannot supply a finite loudness measurement.");return number;
                }
                return new(Number("input_i"),Number("input_lra"),Number("input_tp"),Number("input_thresh"),Number("target_offset"));
            }
            finally{Kill();await child.WaitForExitAsync().ConfigureAwait(false);}
        }
        finally{Workers.Release();}
    }
    private static string F(double value)=>MediaEffectFilters.Number(value);
}
