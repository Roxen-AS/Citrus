using Citrus.Core;
using Citrus.Core.Editing;
using Citrus.Conversion.Editing.Media;

namespace Citrus.Conversion.Tests;

public sealed class VideoEditorDraftTests
{
    private static MediaEditorEngine Engine()
    {
        var capabilities = new MediaCapabilities(
            new HashSet<string> { "pcm_s16le", "mpeg4", "rawvideo" },
            new HashSet<string> { "s16le", "mp4", "rawvideo" }, new HashSet<string>(),
            new HashSet<string> { "atrim", "scale" }, false);
        return new(new(Path.GetFullPath("unused-ffmpeg"), Path.GetFullPath("unused-ffprobe")), capabilities, new());
    }

    private static (SourceSnapshot Source, MediaSourceInfo Info) Inspected()
    {
        var source = new SourceSnapshot(Guid.NewGuid(), Path.GetFullPath("clip.mp4"), 100, 1, new string('0', 64));
        var stream = new VideoStreamInfo(0, new(1, 30), 0, [new(0, new(0, 1))], new(1920, 1080), new(1920, 1080), 0, new(1, 1), false);
        return (source, new(source.Id, new(0, 1), [stream], [], new(10, 1)));
    }

    [Fact]
    public void Resize_has_a_typed_editor_route_and_reuses_inspection_to_create_a_draft()
    {
        var engine = Engine();
        var operation = Assert.Single(engine.Operations, o => o.Id == "video.resize");
        Assert.True(engine.Routes.TryGet(operation.Id, out _));
        var (source, info) = Inspected();
        var draft = engine.CreateDocument(operation, source, info, new() { Width = 1280 }, null);
        var recipe = Assert.IsType<VideoRecipe>(draft.Recipe);
        Assert.Equal(new SizeI(1280, 720), recipe.Canvas);
        Assert.Equal(new TimeRange(new(0, 1), new(10, 1)), Assert.Single(recipe.Clips).Range);
        Assert.Empty(EditorRecipeValidator.Validate(draft));
    }

    [Fact]
    public void Draft_factory_rejects_an_inspection_from_a_different_captured_source()
    {
        var engine = Engine();
        var operation = engine.Operations.Single(o => o.Id == "video.trim");
        var (source, info) = Inspected();
        Assert.Throws<ArgumentException>(() => engine.CreateDocument(operation, source, info with { SourceId = Guid.NewGuid() }, new(), null));
    }

    [Theory]
    [InlineData(0, 720)]
    [InlineData(1280, -1)]
    [InlineData(9000, 720)]
    public void Resize_draft_rejects_invalid_output_dimensions(int width, int height)
    {
        var engine = Engine();
        var operation = engine.Operations.Single(o => o.Id == "video.resize");
        var (source, info) = Inspected();
        Assert.Throws<ArgumentException>(() => engine.CreateDocument(operation, source, info,
            new() { Width = width, Height = height, PreserveAspectRatio = false }, null));
    }
}
