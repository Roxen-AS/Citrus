using Citrus.Core;
using PointD = Citrus.Core.Editing.PointD;
using Citrus.Core.Editing;
using Citrus.Conversion.Editing;
using Citrus.Conversion.Editing.Image;
using ImageMagick;

namespace Citrus.Conversion.Tests;

[Collection(SharedPreviewSchedulerCollection.Name)]
public sealed class ImagePreviewPerformanceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "citrus-preview-work-" + Guid.NewGuid().ToString("N"));
    internal static readonly ImageAdjustments Identity = new(1, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 7, PhotoEffect.None, 0);
    internal static readonly ImageAdjustments All = Identity with { ExposureEv = .2, Brightness = .05, Contrast = .1, Gamma = 1.1, Highlights = .1, Shadows = .1, Saturation = 1.1, Vibrance = .1, HueDegrees = 5, Warmth = .1, Tint = .1, Clarity = .2, Dehaze = .2, Sharpen = .2, Denoise = .2, BlurSigma = 1, Vignette = .1, Grain = .1, Effect = PhotoEffect.Sepia, EffectAmount = .1 };
    public ImagePreviewPerformanceTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private ImageRecipeRenderer Renderer() => new(new SourceSnapshotService(), Path.Combine(root, Guid.NewGuid().ToString("N")));
    private static ImageRecipe Recipe(EditorDocument d) => (ImageRecipe)d.Recipe;
    internal async Task<EditorDocument> Fixture(int width, int height)
    {
        string path = Path.Combine(root, Guid.NewGuid() + ".png"); using (var image = new MagickImage(new MagickColor(100, 120, 140), (uint)width, (uint)height)) image.Write(path);
        var source = (await new SourceSnapshotService().CaptureAsync([path], default))[0].Snapshot!;
        return new(1, 0, [source], [], new ImageRecipe(source.Id, new(0, false, false, null, null, false), Identity, [], null, [], []), new("png", null, "edited", new QualityGoal(90), MetadataPolicy.StripAll, new(255, 255, 255, 255)));
    }
    private static PreviewRequest Preview(int bound, RectD? tile = null) => new(1, new(bound, bound), null, null, tile);

    [Fact]
    public async Task Large_source_viewport_bounds_actual_adjustment_work_and_warns()
    {
        var doc = await Fixture(4096, 2048); doc = doc with { Recipe = Recipe(doc) with { Adjustments = All } }; using var renderer = Renderer();
        var frame = await renderer.RenderPreviewAsync(doc, Preview(128), default);
        Assert.Equal(new SizeI(128, 64), frame.Size); Assert.Equal(frame.Size, renderer.Diagnostics.LastWorkingSize); Assert.Single(frame.Warnings);
        Assert.Equal(128 * 64 * 4, renderer.Diagnostics.CacheBytes); Assert.Equal(1, renderer.Diagnostics.CacheEntries);
    }
    [Fact]
    public async Task Native_size_and_source_tile_stay_exact_and_never_reuse_approximate_pixels()
    {
        var doc = await Fixture(96, 64); doc = doc with { Recipe = Recipe(doc) with { Adjustments = All } }; using var renderer = Renderer();
        Assert.Single((await renderer.RenderPreviewAsync(doc, Preview(32), default)).Warnings);
        var exact = await renderer.RenderPreviewAsync(doc, Preview(96), default); Assert.Empty(exact.Warnings); Assert.Equal(new SizeI(96, 64), renderer.Diagnostics.LastWorkingSize);
        var tile = await renderer.RenderPreviewAsync(doc, Preview(32, new(10, 20, 16, 12)), default); Assert.Empty(tile.Warnings);
        for (int y = 0; y < 12; y++) Assert.Equal(exact.Bgra32.Skip(((y + 20) * 96 + 10) * 4).Take(16 * 4), tile.Bgra32.Skip(y * 16 * 4).Take(16 * 4));
        string stage = Path.Combine(root, "export"); var output = await renderer.RenderExportAsync(doc, stage, null, default); using var image = new MagickImage(Path.Combine(stage, "payload", output.Files[0].RelativePath)); Assert.Equal(exact.Bgra32.ToArray(), image.GetPixels().ToByteArray(PixelMapping.BGRA));
    }
    [Fact]
    public async Task Cache_reuses_only_unadjusted_base_across_revisions_and_keys_geometry_and_source()
    {
        var doc = await Fixture(1024, 512); using var renderer = Renderer(); var first = await renderer.RenderPreviewAsync(doc, Preview(128), default);
        var changed = doc with { Revision = 1, Recipe = Recipe(doc) with { Adjustments = Identity with { Brightness = .2 } } }; var second = await renderer.RenderPreviewAsync(changed, Preview(128), default);
        Assert.Equal(1, renderer.Diagnostics.CacheHits); Assert.Equal(1, renderer.Diagnostics.CacheMisses); Assert.Equal(1, second.Revision); Assert.False(first.Bgra32.SequenceEqual(second.Bgra32));
        var reset = await renderer.RenderPreviewAsync(doc with { Revision = 2 }, Preview(128), default); Assert.Equal(first.Bgra32.ToArray(), reset.Bgra32.ToArray());
        await renderer.RenderPreviewAsync(doc with { Recipe = Recipe(doc) with { Geometry = new(1, true, false, new(10, 20, 400, 800), new(100, 200), false) } }, Preview(64), default); Assert.Equal(2, renderer.Diagnostics.CacheMisses);
        await renderer.RenderPreviewAsync(await Fixture(1024, 512), Preview(128), default); Assert.Equal(3, renderer.Diagnostics.CacheMisses);
    }
    [Fact]
    public async Task Cache_enforces_pixel_byte_budget_and_lru_then_releases_on_disposal()
    {
        using var renderer = Renderer(); var docs = new List<EditorDocument>();
        for (int i = 0; i < 4; i++) { var doc = await Fixture(3000, 1500); doc = doc with { Recipe = Recipe(doc) with { Geometry = new(0, false, false, null, new(1500, 750), false) } }; docs.Add(doc); await renderer.RenderPreviewAsync(doc, Preview(2048), default); Assert.InRange(renderer.Diagnostics.CacheBytes, 0, 64 * 1024 * 1024); Assert.InRange(renderer.Diagnostics.CacheEntries, 0, 4); }
        Assert.Equal(3, renderer.Diagnostics.CacheEntries); Assert.Equal(1, renderer.Diagnostics.CacheEvictions);
        long misses = renderer.Diagnostics.CacheMisses; await renderer.RenderPreviewAsync(docs[1], Preview(2048), default); Assert.Equal(misses, renderer.Diagnostics.CacheMisses);
        await renderer.RenderPreviewAsync(docs[0], Preview(2048), default); Assert.Equal(misses + 1, renderer.Diagnostics.CacheMisses);
        renderer.Dispose(); Assert.Equal(0, renderer.Diagnostics.CacheBytes); Assert.Equal(0, renderer.Diagnostics.CacheEntries); Assert.Equal(0, renderer.Diagnostics.SourceEntries);
    }
    [Fact]
    public async Task Approximate_grain_samples_original_source_centers_after_geometry()
    {
        var doc = await Fixture(512, 256); var geometry = new ImageGeometry(1, true, false, new(16, 32, 224, 448), null, false);
        doc = doc with { Recipe = Recipe(doc) with { Geometry = geometry, Adjustments = Identity with { Grain = 1 } } }; using var renderer = Renderer(); var frame = await renderer.RenderPreviewAsync(doc, Preview(64), default);
        Assert.Equal(new SizeI(32, 64), frame.Size);
        for (int y = 0; y < 64; y++) for (int x = 0; x < 32; x++)
        {
            // Independent inverse: flip-X then CW90 gives S=(512-G.y,256-G.x).
            var s = new PointD(480 - 7 * (y + .5), 240 - 7 * (x + .5)); uint h = unchecked((uint)(int)Math.Floor(s.X) * 0x9e3779b9u ^ (uint)(int)Math.Floor(s.Y) * 0x85ebca6bu ^ 7u); h ^= h >> 16; h = unchecked(h * 0x7feb352du); h ^= h >> 15; h = unchecked(h * 0x846ca68bu); h ^= h >> 16;
            double noise = (h / (double)uint.MaxValue * 2 - 1) * .1; int i = (y * 32 + x) * 4;
            Assert.Equal((byte)Math.Clamp(Math.Round((140 / 255d + noise) * 255, MidpointRounding.AwayFromZero), 0, 255), frame.Bgra32[i]);
        }
    }
    [Fact]
    public async Task Cancellation_does_not_publish_or_poison_cached_base()
    {
        var doc = await Fixture(1024, 512); using var renderer = Renderer(); var expected = await renderer.RenderPreviewAsync(doc, Preview(256), default);
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => renderer.RenderPreviewAsync(doc with { Recipe = Recipe(doc) with { Adjustments = All } }, Preview(256), canceled.Token));
        var actual = await renderer.RenderPreviewAsync(doc, Preview(256), default); Assert.Equal(expected.Bgra32.ToArray(), actual.Bgra32.ToArray()); Assert.Equal(1, renderer.Diagnostics.CacheEntries);
    }
    [Fact]
    public async Task Fast_and_refined_viewports_return_truthful_bounded_frames_and_share_session_cache()
    {
        var doc = await Fixture(4096, 2048); using var renderer = Renderer();
        var fast = await renderer.RenderPreviewAsync(doc, Preview(2048), default);
        Assert.Equal(new SizeI(512, 256), fast.Size); Assert.Equal(fast.Size, renderer.Diagnostics.LastWorkingSize); Assert.Single(fast.Warnings);
        var refined = await renderer.RenderRefinedPreviewAsync(doc, Preview(2048), default);
        Assert.Equal(new SizeI(1024, 512), refined.Size); Assert.Equal(refined.Size, renderer.Diagnostics.LastWorkingSize); Assert.Single(refined.Warnings);
        Assert.Equal(2, renderer.Diagnostics.CacheEntries); Assert.Equal(1, renderer.Diagnostics.SourceEntries);
        await renderer.RenderPreviewAsync(doc, Preview(1024), default); Assert.Equal(1, renderer.Diagnostics.CacheHits);
        var narrow = doc with { Recipe = Recipe(doc) with { Geometry = new(0, false, false, null, new(3000, 6000), true) } };
        var frame = await renderer.RenderPreviewAsync(narrow, Preview(2048), default);
        Assert.Equal(new SizeI(128, 256), frame.Size); Assert.Equal(new SizeI(512, 256), renderer.Diagnostics.LastWorkingSize);
    }
    [Fact]
    public async Task Source_lru_releases_evicted_private_files_and_decoded_base()
    {
        string cache = Path.Combine(root, "source-cache"); using var renderer = new ImageRecipeRenderer(new SourceSnapshotService(), cache);
        string? firstScope = null;
        for (int i = 0; i < 5; i++) { var doc = await Fixture(64, 32); await renderer.RenderPreviewAsync(doc, Preview(32), default); if (i == 0) firstScope = Directory.GetDirectories(cache).Single(); }
        Assert.Equal(4, renderer.Diagnostics.SourceEntries); Assert.Equal(4, Directory.GetDirectories(cache).Length); Assert.Equal(4, renderer.Diagnostics.CacheEntries);
        Assert.False(Directory.Exists(firstScope));
        renderer.Dispose(); Assert.False(Directory.Exists(cache));
    }
    [Fact]
    public async Task Cancel_after_working_base_acquisition_does_not_publish_or_poison_it()
    {
        var doc = await Fixture(1024, 1024); using var renderer = Renderer(); var expected = await renderer.RenderPreviewAsync(doc, Preview(1024), default);
        using var canceled = new CancellationTokenSource(); var active = renderer.RenderPreviewAsync(doc with { Recipe = Recipe(doc) with { Adjustments = All } }, Preview(1024), canceled.Token);
        while (renderer.Diagnostics.CacheHits == 0 && !active.IsCompleted) await Task.Delay(1);
        canceled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        var actual = await renderer.RenderPreviewAsync(doc, Preview(1024), default); Assert.Equal(expected.Bgra32.ToArray(), actual.Bgra32.ToArray());
    }
    [Fact]
    public async Task Refinement_uses_shared_two_worker_gate_and_cancel_does_not_stage()
    {
        var doc = await Fixture(64, 32);
        using var first = await PreviewWorkScheduler.AcquireAsync(default);
        using var second = await PreviewWorkScheduler.AcquireAsync(default);
        string cache = Path.Combine(root, "blocked-refinement");
        using var renderer = new ImageRecipeRenderer(new SourceSnapshotService(), cache);
        using var cancellation = new CancellationTokenSource();
        var pending = renderer.RenderRefinedPreviewAsync(doc, Preview(32), cancellation.Token);
        Assert.False(pending.IsCompleted); Assert.False(Directory.Exists(cache));
        cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(Directory.Exists(cache));
    }
    [Fact]
    public void Cache_bypasses_over_budget_image_and_rejects_canceled_creation()
    {
        using var cache = new ImagePreviewWorkingCache();
        var key = new ImagePreviewWorkingKey("large", new(0, false, false, null, null, false), new(4200, 4000));
        using (var image = cache.GetOrCreate(key, () => new MagickImage(MagickColors.Red, 4200, 4000), default)) Assert.Equal(4200u, image.Width);
        Assert.Equal(0, cache.Bytes); Assert.Equal(0, cache.Count);
        using var canceled = new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(() => cache.GetOrCreate(key, () => { var image = new MagickImage(MagickColors.Red, 1, 1); canceled.Cancel(); return image; }, canceled.Token));
        Assert.Equal(0, cache.Bytes); Assert.Equal(0, cache.Count);
    }

}
