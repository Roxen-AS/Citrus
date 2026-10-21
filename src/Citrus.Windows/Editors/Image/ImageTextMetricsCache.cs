using System.Collections.Immutable;
using Citrus.Core.Editing;

namespace Citrus.Windows.Editors.Image;

/// <summary>Bounded exact text regions; work is off-thread and only the latest dependency set may publish.</summary>
public sealed class ImageTextMetricsCache : IDisposable
{
    private sealed record Key(FontArtifact Artifact,FontSpec Font,string Text,double Width);
    private readonly object sync=new();
    private readonly Dictionary<Key,double> heights=[];
    private readonly Func<FontArtifact,FontSpec,TextGeometry,CancellationToken,Task<RectD>> measure;
    private CancellationTokenSource? current;
    private TaskCompletionSource<bool>? completion;
    private Key[] dependencies=[];
    private long generation;
    private bool disposed;
    public ImageTextMetricsCache(Func<FontArtifact,FontSpec,TextGeometry,CancellationToken,Task<RectD>>? measure=null)
        =>this.measure=measure??((artifact,font,text,ct)=>{ct.ThrowIfCancellationRequested();return Task.FromResult(ImageLayerTextMetrics.Measure(artifact,font,text));});
    public RectD? GetBounds(Annotation layer,ImmutableArray<FontArtifact> fonts)
    {
        if(layer.Geometry is not TextGeometry text || layer.Style.Font is not {} font)return null;
        var artifact=fonts.FirstOrDefault(f=>f.Id==font.ArtifactId);if(artifact is null)return null;
        lock(sync)return heights.TryGetValue(new(artifact,font,text.Text,text.WrapWidth),out double height)?new(text.Origin.X,text.Origin.Y,text.WrapWidth,height):null;
    }
    public Task<bool> UpdateAsync(EditorDocument document,CancellationToken ct)
    {
        var keys=((ImageRecipe)document.Recipe).Annotations.Where(a=>a.Geometry is TextGeometry).Select(a=>
        {
            var text=(TextGeometry)a.Geometry;var font=a.Style.Font!;var artifact=document.Fonts.Single(f=>f.Id==font.ArtifactId);
            return new Key(artifact,font,text.Text,text.WrapWidth);
        }).Distinct().ToArray();
        CancellationTokenSource work;CancellationTokenSource? previous;TaskCompletionSource<bool> result;long epoch;
        lock(sync)
        {
            if(disposed)return Task.FromResult(false);
            // Origin, selection and color changes reuse pending work rather than dispatching more font IO.
            if(current is not null&&completion is not null&&dependencies.SequenceEqual(keys))return completion.Task.WaitAsync(ct);
            work=CancellationTokenSource.CreateLinkedTokenSource(ct);epoch=++generation;previous=current;current=work;dependencies=keys;
            result=new(TaskCreationOptions.RunContinuationsAsynchronously);completion=result;
        }
        Cancel(previous);
        _=RunAsync(keys,work,result,epoch);
        return result.Task;
    }
    private async Task RunAsync(Key[] keys,CancellationTokenSource work,TaskCompletionSource<bool> result,long epoch)
    {
        try
        {
            Key[] missing;
            lock(sync)
            {
                if(disposed||generation!=epoch){result.TrySetResult(false);return;}
                missing=keys.Where(key=>!heights.ContainsKey(key)).ToArray();
            }
            if(missing.Length==0){result.TrySetResult(false);return;}
            var measured=await Task.Run(async()=>
            {
                var values=new List<(Key Key,double Height)>();
                foreach(var key in missing)
                {
                    work.Token.ThrowIfCancellationRequested();
                    var bounds=await measure(key.Artifact,key.Font,new(default,key.Text,key.Width,0),work.Token).ConfigureAwait(false);
                    work.Token.ThrowIfCancellationRequested();values.Add((key,bounds.Height));
                }
                return values;
            },work.Token).ConfigureAwait(false);
            lock(sync)
            {
                if(disposed||generation!=epoch||work.IsCancellationRequested){result.TrySetResult(false);return;}
                foreach(var(key,height)in measured){if(heights.Count>=1024&&!heights.ContainsKey(key))heights.Remove(heights.Keys.First());heights[key]=height;}
                result.TrySetResult(true);
            }
        }
        catch(OperationCanceledException)when(work.IsCancellationRequested){result.TrySetResult(false);}
        catch(Exception error){result.TrySetException(error);}
        finally
        {
            lock(sync){if(ReferenceEquals(current,work)){current=null;completion=null;}}work.Dispose();
        }
    }
    public void Dispose()
    {
        CancellationTokenSource? work;lock(sync){if(disposed)return;disposed=true;generation++;work=current;current=null;completion=null;heights.Clear();}Cancel(work);
    }
    private static void Cancel(CancellationTokenSource? work){try{work?.Cancel();}catch(ObjectDisposedException){}}
}
