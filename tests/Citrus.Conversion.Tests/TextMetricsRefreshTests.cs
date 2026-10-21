using Citrus.Core.Editing;
using Citrus.Windows.Editors.Image;

namespace Citrus.Conversion.Tests;

public sealed class TextMetricsRefreshTests
{
    [Fact]
    public async Task Exact_metrics_refresh_for_content_font_size_leading_and_local_wrap_edits()
    {
        using var f=new ImageLayerFixture();var initial=await f.Create();var artifact=AnnotationRenderingTests.Font();var font=new FontSpec(artifact.Id,20,20);
        var text=new TextGeometry(new(10,10),"A",100,0);var layer=new Annotation(Guid.NewGuid(),true,text,ImageLayerFixture.Red with {Font=font});
        var document=With(initial,artifact,layer);using var cache=new ImageTextMetricsCache();
        Assert.True(await cache.UpdateAsync(document,default));Assert.Equal(20,cache.GetBounds(layer,document.Fonts)!.Value.Height);
        layer=layer with {Geometry=text with {Text=new string('A',15)}};document=With(initial,artifact,layer);
        Assert.Null(cache.GetBounds(layer,document.Fonts));Assert.True(await cache.UpdateAsync(document,default));Assert.Equal(60,cache.GetBounds(layer,document.Fonts)!.Value.Height);
        layer=layer with {Style=layer.Style with {Font=font with {LineHeight=30}}};document=With(initial,artifact,layer);
        Assert.True(await cache.UpdateAsync(document,default));Assert.Equal(80,cache.GetBounds(layer,document.Fonts)!.Value.Height);
        layer=layer with {Style=layer.Style with {Font=font with {SizePixels=10}}};document=With(initial,artifact,layer);
        Assert.True(await cache.UpdateAsync(document,default));Assert.Equal(20,layer.Style.Font!.LineHeight);Assert.Equal(30,cache.GetBounds(layer,document.Fonts)!.Value.Height);
        layer=layer with {Style=layer.Style with {Font=layer.Style.Font! with {LineHeight=10}}};document=With(initial,artifact,layer);
        Assert.True(await cache.UpdateAsync(document,default));Assert.Equal(20,cache.GetBounds(layer,document.Fonts)!.Value.Height);
        layer=layer with {Style=layer.Style with {Font=font},Geometry=((TextGeometry)layer.Geometry) with {WrapWidth=200}};document=With(initial,artifact,layer);
        Assert.True(await cache.UpdateAsync(document,default));Assert.Equal(40,cache.GetBounds(layer,document.Fonts)!.Value.Height);
    }
    [Fact]
    public async Task Late_old_text_measurement_cannot_publish_after_new_text_is_ready()
    {
        using var f=new ImageLayerFixture();var initial=await f.Create();var font=AnnotationRenderingTests.Font();var spec=new FontSpec(font.Id,20,20);
        var old=new Annotation(Guid.NewGuid(),true,new TextGeometry(default,"A",100,0),ImageLayerFixture.Red with {Font=spec});var newer=old with {Geometry=new TextGeometry(default,"AA",100,0)};
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource<RectD>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cache=new ImageTextMetricsCache(async(_,_,text,_)=>{if(text.Text=="A"){entered.TrySetResult();return await release.Task;}return new(0,0,100,70);});
        var first=cache.UpdateAsync(With(initial,font,old),default);await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(await cache.UpdateAsync(With(initial,font,newer),default));release.SetResult(new(0,0,100,20));Assert.False(await first);
        Assert.Null(cache.GetBounds(old,[font]));Assert.Equal(70,cache.GetBounds(newer,[font])!.Value.Height);
    }
    [Fact]
    public async Task Moving_a_pending_layer_reuses_one_worker_and_keeps_the_latest_origin()
    {
        using var f=new ImageLayerFixture();var initial=await f.Create();var font=AnnotationRenderingTests.Font();var spec=new FontSpec(font.Id,20,20);
        var old=new Annotation(Guid.NewGuid(),true,new TextGeometry(new(10,20),"A",100,0),ImageLayerFixture.Red with {Font=spec});var moved=ImageLayerGeometry.Move(old,100,200);
        int calls=0;var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource<RectD>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cache=new ImageTextMetricsCache((_,_,_,_)=>{Interlocked.Increment(ref calls);entered.TrySetResult();return release.Task;});
        var first=cache.UpdateAsync(With(initial,font,old),default);await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second=cache.UpdateAsync(With(initial,font,moved),default);release.SetResult(new(0,0,100,20));await Task.WhenAll(first,second);
        Assert.Equal(1,calls);Assert.Equal(new RectD(110,220,100,20),cache.GetBounds(moved,[font]));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Closing_or_cancelling_during_native_measurement_publishes_no_late_bounds(bool close)
    {
        using var f=new ImageLayerFixture();var initial=await f.Create();var font=AnnotationRenderingTests.Font();var spec=new FontSpec(font.Id,20,20);
        var layer=new Annotation(Guid.NewGuid(),true,new TextGeometry(default,"A",100,0),ImageLayerFixture.Red with {Font=spec});
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource<RectD>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cache=new ImageTextMetricsCache((_,_,_,_)=>{entered.TrySetResult();return release.Task;});
        using var cancel=new CancellationTokenSource();var work=cache.UpdateAsync(With(initial,font,layer),cancel.Token);await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));if(close)cache.Dispose();else cancel.Cancel();release.SetResult(new(0,0,100,99));Assert.False(await work);Assert.Null(cache.GetBounds(layer,[font]));
    }
    [Fact]
    public async Task An_explicit_different_face_invalidates_the_region_and_uses_its_actual_advances()
    {
        using var f=new ImageLayerFixture();var initial=await f.Create();var first=AnnotationRenderingTests.Font();var second=AnnotationRenderingTests.Font(2);
        var layer=new Annotation(Guid.NewGuid(),true,new TextGeometry(default,new string('A',7),100,0),ImageLayerFixture.Red with {Font=new(first.Id,20,20)});
        using var cache=new ImageTextMetricsCache();await cache.UpdateAsync(With(initial,first,layer),default);Assert.Equal(20,cache.GetBounds(layer,[first])!.Value.Height);
        layer=layer with {Style=layer.Style with {Font=layer.Style.Font! with {ArtifactId=second.Id}}};
        Assert.Null(cache.GetBounds(layer,[second]));await cache.UpdateAsync(With(initial,second,layer),default);Assert.Equal(40,cache.GetBounds(layer,[second])!.Value.Height);
    }
    private static EditorDocument With(EditorDocument doc,FontArtifact font,Annotation layer)=>doc with {Fonts=[font],Recipe=((ImageRecipe)doc.Recipe) with {Annotations=[layer]}};
}
