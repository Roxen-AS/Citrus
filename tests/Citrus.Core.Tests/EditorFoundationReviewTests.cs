using System.Collections.Immutable;
using Citrus.Core.Editing;

namespace Citrus.Core.Tests;

public sealed class EditorFoundationReviewTests
{
    [Theory]
    [InlineData(0, 0, 1920, 1080, 0, 0, 248, 140)]
    [InlineData(960, 540, 960, 540, 124, 70, 124, 70)]
    public void Relative_crop_touching_source_edges_clamps_only_rounded_target_edges(
        double x, double y, double width, double height, double expectedX, double expectedY, double expectedWidth, double expectedHeight)
    {
        var template = EditorTestData.Image();
        template = template with { Recipe = EditorTestData.Recipe(template) with { Geometry = new(0, false, false, new(x, y, width, height), null, false) } };
        var target = EditorTestData.Image(EditorTestData.Source("/tmp/target.png"));
        var batch = new ImageBatchTemplate(template, new(1920, 1080), ImageBatchGeometry.Relative, null, false, true);
        var mapped = ImageBatchMapper.Map(batch, target, new(248, 140));
        Assert.Equal(new RectD(expectedX, expectedY, expectedWidth, expectedHeight), EditorTestData.Recipe(mapped).Geometry.Crop);
        Assert.Throws<ArgumentException>(() => ImageBatchMapper.Map(batch with { Geometry = ImageBatchGeometry.Pixels }, target, new(248, 140)));
        var outside = template with { Recipe = EditorTestData.Recipe(template) with { Geometry = new(0, false, false, new(0, 0, 1921, 1080), null, false) } };
        Assert.Throws<ArgumentException>(() => ImageBatchMapper.Map(batch with { Document = outside }, target, new(248, 140)));
    }

    [Fact]
    public void Rational_clip_duration_accepts_exact_end_aligned_bleep_and_rejects_over_end()
    {
        var doc = EditorTestData.Image();
        var clip = Clip(doc, new(1, 6), new(1, 3));
        var audio = Audio(clip, new(1, 6));
        Assert.Empty(EditorRecipeValidator.Validate(doc with { Recipe = audio }));
        var overEnd = audio with { Bleeps = [audio.Bleeps[0] with { Range = new(new(0, 1), new(167, 1000)) }] };
        Assert.Contains(EditorRecipeValidator.Validate(doc with { Recipe = overEnd }), i => i.FieldPath == "Bleeps.Range");
    }

    [Theory]
    [InlineData(1, 1, 6)]
    [InlineData(0.25, 2, 3)]
    [InlineData(4, 1, 24)]
    [InlineData(1.000001, 500000, 3000003)]
    public void Rational_speed_duration_is_exact_for_audio_and_video(double speed, long endNumerator, int endDenominator)
    {
        var doc = EditorTestData.Image();
        var clip = Clip(doc, new(1, 6), new(1, 3)) with { Speed = speed };
        var end = new RationalTime(endNumerator, endDenominator);
        Assert.Empty(EditorRecipeValidator.Validate(doc with { Recipe = Audio(clip, end) }));
        var mask = new TimedMask(Guid.NewGuid(), new(new(0, 1), end), MaskEffect.Solid, new(0, 0, 0, 255), 0,
            [new(new(0, 1), new(0, 0, 10, 10)), new(end, new(0, 0, 10, 10))], MaskInterpolation.Step);
        var video = Video(clip) with { Masks = [mask] };
        Assert.Empty(EditorRecipeValidator.Validate(doc with { Recipe = video }));
        Assert.Contains(EditorRecipeValidator.Validate(doc with { Recipe = video with { SnapshotAt = end } }), i => i.FieldPath == "SnapshotAt");
        Assert.Contains(EditorRecipeValidator.Validate(doc with { Recipe = video with { SplitAt = [end] } }), i => i.FieldPath == "SplitAt");
        var inside = new RationalTime(endNumerator, checked(endDenominator * 2));
        Assert.Empty(EditorRecipeValidator.Validate(doc with { Recipe = video with { SplitAt = [inside] } }));
        Assert.Empty(EditorRecipeValidator.Validate(doc with { Recipe = video with { SnapshotAt = inside } }));
    }

    [Fact]
    public void Short_clip_after_hour_24_is_valid_while_assembled_duration_is_bounded()
    {
        var doc = EditorTestData.Image();
        var lateClip = Clip(doc, new(90000, 1), new(90060, 1));
        Assert.Empty(EditorRecipeValidator.Validate(doc with { Recipe = Audio(lateClip, new(60, 1)) }));
        Assert.Empty(EditorRecipeValidator.Validate(doc with { Recipe = Video(lateClip) }));
        var longest = lateClip with { Range = new(new(90000, 1), new(176400, 1)) };
        Assert.Empty(EditorRecipeValidator.Validate(doc with { Recipe = Video(longest) }));
        var tooLong = longest with { Range = new(new(90000, 1), new(176401, 1)) };
        Assert.Contains(EditorRecipeValidator.Validate(doc with { Recipe = Video(tooLong) }), i => i.FieldPath == "Clips");
        // Speed and concatenation affect the assembled budget; absolute source offsets do not.
        Assert.Contains(EditorRecipeValidator.Validate(doc with { Recipe = Video(longest with { Speed = .5 }) }), i => i.FieldPath == "Clips");
        Assert.Contains(EditorRecipeValidator.Validate(doc with { Recipe = Video(longest) with { Clips = [longest, lateClip with { Id = Guid.NewGuid() }] } }), i => i.FieldPath == "Clips");
    }

    [Theory]
    [InlineData(0.249999)]
    [InlineData(4.000001)]
    [InlineData(1.0000001)]
    public void Invalid_speed_stays_a_validation_issue(double speed)
    {
        var doc = EditorTestData.Image();
        var recipe = Video(Clip(doc, new(0, 1), new(60, 1)) with { Speed = speed });
        Assert.Contains(EditorRecipeValidator.Validate(doc with { Recipe = recipe }), i => i.FieldPath == "Clips.Speed");
    }

    [Theory]
    [InlineData(CollageLayout.Grid)]
    [InlineData(CollageLayout.Row)]
    [InlineData(CollageLayout.Column)]
    [InlineData(CollageLayout.FeaturedLeft)]
    [InlineData(CollageLayout.FeaturedTop)]
    public void Collage_rejects_margin_that_removes_all_tile_area(CollageLayout layout)
    {
        var doc = EditorTestData.Image();
        var collage = Collage(doc, layout, 1);
        Assert.NotEmpty(EditorRecipeValidator.Validate(doc with { Recipe = collage with { Margin = 60 } }));
        Assert.NotEmpty(EditorRecipeValidator.Validate(doc with { Recipe = collage with { Margin = 50 } }));
        Assert.Empty(EditorRecipeValidator.Validate(doc with { Recipe = collage with { Margin = 49.5 } }));
    }

    [Theory]
    [InlineData(CollageLayout.Grid, 4, 2)]
    [InlineData(CollageLayout.Row, 2, 1)]
    [InlineData(CollageLayout.Column, 2, 1)]
    [InlineData(CollageLayout.FeaturedLeft, 3, 1)]
    [InlineData(CollageLayout.FeaturedTop, 3, 1)]
    public void Collage_rejects_gaps_that_remove_tile_area_and_accepts_one_pixel_extents(CollageLayout layout, int count, int columns)
    {
        var doc = EditorTestData.Image();
        var collage = Collage(doc, layout, count) with { Columns = columns, Margin = 10 };
        Assert.NotEmpty(EditorRecipeValidator.Validate(doc with { Recipe = collage with { Gap = 80 } }));
        Assert.NotEmpty(EditorRecipeValidator.Validate(doc with { Recipe = collage with { Gap = 81 } }));
        Assert.Empty(EditorRecipeValidator.Validate(doc with { Recipe = collage with { Gap = 78 } }));
    }

    private static MediaClip Clip(EditorDocument doc, RationalTime start, RationalTime end) => new(Guid.NewGuid(), doc.Sources[0].Id, 0, 1, new(start, end), 1);
    private static AudioRecipe Audio(MediaClip clip, RationalTime end) => new([clip], 48000, new(["L"], ["L"], [1], false),
        [new(Guid.NewGuid(), new(new(0, 1), end), BleepMode.Silence, 1000, -10, 10, [0])], null, []);
    private static VideoRecipe Video(MediaClip clip) => new([clip], null, new(100, 100), ImageFit.Contain, false, [], [], null, []);
    private static CollageRecipe Collage(EditorDocument doc, CollageLayout layout, int count)
    {
        var items = Enumerable.Range(0, count).Select(_ => new CollageItem(Guid.NewGuid(), doc.Sources[0].Id, null, ImageFit.Contain, new(.5, .5), 1)).ToImmutableArray();
        return new(items, layout, new(100, 100), 1, 0, 0, layout is CollageLayout.FeaturedLeft or CollageLayout.FeaturedTop ? items[0].Id : null,
            .5, new TransparentBackground(), 0, null);
    }
}
