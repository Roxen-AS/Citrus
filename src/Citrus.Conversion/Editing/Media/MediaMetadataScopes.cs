using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Citrus.Core.Editing;

namespace Citrus.Conversion.Editing.Media;

internal static class MediaMetadataScopes
{
    private static readonly HashSet<string> StreamKeys=new(["title","artist","comment","description","language","handler_name","creation_time","copyright"],StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ChapterKeys=new(["title","artist","comment","description"],StringComparer.OrdinalIgnoreCase);
    internal readonly record struct Tag(string Scope,string Key,string Value);
    public static bool Scope(string scope,out char kind,out int index)
    {
        kind='g';index=-1;if(scope=="format")return true;
        int colon=scope.IndexOf(':');if(colon<0)return false;
        kind=scope[..colon] switch {"stream"=>'s',"chapter"=>'c',"program"=>'p',_=>'\0'};
        string suffix=scope[(colon+1)..];return kind!='\0'&&int.TryParse(suffix,NumberStyles.None,CultureInfo.InvariantCulture,out index)&&index is >=0 and <=4096&&suffix==index.ToString(CultureInfo.InvariantCulture);
    }
    public static bool Writable(string scope,string key)=>Scope(scope,out char kind,out _)&&(kind switch {'g'=>MediaEditorEngine.WritableTags.Contains(key),'s'=>StreamKeys.Contains(key),'c'=>ChapterKeys.Contains(key),_=>false});
    public static IEnumerable<Tag> Tags(JsonElement root)
    {
        if(root.TryGetProperty("format",out var format))foreach(var tag in ObjectTags(format,"format"))yield return tag;
        foreach(var (array,prefix) in new[]{("streams","stream"),("chapters","chapter"),("programs","program")})
            if(root.TryGetProperty(array,out var values))
            {
                int ordinal=0;
                foreach(var value in values.EnumerateArray())
                {
                    int index=array=="streams"?value.GetProperty("index").GetInt32():ordinal;
                    foreach(var tag in ObjectTags(value,prefix+":"+index.ToString(CultureInfo.InvariantCulture)))yield return tag;
                    ordinal++;
                }
            }
    }
    private static IEnumerable<Tag> ObjectTags(JsonElement item,string scope)
    {
        if(item.TryGetProperty("tags",out var tags))foreach(var tag in tags.EnumerateObject())yield return new(scope,tag.Name,tag.Value.ToString());
    }
    public static HashSet<int> ChapterStreams(JsonElement root)
    {
        if(!root.TryGetProperty("chapters",out var chapters)||chapters.GetArrayLength()==0)return [];
        return root.GetProperty("streams").EnumerateArray().Where(s=>s.TryGetProperty("codec_type",out var type)&&type.GetString()=="data"&&s.TryGetProperty("codec_tag_string",out var tag)&&tag.GetString()=="text").Select(s=>s.GetProperty("index").GetInt32()).ToHashSet();
    }
    public static Dictionary<int,int> StreamMap(JsonElement root)=>root.GetProperty("streams").EnumerateArray().Where(s=>!ChapterStreams(root).Contains(s.GetProperty("index").GetInt32())).Select((s,index)=>(Original:s.GetProperty("index").GetInt32(),Output:index)).ToDictionary(p=>p.Original,p=>p.Output);
    public static void Validate(ImmutableArray<MetadataEdit> edits,JsonElement root)
    {
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var map=StreamMap(root);
        foreach(var edit in edits)
        {
            if(!Scope(edit.Scope,out char kind,out int index)||!Writable(edit.Scope,edit.Key)||!names.Add(edit.Scope+"\0"+edit.Key))throw new ArgumentException("Unsupported or duplicate scoped metadata field.");
            if(kind=='s'&&!map.ContainsKey(index)||kind=='c'&&(!root.TryGetProperty("chapters",out var chapters)||index>=chapters.GetArrayLength()))throw new ArgumentException("The metadata target does not exist in the captured file.");
        }
    }
    public static IEnumerable<string> Arguments(ImmutableArray<MetadataEdit> edits,MetadataPolicy policy,int input=0,IReadOnlyDictionary<int,int>? streamMap=null)
    {
        yield return "-map_metadata";yield return policy==MetadataPolicy.PreserveSupported?input.ToString(CultureInfo.InvariantCulture):"-1";
        if(policy!=MetadataPolicy.PreserveSupported)
        {
            yield return "-map_metadata:s";yield return "-1";
            yield return "-map_metadata:c";yield return "-1";
        }
        foreach(var edit in edits)
        {
            if(!Scope(edit.Scope,out char kind,out int index)||!Writable(edit.Scope,edit.Key))throw new ArgumentException("Unsupported metadata scope or key.");
            if(kind=='s'&&streamMap is not null)index=streamMap[index];
            yield return kind=='g'?"-metadata":"-metadata:"+kind+":"+index.ToString(CultureInfo.InvariantCulture);
            yield return edit.Key+"="+(edit.Action==MetadataEditAction.Set?((TextValue)edit.Value!).Value:"");
        }
    }
    public static bool RegeneratedDefault(Tag tag)
    {
        if(tag.Key.Equals("language",StringComparison.OrdinalIgnoreCase)&&tag.Value=="und"||tag.Key.Equals("handler_name",StringComparison.OrdinalIgnoreCase)&&string.IsNullOrEmpty(tag.Value))return true;
        // The MP4 chapter atom retains an empty title slot after chapter metadata is removed.
        // Accept that empty default only; any nonempty chapter title must still fail removal validation.
        return tag.Key.Equals("title",StringComparison.OrdinalIgnoreCase)&&tag.Value.Length==0&&Scope(tag.Scope,out char kind,out _)&&kind=='c';
    }
}
