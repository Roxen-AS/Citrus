using Citrus.Core;
using Citrus.Core.Editing;
using ImageMagick;

namespace Citrus.Conversion.Editing.Media;

public sealed class MediaEditorPayloadValidator(MediaProcess process,MediaProbe? sourceProbe=null) : IEditorPayloadValidator
{
    public async Task ValidateAsync(OperationRequest request,StagedExport staged,string directory,CancellationToken ct)
    {
        foreach(var output in staged.Files)
        {
            string path=Path.Combine(directory,"payload",output.RelativePath);
            if(request.Editor!.Recipe is VideoRecipe {SnapshotAt:not null} snapshot||request.Editor.Recipe is VisualizerRecipe {Mode:VisualizerMode.StillWaveform})
            {
                using var image=new MagickImage();await image.ReadAsync(path,ct).ConfigureAwait(false);
                SizeI size=request.Editor.Recipe is VideoRecipe v?v.Canvas:((VisualizerRecipe)request.Editor.Recipe).Size;
                if(image.Width!=size.Width||image.Height!=size.Height) throw new InvalidDataException("Snapshot dimensions differ from the selected output.");
                continue;
            }
            using var json=await process.ProbeJsonAsync(["-show_streams","-show_format","-show_chapters","-show_programs","-of","json",..MediaProcess.InputArguments(path)],2*1024*1024,ct).ConfigureAwait(false);
            var streams=json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            var video=streams.FirstOrDefault(s=>s.GetProperty("codec_type").GetString()=="video");
            var audio=streams.FirstOrDefault(s=>s.GetProperty("codec_type").GetString()=="audio");
            bool metadataOnly=request.Operation.Id.EndsWith("metadata",StringComparison.Ordinal);
            Dictionary<int,int>? metadataStreamMap=null;
            if(metadataOnly)
            {
                if(sourceProbe is null)throw new InvalidDataException("Metadata validation requires the captured original source.");
                string source=sourceProbe.StagedSources[request.Editor.Sources[0].Id];
                using var original=await process.ProbeJsonAsync(["-show_streams","-show_format","-show_chapters","-show_programs","-of","json",..MediaProcess.InputArguments(source)],2*1024*1024,ct).ConfigureAwait(false);
                var originalMap=MediaMetadataScopes.StreamMap(original.RootElement);var outputMap=MediaMetadataScopes.StreamMap(json.RootElement);
                var originals=original.RootElement.GetProperty("streams").EnumerateArray().Where(s=>originalMap.ContainsKey(s.GetProperty("index").GetInt32())).ToArray();
                var outputs=streams.Where(s=>outputMap.ContainsKey(s.GetProperty("index").GetInt32())).ToArray();
                if(originals.Length!=outputs.Length||original.RootElement.GetProperty("format").GetProperty("format_name").GetString()!=json.RootElement.GetProperty("format").GetProperty("format_name").GetString())throw new InvalidDataException("Metadata save changed the original container or media stream count.");
                for(int i=0;i<outputs.Length;i++)
                    foreach(string field in new[]{"codec_name","codec_type","width","height","sample_rate","channels"})
                        if(originals[i].TryGetProperty(field,out var expected)&&(!outputs[i].TryGetProperty(field,out var actual)||expected.ToString()!=actual.ToString()))throw new InvalidDataException("Metadata save changed media property "+field+".");
                metadataStreamMap=originalMap.ToDictionary(p=>p.Key,p=>outputMap.Single(output=>output.Value==p.Value).Key);
                var before=await PacketHashes(source,originalMap,ct).ConfigureAwait(false);var after=await PacketHashes(path,outputMap,ct).ConfigureAwait(false);
                if(before.Count!=after.Count||before.Any(pair=>!after.TryGetValue(pair.Key,out string? hash)||pair.Value!=hash))throw new InvalidDataException("Metadata save changed encoded media packets.");
                var originalChapters=original.RootElement.GetProperty("chapters").EnumerateArray().ToArray();var outputChapters=json.RootElement.GetProperty("chapters").EnumerateArray().ToArray();
                if(originalChapters.Length!=outputChapters.Length)throw new InvalidDataException("Metadata save changed chapter count.");
                for(int i=0;i<originalChapters.Length;i++)foreach(string edge in new[]{"start","end"})
                    if(MediaTime.Pts(originalChapters[i].GetProperty(edge).GetInt64(),MediaTime.ParseRatio(originalChapters[i].GetProperty("time_base").GetString()!))!=MediaTime.Pts(outputChapters[i].GetProperty(edge).GetInt64(),MediaTime.ParseRatio(outputChapters[i].GetProperty("time_base").GetString()!)))throw new InvalidDataException("Metadata save changed chapter timing.");
            }
            else if(request.Editor.Recipe is VideoRecipe recipe)
            {
                if(video.ValueKind==System.Text.Json.JsonValueKind.Undefined||video.GetProperty("width").GetInt32()!=recipe.Canvas.Width||video.GetProperty("height").GetInt32()!=recipe.Canvas.Height)
                    throw new InvalidDataException("Edited video has unexpected dimensions or no picture stream.");
                if(recipe.Mute&&audio.ValueKind!=System.Text.Json.JsonValueKind.Undefined) throw new InvalidDataException("Muted output still contains an audio stream.");
                if(!recipe.Mute&&recipe.Clips.Any(c=>c.AudioStream is not null)&&audio.ValueKind==System.Text.Json.JsonValueKind.Undefined) throw new InvalidDataException("Edited output lost its audio stream.");
            }
            else if(request.Editor.Recipe is AudioRecipe recipeAudio)
            {
                if(audio.ValueKind==System.Text.Json.JsonValueKind.Undefined||audio.GetProperty("channels").GetInt32()!=recipeAudio.Channels.OutputChannels.Length||int.Parse(audio.GetProperty("sample_rate").GetString()!,System.Globalization.CultureInfo.InvariantCulture)!=recipeAudio.SampleRate)
                    throw new InvalidDataException("Edited audio has unexpected channels or sample rate.");
            }
            else if(request.Editor.Recipe is VisualizerRecipe visual&&(video.ValueKind==System.Text.Json.JsonValueKind.Undefined||video.GetProperty("width").GetInt32()!=visual.Size.Width||video.GetProperty("height").GetInt32()!=visual.Size.Height))
                throw new InvalidDataException("Visualizer has unexpected dimensions.");
            var edits=request.Editor.Recipe switch {VideoRecipe v=>v.MetadataEdits,AudioRecipe a=>a.MetadataEdits,_=>[]};
            var scopedTags=MediaMetadataScopes.Tags(json.RootElement).ToArray();
            var values=new Dictionary<string,MediaMetadataScopes.Tag>(StringComparer.OrdinalIgnoreCase);
            foreach(var tag in scopedTags)values[tag.Scope+"\0"+tag.Key]=tag;
            foreach(var edit in edits)
            {
                string scope=edit.Scope;
                if(metadataStreamMap is not null&&MediaMetadataScopes.Scope(scope,out char kind,out int index)&&kind=='s')scope="stream:"+metadataStreamMap[index].ToString(System.Globalization.CultureInfo.InvariantCulture);
                bool present=values.TryGetValue(scope+"\0"+edit.Key,out var saved);
                if(edit.Action==MetadataEditAction.Set&&(!present||saved.Value!=((TextValue)edit.Value!).Value))
                    throw new InvalidDataException($"The selected container could not retain metadata field '{edit.Key}'. Choose a supported field or format.");
                if(edit.Action==MetadataEditAction.Remove&&present&&!MediaMetadataScopes.RegeneratedDefault(saved))throw new InvalidDataException($"Metadata field '{edit.Key}' was not removed from {scope}.");
            }
            if(request.Editor.Export.Metadata==MetadataPolicy.StripAll&&scopedTags.Any(tag=>MediaMetadataScopes.Writable(tag.Scope,tag.Key)&&!MediaMetadataScopes.RegeneratedDefault(tag)))throw new InvalidDataException("Descriptive format, stream or chapter metadata remains after Remove All.");
        }
    }
    private async Task<Dictionary<int,string>> PacketHashes(string path,IReadOnlyDictionary<int,int> mapping,CancellationToken ct)
    {
        var hashes=new Dictionary<int,System.Security.Cryptography.IncrementalHash>();
        try
        {
            await process.ProbeLinesAsync(["-show_packets","-show_data_hash","sha256","-show_entries","packet=stream_index,data_hash","-of","compact=p=0",..MediaProcess.InputArguments(path)],line=>
            {
                var fields=line.Split('|');
                string? stream=fields.FirstOrDefault(f=>f.StartsWith("stream_index=",StringComparison.Ordinal)),data=fields.FirstOrDefault(f=>f.StartsWith("data_hash=",StringComparison.Ordinal));
                if(stream is null||data is null)return;
                int index=int.Parse(stream[13..],System.Globalization.CultureInfo.InvariantCulture);
                if(!mapping.TryGetValue(index,out int logicalIndex))return;
                index=logicalIndex;
                if(!hashes.TryGetValue(index,out var hash))hashes[index]=hash=System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
                hash.AppendData(System.Text.Encoding.UTF8.GetBytes(data));
            },ct).ConfigureAwait(false);
            return hashes.ToDictionary(p=>p.Key,p=>Convert.ToHexString(p.Value.GetHashAndReset()));
        }
        finally{foreach(var hash in hashes.Values)hash.Dispose();}
    }
}
