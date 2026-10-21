using System.Collections.Immutable;
using System.Globalization;
using Citrus.Core.Editing;

namespace Citrus.Conversion.Editing.Media;

/// <summary>Trusted typed effects shared by PCM audition and export. No expressions come from user strings.</summary>
public static class MediaEffectFilters
{
    public static List<string> Audio(AudioRecipe audio, string? measuredLoudness=null)
    {
        var matrix=audio.Channels;
        int inputs=matrix.InputChannels.Length,outputs=matrix.OutputChannels.Length;
        var result=new List<string>();
        bool identity=inputs==outputs&&matrix.Coefficients.Select((v,i)=>v==(i/inputs==i%inputs?1d:0d)).All(v=>v);
        if(!identity)
        {
            string rows=string.Join('|',Enumerable.Range(0,outputs).Select(output=>"c"+output+"="+string.Join('+',Enumerable.Range(0,inputs).Select(input=>Number(matrix.Coefficients[output*inputs+input])+"*c"+input))));
            result.Add($"pan={outputs}c|{rows}");
        }
        if(matrix.LimitPeak) result.Add("alimiter=limit=0.999:level=false:latency=true");
        if(!audio.Bleeps.IsEmpty)
        {
            string[] expressions=new string[outputs];
            for(int channel=0;channel<outputs;channel++)
            {
                string expression=$"val({channel})";
                var groups=audio.Bleeps.Where(r=>r.Channels.Contains(channel)).GroupBy(r=>(r.Mode,r.FrequencyHz,r.GainDbFs,r.FadeMilliseconds));
                foreach(var group in groups)
                {
                    var intervals=new List<(double Start,double End)>();
                    foreach(var region in group.OrderBy(r=>Seconds(r.Range.Start)))
                    {
                        double start=Seconds(region.Range.Start),end=Seconds(region.Range.End);
                        if(intervals.Count>0&&start<=intervals[^1].End) intervals[^1]=(intervals[^1].Start,Math.Max(intervals[^1].End,end));
                        else intervals.Add((start,end));
                    }
                    foreach(var range in intervals)
                    {
                        string tone="0";
                        if(group.Key.Mode==BleepMode.Tone)
                        {
                            double fade=Math.Min(group.Key.FadeMilliseconds/1000d,(range.End-range.Start)/2);
                            string envelope=fade==0?"1":$"min(1,min((t-{Number(range.Start)})/{Number(fade)},({Number(range.End)}-t)/{Number(fade)}))";
                            tone=$"{Number(Math.Pow(10,group.Key.GainDbFs/20))}*sin(2*PI*{Number(group.Key.FrequencyHz)}*t)*{envelope}";
                        }
                        expression=$"if(gte(t,{Number(range.Start)})*lt(t,{Number(range.End)}),{tone},{expression})";
                    }
                }
                expressions[channel]=expression;
            }
            result.Add("aeval='"+string.Join('|',expressions)+"':c=same");
        }
        if(audio.Loudness is {} loudness) result.Add(measuredLoudness??$"loudnorm=I={Number(loudness.IntegratedLufs)}:LRA={Number(loudness.LoudnessRangeLu)}:TP={Number(loudness.TruePeakDbTp)}");
        return result;
    }
    public static double Seconds(RationalTime value)=>value.Numerator/(double)value.Denominator;
    public static string Number(double value)=>value.ToString("0.#########",CultureInfo.InvariantCulture);
}
