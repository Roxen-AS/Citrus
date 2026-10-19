using System.Text.Json;
using Citrus.Core;

namespace Citrus.Core.Tests;

public sealed partial class EditorDocumentTests
{
    [Theory]
    [InlineData("EditorDocument")]
    [InlineData("SourceSnapshot")]
    [InlineData("FontArtifact")]
    [InlineData("ImageRecipe")]
    [InlineData("CollageRecipe")]
    [InlineData("VideoRecipe")]
    [InlineData("AudioRecipe")]
    [InlineData("VisualizerRecipe")]
    [InlineData("PdfRecipe")]
    public void Exact_public_contracts_exist(string name) =>
        Assert.NotNull(typeof(OperationRequest).Assembly.GetType("Citrus.Core.Editing." + name));

    [Fact]
    public void Schema_roundtrip_and_unknown_discriminator_rejection()
    {
        var type = typeof(OperationRequest).Assembly.GetType("Citrus.Core.Editing.EditorRecipe");
        Assert.NotNull(type);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{\"$type\":\"arbitrary-code\"}", type));
    }
}

public sealed partial class EditorDocumentTests
{
    [Fact]
    public void Recipes_are_defensive_and_history_caps_at_100()
    {
        var source = EditorTestData.Source();
        var mutable = new[] { source };
        var doc = EditorTestData.Image(source) with { Sources = System.Collections.Immutable.ImmutableArray.CreateRange(mutable) };
        mutable[0] = EditorTestData.Source();
        Assert.Equal(source, doc.Sources[0]);
        var history = new Citrus.Core.Editing.EditorHistory(doc);
        for (int i = 0; i < 120; i++) history.Commit(doc with { Export = doc.Export with { OutputStem = "edit" + i } }, "edit" + i);
        int undone = 0;
        while (history.Undo()) undone++;
        Assert.Equal(99, undone);
    }

    [Fact]
    public void Undo_increases_revision_and_new_edit_clears_redo()
    {
        var doc = EditorTestData.Image();
        var history = new Citrus.Core.Editing.EditorHistory(doc);
        history.Commit(doc with { Export = doc.Export with { OutputStem = "one" } }, "gesture");
        history.Commit(doc with { Export = doc.Export with { OutputStem = "two" } }, "gesture");
        Assert.True(history.Undo());
        Assert.Equal(doc.Export, history.Current.Export);
        Assert.Equal(3, history.Current.Revision);
        Assert.True(history.Redo());
        Assert.Equal("two", history.Current.Export.OutputStem);
        history.Undo();
        history.Commit(doc, "new");
        Assert.False(history.Redo());
        history.Reset();
        Assert.Equal(doc.Export, history.Current.Export);
        Assert.Equal(7, history.Current.Revision);
    }

    [Fact]
    public void Schema_roundtrip_validates_version_nested_discriminators_and_nonfinite_values()
    {
        var doc = EditorTestData.Image();
        var json = JsonSerializer.Serialize(doc);
        var copy = JsonSerializer.Deserialize<Citrus.Core.Editing.EditorDocument>(json)!;
        Assert.Equal(json, JsonSerializer.Serialize(copy));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Citrus.Core.Editing.EditorDocument>(json.Replace("\"SchemaVersion\":1", "\"SchemaVersion\":2")));
        Assert.Empty(Citrus.Core.Editing.EditorRecipeValidator.Validate(copy));
        Assert.NotEmpty(Citrus.Core.Editing.EditorRecipeValidator.Validate(copy with { SchemaVersion = 2 }));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Citrus.Core.Editing.EditorDocument>(json.Replace("\"quality\"", "\"unknown\"")));
        Assert.NotEmpty(Citrus.Core.Editing.EditorRecipeValidator.Validate(doc with { Recipe = EditorTestData.Recipe(doc) with { Adjustments = EditorTestData.Identity with { Gamma = double.NaN } } }));
    }

    [Fact]
    public void Validator_rejects_unknown_references_duplicate_ids_and_invalid_families()
    {
        var doc = EditorTestData.Image();
        Assert.NotEmpty(Citrus.Core.Editing.EditorRecipeValidator.Validate(doc with { Sources = [doc.Sources[0], doc.Sources[0]] }));
        Assert.NotEmpty(Citrus.Core.Editing.EditorRecipeValidator.Validate(doc with { Recipe = EditorTestData.Recipe(doc) with { SourceId = Guid.NewGuid() } }));
        Assert.NotEmpty(Citrus.Core.Editing.EditorRecipeValidator.Validate(doc with { Recipe = new Citrus.Core.Editing.PdfRecipe([], Citrus.Core.Editing.PdfOutputMode.Document, 300, Citrus.Core.Editing.PdfCompressionMode.PreserveStructure, 85, []) }));
    }

    [Fact]
    public void Rational_time_normalizes_and_compares_without_overflow()
    {
        Assert.Equal(new Citrus.Core.Editing.RationalTime(1, 2), new Citrus.Core.Editing.RationalTime(2, 4));
        Assert.True(new Citrus.Core.Editing.RationalTime(long.MaxValue, 2).CompareTo(new(long.MaxValue, 3)) > 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Citrus.Core.Editing.RationalTime(1, 0));
    }

    [Fact]
    public void Relative_crop_maps_per_source_while_pixel_crop_rejects_small_source()
    {
        var template = EditorTestData.Image();
        template = template with { Recipe = EditorTestData.Recipe(template) with { Geometry = new(0,false,false,new(100,50,600,400),null,false) } };
        var target = EditorTestData.Image(EditorTestData.Source("/tmp/target.jpeg"));
        var batch = new Citrus.Core.Editing.ImageBatchTemplate(template,new(1000,500),Citrus.Core.Editing.ImageBatchGeometry.Relative,null,false,true);
        var mapped = Citrus.Core.Editing.ImageBatchMapper.Map(batch,target,new(500,1000));
        Assert.Equal(new Citrus.Core.Editing.RectD(50,100,300,800), EditorTestData.Recipe(mapped).Geometry.Crop);
        Assert.Equal(EditorTestData.Recipe(target).SourceId, EditorTestData.Recipe(mapped).SourceId);
        Assert.Throws<ArgumentException>(() => Citrus.Core.Editing.ImageBatchMapper.Map(batch with { Geometry = Citrus.Core.Editing.ImageBatchGeometry.Pixels },target,new(500,100)));
    }

    [Fact]
    public void Apply_to_all_preserves_per_root_format_name_and_existing_layers_by_default()
    {
        var template = EditorTestData.Image();
        var target = EditorTestData.Image(EditorTestData.Source("/tmp/other.tiff"));
        var mask = new Citrus.Core.Editing.PixelMask(Guid.NewGuid(),new(1,2,3,4),Citrus.Core.Editing.MaskEffect.Solid,new(0,0,0,255),0);
        target = target with { Recipe = EditorTestData.Recipe(target) with { Masks = [mask] } };
        var mapped = Citrus.Core.Editing.ImageBatchMapper.Map(new(template,new(100,100),Citrus.Core.Editing.ImageBatchGeometry.Relative,null,false,true),target,new(100,200));
        Assert.Equal("tiff", mapped.Export.FormatId);
        Assert.Equal(target.Export.OutputStem, mapped.Export.OutputStem);
        Assert.Equal(mask, Assert.Single(EditorTestData.Recipe(mapped).Masks));
    }

    [Fact]
    public void Width_only_aspect_resize_uses_each_crop_ratio_and_legacy_allows_upscale()
    {
        var doc = EditorTestData.Image();
        var template = new Citrus.Core.Editing.ImageBatchTemplate(doc,new(100,100),Citrus.Core.Editing.ImageBatchGeometry.Relative,new(400,null,true,true),false,true);
        var mapped = Citrus.Core.Editing.ImageBatchMapper.Map(template,doc,new(100,200));
        Assert.Equal(new Citrus.Core.Editing.SizeI(400,800), EditorTestData.Recipe(mapped).Geometry.OutputSize);
        Assert.True(EditorTestData.Recipe(mapped).Geometry.AllowUpscale);
    }
}

public sealed partial class EditorDocumentTests
{
    [Fact]
    public void Copy_layers_rejects_conflicting_captured_identities()
    {
        var target = EditorTestData.Image();
        var shared = EditorTestData.Source("/tmp/background.png");
        var recipe = EditorTestData.Recipe(target);
        target = target with { Sources = [..target.Sources,shared], Recipe = recipe with { Canvas = new(new(100,100),new Citrus.Core.Editing.ImageBackground(shared.Id,Citrus.Core.Editing.ImageFit.Cover,new(.5,.5),1,0),new(0,0,100,100),Citrus.Core.Editing.ImageFit.Contain,0,null) } };
        var template = target with { Sources = [target.Sources[0],shared with { Sha256 = new string('B',64) }] };
        Assert.Throws<ArgumentException>(() => Citrus.Core.Editing.ImageBatchMapper.Map(new(template,new(100,100),Citrus.Core.Editing.ImageBatchGeometry.Relative,null,true,true),target,new(100,100)));
    }

    [Fact]
    public void Conflicting_overlapping_tones_and_masks_outside_timeline_are_rejected()
    {
        var doc = EditorTestData.Image();
        var clip = new Citrus.Core.Editing.MediaClip(Guid.NewGuid(),doc.Sources[0].Id,0,1,new(new(0,1),new(10,1)),1);
        var regions = System.Collections.Immutable.ImmutableArray.Create(
            new Citrus.Core.Editing.BleepRegion(Guid.NewGuid(),new(new(0,1),new(5,1)),Citrus.Core.Editing.BleepMode.Tone,1000,-10,10,[0]),
            new Citrus.Core.Editing.BleepRegion(Guid.NewGuid(),new(new(2,1),new(6,1)),Citrus.Core.Editing.BleepMode.Tone,2000,-10,10,[0]));
        var audio = new Citrus.Core.Editing.AudioRecipe([clip],48000,new(["L"],["L"],[1],false),regions,null,[]);
        Assert.NotEmpty(Citrus.Core.Editing.EditorRecipeValidator.Validate(doc with { Recipe = audio }));
        var mask = new Citrus.Core.Editing.TimedMask(Guid.NewGuid(),new(new(0,1),new(11,1)),Citrus.Core.Editing.MaskEffect.Solid,new(0,0,0,255),0,[new(new(0,1),new(0,0,10,10))],Citrus.Core.Editing.MaskInterpolation.Step);
        var video = new Citrus.Core.Editing.VideoRecipe([clip],null,new(100,100),Citrus.Core.Editing.ImageFit.Contain,false,[mask],[],null,[]);
        Assert.NotEmpty(Citrus.Core.Editing.EditorRecipeValidator.Validate(doc with { Recipe = video }));
    }
}
