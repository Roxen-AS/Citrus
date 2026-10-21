using System.Collections.Immutable;
using System.Security.Cryptography;
using Citrus.Core;
using Citrus.Core.Editing;
using Citrus.Conversion.Editing;
using Citrus.Conversion.Editing.Image;
using ImageMagick;

namespace Citrus.Conversion.Tests;

[Collection(SharedPreviewSchedulerCollection.Name)]
public sealed class ImageRecipeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "citrus-image-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string> sourceHashes = new();
    private static readonly ImageAdjustments Identity = new(1, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 7, PhotoEffect.None, 0);
    public ImageRecipeTests() => Directory.CreateDirectory(root);
    public void Dispose()
    {
        try { foreach (var (path, hash) in sourceHashes) Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))); }
        finally { Directory.Delete(root, true); }
    }
    private void RecordIntentionalSourceChange(string path) => sourceHashes[path] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private string PathIn(string name) => Path.Combine(root, name);
    private static ImageRecipe Recipe(EditorDocument d) => (ImageRecipe)d.Recipe;
    private static OperationRequest Request(EditorDocument d) => new(new("image.crop", "Crop", OperationMode.Tools, null, false, true), [d.Sources[0].Path], new(), null, d);
    private async Task<EditorDocument> Fixture(int width = 7, int height = 5, string extension = "png", bool orientation = false)
    {
        var bytes = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            bytes[i] = (byte)((x * 37 + y * 19 + 13) % 256); bytes[i + 1] = (byte)((x * 11 + y * 43 + 31) % 256); bytes[i + 2] = (byte)((x * 67 + y * 7 + 61) % 256); bytes[i + 3] = (byte)((x + y) % 3 == 0 ? 96 : 255);
        }
        using var image = new MagickImage(bytes, new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.RGBA));
        if (orientation) { image.Orientation = Orientation.RightTop; var exif = new ExifProfile(); exif.SetValue(ExifTag.Orientation, (ushort)6); image.SetProfile(exif); }
        image.SetProfile(ColorProfiles.SRGB);
        string path = PathIn(Guid.NewGuid() + "." + extension);
        image.Write(path);
        var source = (await new SourceSnapshotService().CaptureAsync([path], default))[0].Snapshot!;
        sourceHashes.Add(path, source.Sha256);
        return new(1, 3, [source], [], new ImageRecipe(source.Id, new(0, false, false, null, null, false), Identity, [], null, [], []), new("png", null, "edited", new QualityGoal(90), MetadataPolicy.PreserveSupported, new(255, 255, 255, 255)));
    }
    private ImageRecipeRenderer Renderer() => new(new SourceSnapshotService(), PathIn("cache-" + Guid.NewGuid()));
    private static byte[] Pixels(string path) { using var image = new MagickImage(path); return image.GetPixels().ToByteArray(PixelMapping.BGRA)!; }
    private static PreviewRequest Preview(RectD? tile = null) => new(9, new(2048, 2048), null, null, tile);

    [Fact]
    public async Task Identity_adjustments_preserve_pixels()
    {
        var doc = await Fixture(); using var renderer = Renderer();
        var preview = await renderer.RenderPreviewAsync(doc, Preview(), default);
        Assert.Equal(Pixels(doc.Sources[0].Path), preview.Bgra32.ToArray());
        Assert.Equal(new SizeI(7, 5), preview.Size); Assert.Equal(28, preview.Stride);
        Assert.Equal(3, preview.Revision); Assert.Equal(9, preview.Generation);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, false)]
    [InlineData(2, false, false)]
    [InlineData(3, false, false)]
    [InlineData(0, true, false)]
    [InlineData(1, true, false)]
    [InlineData(2, true, false)]
    [InlineData(3, true, false)]
    [InlineData(0, false, true)]
    [InlineData(1, false, true)]
    [InlineData(2, false, true)]
    [InlineData(3, false, true)]
    [InlineData(0, true, true)]
    [InlineData(1, true, true)]
    [InlineData(2, true, true)]
    [InlineData(3, true, true)]
    public async Task All_rotations_flips_and_rounded_non_square_crop_preserve_marker_locations(int turns, bool flipX, bool flipY)
    {
        var doc = await Fixture(); var recipe = Recipe(doc) with { Geometry = new(turns, flipX, flipY, new(.2, 1.2, 3.1, 2.1), null, false) }; doc = doc with { Recipe = recipe };
        using var renderer = Renderer(); var preview = await renderer.RenderPreviewAsync(doc, Preview(), default);
        Assert.Equal(new SizeI(4, 3), preview.Size);
        byte[] source = Pixels(doc.Sources[0].Path);
        for (int y = 0; y < 5; y++) for (int x = 0; x < 7; x++)
        {
            var p = ImageGeometryMapper.MapToCanvas(recipe, new(7, 5), new(x + .5, y + .5));
            var back = ImageGeometryMapper.MapFromCanvas(recipe, new(7, 5), p);
            Assert.Equal(x + .5, back.X, 10); Assert.Equal(y + .5, back.Y, 10);
            if (p.X >= 0 && p.X < 4 && p.Y >= 0 && p.Y < 3)
                Assert.Equal(source.Skip((y * 7 + x) * 4).Take(4), preview.Bgra32.Skip(((int)p.Y * 4 + (int)p.X) * 4).Take(4));
        }
    }

    [Fact]
    public async Task Exif_rotation_crop_rounding_and_exact_output_match_preview()
    {
        var doc = await Fixture(7, 5, "tiff", true);
        doc = doc with { Recipe = Recipe(doc) with { Geometry = new(1, true, false, new(.2, .2, 4.1, 2.1), new(3, 2), false) } };
        using var renderer = Renderer(); var preview = await renderer.RenderPreviewAsync(doc, Preview(), default);
        string stage = PathIn("export"); var export = await renderer.RenderExportAsync(doc, stage, null, default);
        Assert.Equal(new SizeI(3, 2), preview.Size);
        Assert.Equal(preview.Bgra32.ToArray(), Pixels(Path.Combine(stage, "payload", export.Files[0].RelativePath)));
        using var image = new MagickImage(Path.Combine(stage, "payload", export.Files[0].RelativePath));
        Assert.True(image.Orientation is Orientation.TopLeft or Orientation.Undefined);
        Assert.NotNull(image.GetColorProfile());
        Assert.Equal(doc.Sources[0].Sha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(doc.Sources[0].Path))));
    }

    private static readonly IReadOnlyDictionary<string, string> Goldens = new Dictionary<string, string>
    {
        ["exposure"] = "CB131276334CC634F96349A8C6A3EE9CC09910FB30E60F37FCF04EE4BDA2E162",
        ["brightness"] = "F587E3995C6306288F1617874E132F72959BD19AB1AD003A565CDC35B076504D",
        ["contrast"] = "A3E52C1A4132B4544AA6F088D53D2EE5BA1433D34C5838A39C82CA086DA74936",
        ["gamma"] = "87524B193C6461888791C4F5004FC5E347ED4184D7B51F21878C291AF288ECB6",
        ["highlights"] = "137D38BF72664BBFD554A2D7D8D33C3AB414D3468782E35EEB7BBE3B92D692F4",
        ["shadows"] = "3FD32F0893457DC351B6F22FAD839928E4FF1309B761262E0D5F4D4740F0C7D2",
        ["saturation"] = "BC9DED37BBCECCA9C0C3A070CA475107E3BF0D40227A920C783340BD8963C394",
        ["vibrance"] = "9E2D16F2A0589D5B676521DABEF4B0B8F652FFF123DCEA1FF107A4C713E94C35",
        ["hue"] = "39032282DEEB0DE59E0A1907110F935E1A73D5908368405EE97AD4CE1A13FDFB",
        ["warmth"] = "5C5441095AF8E730B0C9072166F0E190C3D388569951FEC0FED02656865CA1C8",
        ["tint"] = "9A9591A4F353C9A2DB010CF5906B24F601FE6DC49343ED4F8B9D70A00043EECA",
        ["clarity"] = "7FF4CD904497A6A5BA3060C3DBC14C13A4B14D47E6AF8E7245B5AA76C9CD7963",
        ["dehaze"] = "5B7F93ED5513040BA311CFA13C9D655F2E32657BE1AA55A82DEF6885FE9C1655",
        ["sharpen"] = "2D41C77E27D1C272E2FACCFDDF89D46D0DD4BC980BE6C4CB789A374902D7CD43",
        ["denoise"] = "E0A2DBEC7FBDE31AABCB81E670DCCD483D2D0DA297C39C055775C740999735CD",
        ["blur"] = "E3CCF4B5CA808EF2ABD7414E860784E609BA3E1B7D2F349E0B076959ECDEB519",
        ["vignette"] = "61AFA23893319936DCA81E059788AC6EADAF6477A76F3C9F089D22F68C909C7B",
        ["grain"] = "ECF63285D286029ED8EFCA600FCF7ED01502609E3B50A0DDB9E3036A866EDD21",
        ["Grayscale"] = "73232198F3351AF1C3C30A1D882528FA6DF8C712FAE2D632215E9F8A4548AA6C",
        ["Sepia"] = "BA861148F9BB6CD7B422A0F6752E47ACAF8F8A28E0E0D0F6B9B5290A4E2E7B53",
        ["Invert"] = "52F48E2FE308EA6C7E94419266645E561AA925AFE72A8FCBFBCB155C8B1787B7",
        ["Posterize"] = "3EBDE41DC1EDB3C7C1B6D8EC1A3BCB01CFF07E53E8D12F134A4B662DA0DFF352",
    };

    public static IEnumerable<object[]> Adjustments()
    {
        yield return ["exposure", Identity with { ExposureEv = 1 }]; yield return ["brightness", Identity with { Brightness = .2 }];
        yield return ["contrast", Identity with { Contrast = .5 }]; yield return ["gamma", Identity with { Gamma = 2 }];
        yield return ["highlights", Identity with { Highlights = .8 }]; yield return ["shadows", Identity with { Shadows = .8 }];
        yield return ["saturation", Identity with { Saturation = 0 }]; yield return ["vibrance", Identity with { Vibrance = .8 }];
        yield return ["hue", Identity with { HueDegrees = 70 }]; yield return ["warmth", Identity with { Warmth = .8 }]; yield return ["tint", Identity with { Tint = .8 }];
        yield return ["clarity", Identity with { Clarity = .8 }]; yield return ["dehaze", Identity with { Dehaze = .8 }];
        yield return ["sharpen", Identity with { Sharpen = .8 }]; yield return ["denoise", Identity with { Denoise = .8 }]; yield return ["blur", Identity with { BlurSigma = 2 }];
        yield return ["vignette", Identity with { Vignette = .8 }]; yield return ["grain", Identity with { Grain = .8 }];
        foreach (var effect in new[] { PhotoEffect.Grayscale, PhotoEffect.Sepia, PhotoEffect.Invert, PhotoEffect.Posterize }) yield return [effect.ToString(), Identity with { Effect = effect, EffectAmount = .8 }];
    }
    [Theory]
    [MemberData(nameof(Adjustments))]
    public async Task Each_adjustment_control_has_deterministic_pixel_effect(string control, ImageAdjustments adjustment)
    {
        var doc = await Fixture(19, 17); using var renderer = Renderer(); var original = await renderer.RenderPreviewAsync(doc, Preview(), default);
        doc = doc with { Recipe = Recipe(doc) with { Adjustments = adjustment } };
        var first = await renderer.RenderPreviewAsync(doc, Preview(), default); var second = await renderer.RenderPreviewAsync(doc, Preview(), default);
        Assert.False(first.Bgra32.SequenceEqual(original.Bgra32), control); Assert.Equal(first.Bgra32.ToArray(), second.Bgra32.ToArray());
        Assert.Equal(Goldens[control], Convert.ToHexString(SHA256.HashData(first.Bgra32.AsSpan())));
        Assert.Equal(original.Bgra32.Where((_, i) => i % 4 == 3), first.Bgra32.Where((_, i) => i % 4 == 3));
        string stage = PathIn("export"); var export = await renderer.RenderExportAsync(doc, stage, null, default);
        Assert.Equal(first.Bgra32.ToArray(), Pixels(Path.Combine(stage, "payload", export.Files[0].RelativePath)));
    }

    [Fact]
    public async Task Grain_seed_and_source_coordinates_match_repeated_render_and_detail_tile()
    {
        var doc = await Fixture(19, 17); doc = doc with { Recipe = Recipe(doc) with { Geometry = new(1, true, false, new(2, 1, 12, 14), null, false), Adjustments = Identity with { Grain = .6, Clarity = .5, BlurSigma = .4 } } };
        using var renderer = Renderer(); var full = await renderer.RenderPreviewAsync(doc, Preview(), default);
        // S=[4,7]x[5,9] maps through flip-X, CW90 and crop to C=[6,10]x[11,14].
        var tile = await renderer.RenderPreviewAsync(doc, Preview(new(4, 5, 3, 4)), default);
        Assert.Equal(new SizeI(4, 3), tile.Size);
        for (int y = 0; y < 3; y++) Assert.Equal(full.Bgra32.Skip(((y + 11) * 12 + 6) * 4).Take(16), tile.Bgra32.Skip(y * 16).Take(16));
    }

    [Fact]
    public async Task Export_uses_original_resolution_not_preview()
    {
        var doc = await Fixture(2051, 3); using var renderer = Renderer();
        var preview = await renderer.RenderPreviewAsync(doc, new(1, new(128, 128), null, null, null), default);
        Assert.Equal(128, preview.Size.Width);
        string stage = PathIn("export"); var export = await renderer.RenderExportAsync(doc, stage, null, default);
        using var output = new MagickImage(Path.Combine(stage, "payload", export.Files[0].RelativePath)); Assert.Equal(2051u, output.Width);
        Assert.Equal(Pixels(doc.Sources[0].Path), output.GetPixels().ToByteArray(PixelMapping.BGRA));
    }

    [Fact]
    public async Task Invalid_crop_and_mask_fail_before_payload()
    {
        var doc = await Fixture(); using var renderer = Renderer();
        var invalid = new[] { doc with { Recipe = Recipe(doc) with { Geometry = new(0, false, false, new(6, 0, 3, 2), null, false) } }, doc with { Recipe = Recipe(doc) with { Masks = [new(Guid.NewGuid(), new(6, 4, 2, 2), MaskEffect.Solid, new(0, 0, 0, 255), 0)] } } };
        foreach (var item in invalid) { string stage = PathIn(Guid.NewGuid().ToString()); await Assert.ThrowsAnyAsync<Exception>(() => renderer.RenderExportAsync(item, stage, null, default)); Assert.False(Directory.Exists(Path.Combine(stage, "payload"))); }
    }

    [Fact]
    public async Task Changed_source_and_cancellation_do_not_publish_or_leave_owned_staging()
    {
        var doc = await Fixture(); string cache = PathIn("cache");
        using (var renderer = new ImageRecipeRenderer(new SourceSnapshotService(), cache))
        {
            await renderer.RenderPreviewAsync(doc, Preview(), default);
            File.WriteAllBytes(doc.Sources[0].Path, [1, 2, 3]); RecordIntentionalSourceChange(doc.Sources[0].Path);
            await Assert.ThrowsAsync<IOException>(() => renderer.RenderExportAsync(doc, PathIn("export"), null, default));
            using var cts = new CancellationTokenSource(); cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => renderer.RenderPreviewAsync(doc, Preview(), cts.Token));
        }
        Assert.False(Directory.Exists(cache)); Assert.False(Directory.Exists(PathIn("export")));
    }

    [Fact]
    public async Task Independent_payload_validator_rejects_wrong_actual_format_and_dimensions()
    {
        var doc = await Fixture(); using var renderer = Renderer(); string stage = PathIn("export");
        var export = await renderer.RenderExportAsync(doc, stage, null, default); var validator = new ImageEditorPayloadValidator();
        await validator.ValidateAsync(Request(doc), export, stage, default);
        string payload = Path.Combine(stage, "payload", export.Files[0].RelativePath);
        using (var replacement = new MagickImage(MagickColors.Red, 7, 5)) replacement.Write(payload, MagickFormat.Jpeg);
        await Assert.ThrowsAnyAsync<Exception>(() => validator.ValidateAsync(Request(doc), export with { Files = [new(export.Files[0].RelativePath, new FileInfo(payload).Length)] }, stage, default));
        using (var replacement = new MagickImage(MagickColors.Red, 1, 1)) replacement.Write(payload, MagickFormat.Png);
        await Assert.ThrowsAnyAsync<Exception>(() => validator.ValidateAsync(Request(doc), export with { Files = [new(export.Files[0].RelativePath, new FileInfo(payload).Length)] }, stage, default));
    }

    [Fact]
    public async Task Inspector_checks_real_content_and_rejects_multiframe()
    {
        var doc = await Fixture(extension: "tiff"); var inspector = new ImageEditorAdmissionInspector(new SourceSnapshotService());
        Assert.Empty(await inspector.ValidateAsync(Request(doc), default));
        using (var frames = new MagickImageCollection()) { frames.Add(new MagickImage(MagickColors.Red, 7, 5)); frames.Add(new MagickImage(MagickColors.Blue, 7, 5)); frames.Write(doc.Sources[0].Path, MagickFormat.Tiff); }
        RecordIntentionalSourceChange(doc.Sources[0].Path);
        var source = (await new SourceSnapshotService().CaptureAsync([doc.Sources[0].Path], default))[0].Snapshot!;
        doc = doc with { Sources = [source], Recipe = Recipe(doc) with { SourceId = source.Id } };
        Assert.NotEmpty(await inspector.ValidateAsync(Request(doc), default));
    }

    [Fact]
    public async Task Shared_preview_scheduler_bounds_workers_and_idempotent_release()
    {
        using var a = await PreviewWorkScheduler.AcquireAsync(default); using var b = await PreviewWorkScheduler.AcquireAsync(default);
        using var cts = new CancellationTokenSource(); var waiting = PreviewWorkScheduler.AcquireAsync(cts.Token).AsTask(); Assert.False(waiting.IsCompleted);
        cts.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        a.Dispose(); a.Dispose(); using var c = await PreviewWorkScheduler.AcquireAsync(default);
        var next = PreviewWorkScheduler.AcquireAsync(default).AsTask(); Assert.False(next.IsCompleted); b.Dispose(); using var d = await next;
    }
    [Fact]
    public async Task Oversize_png_header_is_rejected_by_ping_before_pixel_allocation()
    {
        var doc = await Fixture(); byte[] data = File.ReadAllBytes(doc.Sources[0].Path);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16, 4), 20000);
        uint crc = uint.MaxValue; foreach (byte value in data.AsSpan(12, 17)) { crc ^= value; for (int b = 0; b < 8; b++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1; }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(29, 4), ~crc); File.WriteAllBytes(doc.Sources[0].Path, data); RecordIntentionalSourceChange(doc.Sources[0].Path);
        var captured = (await new SourceSnapshotService().CaptureAsync([doc.Sources[0].Path], default))[0].Snapshot!;
        doc = doc with { Sources = [captured], Recipe = Recipe(doc) with { SourceId = captured.Id } };
        var issues = await new ImageEditorAdmissionInspector(new SourceSnapshotService()).ValidateAsync(Request(doc), default);
        Assert.Contains(issues, i => i.Message.Contains("16384"));
    }

    [Fact]
    public async Task Queued_editor_preview_uses_shared_workers_and_cancels_without_staging()
    {
        var doc = await Fixture(); using var first = await PreviewWorkScheduler.AcquireAsync(default); using var second = await PreviewWorkScheduler.AcquireAsync(default);
        using var cancellation = new CancellationTokenSource(); string cache = PathIn("blocked-cache"); using var renderer = new ImageRecipeRenderer(new SourceSnapshotService(), cache);
        var preview = renderer.RenderPreviewAsync(doc, Preview(), cancellation.Token); Assert.False(preview.IsCompleted); Assert.False(Directory.Exists(cache));
        cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preview); Assert.False(Directory.Exists(cache));
    }

    [Theory]
    [InlineData("jpg")]
    [InlineData("jpeg")]
    [InlineData("tif")]
    [InlineData("tiff")]
    [InlineData("png")]
    [InlineData("webp")]
    [InlineData("bmp")]
    [InlineData("gif")]
    [InlineData("avif")]
    public async Task Real_engine_publishes_valid_alias_format_and_measured_bytes(string format)
    {
        var doc = await Fixture(); doc = doc with { Export = doc.Export with { FormatId = format } }; using var renderer = Renderer();
        var publisher = new EditorOutputPublisher(new(), new(), new ImageEditorPayloadValidator());
        var engine = new ImageEditorEngine(renderer, publisher, new ImageEditorAdmissionInspector(new SourceSnapshotService()));
        var request = Request(doc); Assert.True(engine.CanExecute(request));
        var result = await engine.ExecuteAsync(request, null, default); string output = Assert.Single(result.Outputs);
        Assert.Equal("." + format, Path.GetExtension(output)); Assert.Equal(new FileInfo(output).Length, result.Compression!.ActualBytes); Assert.Equal(doc.Sources[0].Length, result.Compression.OriginalBytes); Assert.True(result.Compression.MetTarget); Assert.Equal(1, result.Compression.Attempts);
        using var image = new MagickImage(output); Assert.Equal(7u, image.Width); Assert.Equal(5u, image.Height);
        Assert.Equal(doc.Sources[0].Sha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(doc.Sources[0].Path))));
    }

    [Theory]
    [InlineData(MetadataPolicy.PreserveSupported)]
    [InlineData(MetadataPolicy.StripDescriptive)]
    [InlineData(MetadataPolicy.StripAll)]
    public async Task Metadata_policy_and_srgb_conversion_are_explicit(MetadataPolicy policy)
    {
        var doc = await Fixture(extension: "jpeg");
        using (var source = new MagickImage(doc.Sources[0].Path)) { source.SetProfile(ColorProfiles.AdobeRGB1998); var exif = new ExifProfile(); exif.SetValue(ExifTag.Artist, "Citrus original fixture"); source.SetProfile(exif); source.Write(doc.Sources[0].Path); }
        RecordIntentionalSourceChange(doc.Sources[0].Path);
        using (var check = new MagickImage(doc.Sources[0].Path)) Assert.Equal("Citrus original fixture", check.GetExifProfile()?.GetValue(ExifTag.Artist)?.Value);
        var captured = (await new SourceSnapshotService().CaptureAsync([doc.Sources[0].Path], default))[0].Snapshot!;
        doc = doc with { Sources = [captured], Recipe = Recipe(doc) with { SourceId = captured.Id, Adjustments = Identity with { Brightness = .1 } }, Export = doc.Export with { FormatId = "jpeg", Metadata = policy } };
        using var renderer = Renderer(); string stage = PathIn("export"); var export = await renderer.RenderExportAsync(doc, stage, null, default);
        await new ImageEditorPayloadValidator().ValidateAsync(Request(doc), export, stage, default);
        using var result = new MagickImage(Path.Combine(stage, "payload", export.Files[0].RelativePath));
        if (policy == MetadataPolicy.StripAll) Assert.Null(result.GetColorProfile()); else Assert.Equal(ColorProfiles.SRGB.ToByteArray(), result.GetColorProfile()!.ToByteArray());
        string? artist = result.GetExifProfile()?.GetValue(ExifTag.Artist)?.Value;
        if (policy == MetadataPolicy.PreserveSupported) Assert.Equal("Citrus original fixture", artist); else Assert.Null(artist);
    }

    [Fact]
    public async Task Matte_is_visible_in_preview_and_opaque_lossless_export()
    {
        var doc = await Fixture(); doc = doc with { Export = doc.Export with { FormatId = "bmp", Matte = new(17, 43, 79, 255) } };
        using var renderer = Renderer(); var preview = await renderer.RenderPreviewAsync(doc, Preview(), default); Assert.All(preview.Bgra32.Where((_, i) => i % 4 == 3), a => Assert.Equal(255, a));
        string stage = PathIn("export"); var output = await renderer.RenderExportAsync(doc, stage, null, default); Assert.Equal(preview.Bgra32.ToArray(), Pixels(Path.Combine(stage, "payload", output.Files[0].RelativePath)));
    }

    [Fact]
    public async Task Crop_and_grain_use_original_source_coordinates_after_flips_and_rotation()
    {
        var doc = await Fixture(19, 17); doc = doc with { Recipe = Recipe(doc) with { Adjustments = Identity with { Grain = .9 } } }; using var renderer = Renderer(); var original = await renderer.RenderPreviewAsync(doc, Preview(), default);
        var rotated = Recipe(doc) with { Geometry = new(3, true, true, new(2, 3, 10, 11), null, false) }; var output = await renderer.RenderPreviewAsync(doc with { Recipe = rotated }, Preview(), default);
        for (int y = 0; y < output.Size.Height; y++) for (int x = 0; x < output.Size.Width; x++) { var s = ImageGeometryMapper.MapFromCanvas(rotated, new(19, 17), new(x + .5, y + .5)); Assert.Equal(original.Bgra32.Skip(((int)s.Y * 19 + (int)s.X) * 4).Take(4), output.Bgra32.Skip((y * 10 + x) * 4).Take(4)); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public async Task Geometry_roundtrip_at_100_150_200_percent_dpi(double dpi)
    {
        var doc = await Fixture(); var recipe = Recipe(doc) with { Geometry = new(3, true, true, new(.2, .7, 3.3, 4.1), new(6, 10), true) };
        foreach (var point in new[] { new Citrus.Core.Editing.PointD(-1, -2), new(.5, .5), new(7, 5) })
        { var canvas = ImageGeometryMapper.MapToCanvas(recipe, new(7, 5), point); var dips = new Citrus.Core.Editing.PointD(canvas.X / dpi, canvas.Y / dpi); var back = ImageGeometryMapper.MapFromCanvas(recipe, new(7, 5), new(dips.X * dpi, dips.Y * dpi)); Assert.Equal(point.X, back.X, 10); Assert.Equal(point.Y, back.Y, 10); }
    }

    [Fact]
    public void Dehaze_atmosphere_ignores_fully_transparent_hidden_rgb()
    {
        byte[] data = Enumerable.Range(0, 16).SelectMany(_ => new byte[] { 100, 120, 140, 255 }).ToArray(); data[0] = data[1] = data[2] = 255; data[3] = 0;
        using var image = new MagickImage(data, new PixelReadSettings(4, 4, StorageType.Char, PixelMapping.RGBA));
        var recipe = new ImageRecipe(Guid.NewGuid(), new(0, false, false, null, null, false), Identity with { Dehaze = 1 }, [], null, [], []);
        ImageAdjustmentsPipeline.Apply(image, recipe, new(4, 4), default);
        byte[] result = image.GetPixels().ToByteArray(PixelMapping.RGBA)!;
        Assert.Equal(data.Skip(4), result.Skip(4));
    }

    [Theory]
    [InlineData("exposure", 86, 46, 22)]
    [InlineData("brightness", 112, 82, 64)]
    [InlineData("contrast", 0, 0, 0)]
    [InlineData("gamma", 125, 89, 58)]
    [InlineData("highlights", 62, 32, 13)]
    [InlineData("shadows", 144, 73, 31)]
    [InlineData("saturation", 29, 29, 29)]
    [InlineData("vibrance", 82, 32, 2)]
    public void Tone_golden_pixel_matches_declared_srgb_math(string name, byte b, byte g, byte r)
    {
        // Independently evaluated from the documented scalar equations for RGB=(13,31,61).
        var adjustment = (ImageAdjustments)Adjustments().Single(row => (string)row[0] == name)[1];
        using var image = new MagickImage(new byte[] { 13, 31, 61, 255 }, new PixelReadSettings(1, 1, StorageType.Char, PixelMapping.RGBA));
        var recipe = new ImageRecipe(Guid.NewGuid(), new(0, false, false, null, null, false), adjustment, [], null, [], []);
        ImageAdjustmentsPipeline.Apply(image, recipe, new(1, 1), default);
        Assert.Equal(new byte[] { b, g, r, 255 }, image.GetPixels().ToByteArray(PixelMapping.BGRA));
    }

    [Fact]
    public async Task Metadata_uses_its_editor_and_invalid_pixel_geometry_is_an_explicit_error()
    {
        var doc = await Fixture(); using var renderer = Renderer();
        var expected=await renderer.RenderPreviewAsync(doc,Preview(),default);
        var reduction=await renderer.RenderPreviewAsync(doc with{Export=doc.Export with{Compression=new ReductionGoal(20)}},Preview(),default);
        Assert.Equal(expected.Bgra32.ToArray(),reduction.Bgra32.ToArray());
        await Assert.ThrowsAsync<NotSupportedException>(() => renderer.RenderPreviewAsync(doc with { Recipe = Recipe(doc) with { MetadataEdits = [new("exif", "Artist", MetadataEditAction.Remove, null)] } }, Preview(), default));
        await Assert.ThrowsAsync<ArgumentException>(() => renderer.RenderPreviewAsync(doc with { Recipe = Recipe(doc) with { Geometry = new(0, false, false, null, new(8, 5), false) } }, Preview(), default));
        await Assert.ThrowsAsync<ArgumentException>(() => renderer.RenderPreviewAsync(doc with { Recipe = Recipe(doc) with { Geometry = new(0, false, false, new(4, 0, 3, 5), null, false) } }, Preview(new(0, 0, 2, 2)), default));
    }

    [Fact]
    public void Blur_uses_alpha_weighted_color_once_and_preserves_original_alpha()
    {
        byte[] original = [255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 0];
        using var image = new MagickImage(original, new PixelReadSettings(3, 1, StorageType.Char, PixelMapping.RGBA));
        var recipe = new ImageRecipe(Guid.NewGuid(), new(0, false, false, null, null, false), Identity with { BlurSigma = 1 }, [], null, [], []);
        ImageAdjustmentsPipeline.Apply(image, recipe, new(3, 1), default);
        using var numerator = new MagickImage(new byte[] { 255, 0, 0, 0, 128, 0, 0, 0, 0 }, new PixelReadSettings(3, 1, StorageType.Char, PixelMapping.RGB));
        using var weights = new MagickImage(new byte[] { 255, 255, 255, 128, 128, 128, 0, 0, 0 }, new PixelReadSettings(3, 1, StorageType.Char, PixelMapping.RGB));
        numerator.GaussianBlur(0, 1, Channels.RGB); weights.GaussianBlur(0, 1, Channels.RGB);
        byte[] rgb = numerator.GetPixels().ToByteArray(PixelMapping.RGB)!, alpha = weights.GetPixels().ToByteArray(PixelMapping.RGB)!;
        byte[] expected = (byte[])original.Clone(); for (int x = 0; x < 3; x++) for (int c = 0; c < 3; c++) expected[x * 4 + c] = (byte)Math.Clamp(Math.Round(rgb[x * 3 + c] * 255d / alpha[x * 3], MidpointRounding.AwayFromZero), 0, 255);
        Assert.Equal(expected, image.GetPixels().ToByteArray(PixelMapping.RGBA));
    }

    [Fact]
    public async Task New_route_catalog_discovery_is_separate_from_typed_execution_admission()
    {
        var doc = await Fixture(); using var renderer = Renderer(); var engine = new ImageEditorEngine(renderer, new(new(), new(), new ImageEditorPayloadValidator()), new ImageEditorAdmissionInspector(new SourceSnapshotService()));
        var discovery = Request(doc) with { Editor = null }; Assert.True(engine.CanExecute(discovery));
        await Assert.ThrowsAsync<NotSupportedException>(() => engine.ExecuteAsync(discovery, null, default));
        Assert.False(engine.CanExecute(discovery with { Operation = new("image.resize", "Resize", OperationMode.Tools, null, false, true) }));
    }


    [Fact]
    public async Task Exif_orientation_is_applied_once_before_user_geometry()
    {
        var doc = await Fixture(7, 5, "tiff", true); using var source = new MagickImage(doc.Sources[0].Path); Assert.Equal(Orientation.RightTop, source.Orientation);
        byte[] raw = source.GetPixels().ToByteArray(PixelMapping.BGRA)!; using var renderer = Renderer(); var output = await renderer.RenderPreviewAsync(doc, Preview(), default); Assert.Equal(new SizeI(5, 7), output.Size);
        for (int y = 0; y < 5; y++) for (int x = 0; x < 7; x++) Assert.Equal(raw.Skip((y * 7 + x) * 4).Take(4), output.Bgra32.Skip((x * 5 + 4 - y) * 4).Take(4));
    }

    [Fact]
    public async Task Cancellation_after_staging_removes_owned_export_files()
    {
        var doc = await Fixture(); using var renderer = Renderer(); using var cancellation = new CancellationTokenSource(); string stage = PathIn("canceled-export");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => renderer.RenderExportAsync(doc, stage, new CancelProgress(cancellation), cancellation.Token)); Assert.False(Directory.Exists(stage));
    }
    private sealed class CancelProgress(CancellationTokenSource cancellation) : IProgress<ConversionProgress>
    { public void Report(ConversionProgress value) => cancellation.Cancel(); }

    public static IEnumerable<object[]> FormatMetadataPolicies()
    {
        foreach (string format in new[] { "jpg", "jpeg", "png", "webp", "tif", "tiff", "bmp", "gif", "avif" })
            foreach (var policy in Enum.GetValues<MetadataPolicy>()) yield return [format, policy];
    }
    [Theory]
    [MemberData(nameof(FormatMetadataPolicies))]
    public async Task Publisher_accepts_normal_metadata_policy_for_each_format(string format, MetadataPolicy policy)
    {
        var doc = await Fixture(); doc = doc with { Export = doc.Export with { FormatId = format, Metadata = policy } }; using var renderer = Renderer(); string stage = PathIn("export"); var staged = await renderer.RenderExportAsync(doc, stage, null, default);
        var publisher = new EditorOutputPublisher(new(), new(), new ImageEditorPayloadValidator());
        var result = await publisher.PublishAsync(Request(doc), staged, stage, default); Assert.True(File.Exists(Assert.Single(result.Outputs)));
    }

    [Theory]
    [InlineData(MetadataPolicy.StripDescriptive, "artist-and-adobe")]
    [InlineData(MetadataPolicy.StripDescriptive, "artist")]
    [InlineData(MetadataPolicy.StripDescriptive, "comment")]
    [InlineData(MetadataPolicy.StripAll, "comment")]
    [InlineData(MetadataPolicy.StripAll, "artist")]
    [InlineData(MetadataPolicy.StripAll, "title")]
    [InlineData(MetadataPolicy.StripDescriptive, "title")]
    [InlineData(MetadataPolicy.StripDescriptive, "adobe")]
    [InlineData(MetadataPolicy.PreserveSupported, "adobe")]
    [InlineData(MetadataPolicy.StripDescriptive, "missing-icc")]
    [InlineData(MetadataPolicy.PreserveSupported, "missing-icc")]
    public async Task Publisher_rejects_metadata_or_profile_policy_violations_without_rewriting_payload(MetadataPolicy policy, string mutation)
    {
        var doc = await Fixture(); doc = doc with { Export = doc.Export with { Metadata = policy } }; using var renderer = Renderer(); string stage = PathIn("export"); var staged = await renderer.RenderExportAsync(doc, stage, null, default); string payload = Path.Combine(stage, "payload", staged.Files[0].RelativePath);
        using (var image = new MagickImage(payload))
        {
            if (mutation.Contains("artist")) { var exif = new ExifProfile(); exif.SetValue(ExifTag.Artist, "must not be exported"); image.SetProfile(exif); }
            if (mutation.Contains("adobe")) image.SetProfile(ColorProfiles.AdobeRGB1998);
            if (mutation == "comment") image.SetAttribute("comment", "must not be exported");
            if (mutation == "title") image.SetAttribute("Title", "must not be exported");
            if (mutation == "missing-icc") image.RemoveProfile("icc");
            image.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", "true"); image.Write(payload, MagickFormat.Png);
        }
        using (var check = new MagickImage(payload))
        {
            if (mutation.Contains("artist")) Assert.Equal("must not be exported", check.GetExifProfile()?.GetValue(ExifTag.Artist)?.Value);
            if (mutation == "comment") Assert.Equal("must not be exported", check.GetAttribute("comment"));
            if (mutation == "title") Assert.Equal("must not be exported", check.GetAttribute("Title"));
            if (mutation.Contains("adobe")) Assert.Equal(ColorProfiles.AdobeRGB1998.ToByteArray(), check.GetColorProfile()!.ToByteArray());
            if (mutation == "missing-icc") Assert.Null(check.GetColorProfile());
        }
        long bytes = new FileInfo(payload).Length; staged = staged with { Files = [new(staged.Files[0].RelativePath, bytes)], Compression = staged.Compression with { ActualBytes = bytes } };
        byte[] before = SHA256.HashData(File.ReadAllBytes(payload)); var publisher = new EditorOutputPublisher(new(), new(), new ImageEditorPayloadValidator());
        await Assert.ThrowsAsync<InvalidDataException>(() => publisher.PublishAsync(Request(doc), staged, stage, default));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(payload))); Assert.False(File.Exists(PathIn("edited.png")));
    }

    [Fact]
    public async Task Publisher_preserve_supported_keeps_source_description_and_srgb_profile()
    {
        var doc = await Fixture(); string original = doc.Sources[0].Path;
        using (var image = new MagickImage(original))
        {
            var exif = new ExifProfile(); exif.SetValue(ExifTag.Artist, "Original artist"); image.SetProfile(exif); image.SetAttribute("comment", "Original comment"); image.Write(original, MagickFormat.Png);
        }
        RecordIntentionalSourceChange(original); var snapshot = (await new SourceSnapshotService().CaptureAsync([original], default))[0].Snapshot!;
        doc = doc with { Sources = [snapshot], Recipe = Recipe(doc) with { SourceId = snapshot.Id } };
        using var renderer = Renderer(); string scope = PathIn("export"); var staged = await renderer.RenderExportAsync(doc, scope, null, default);
        var published = await new EditorOutputPublisher(new(), new(), new ImageEditorPayloadValidator()).PublishAsync(Request(doc), staged, scope, default);
        using var output = new MagickImage(Assert.Single(published.Outputs)); Assert.Equal("Original artist", output.GetExifProfile()?.GetValue(ExifTag.Artist)?.Value); Assert.Equal("Original comment", output.GetAttribute("comment")); Assert.Equal(ColorProfiles.SRGB.ToByteArray(), output.GetColorProfile()!.ToByteArray());
    }

}
