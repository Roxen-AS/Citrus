using Citrus.Core;
using ImageMagick;
namespace Citrus.Conversion.Tests;

public sealed class ImageEngineTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "citrus-image-" + Guid.NewGuid());
    private readonly ImageEngine engine = new();
    public ImageEngineTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private string Fixture(string extension = "png")
    {
        string path = Path.Combine(directory, "original." + extension);
        using var image = new MagickImage(MagickColors.Orange, 80, 40);
        image.SetAttribute("comment", "private metadata");
        var profile = new ExifProfile();
        profile.SetValue(ExifTag.Artist, "private author");
        image.SetProfile(profile);
        image.Write(path);
        return path;
    }
    private OperationRequest Request(string id, string path, OperationOptions? options = null) =>
        new(engine.Operations.Single(o => o.Id == id), [path], options ?? new(), null);

    [Theory]
    [InlineData("png", MagickFormat.Png)]
    [InlineData("jpeg", MagickFormat.Jpeg)]
    [InlineData("webp", MagickFormat.WebP)]
    [InlineData("tiff", MagickFormat.Tiff)]
    [InlineData("bmp", MagickFormat.Bmp)]
    [InlineData("gif", MagickFormat.Gif)]
    [InlineData("avif", MagickFormat.Avif)]
    public async Task Advertised_raster_outputs_decode_and_round_trip(string extension, MagickFormat format)
    {
        var source = Fixture();
        var original = File.ReadAllBytes(source);
        var result = await engine.ExecuteAsync(Request("image.convert." + extension, source), null, default);
        string output = Assert.Single(result.Outputs);
        Assert.NotEqual(source, output);
        using var converted = new MagickImage(output);
        Assert.Equal(format, converted.Format);
        Assert.Equal(80u, converted.Width);
        Assert.Equal(40u, converted.Height);
        var roundTrip = await engine.ExecuteAsync(Request("image.convert.png", output), null, default);
        using var decoded = new MagickImage(Assert.Single(roundTrip.Outputs));
        Assert.Equal(MagickFormat.Png, decoded.Format);
        Assert.Equal(80u, decoded.Width);
        using var pixels = decoded.GetPixels();
        var orange = pixels.GetPixel(40, 20).ToArray();
        Assert.InRange(orange[0], (byte)240, (byte)255);
        Assert.InRange(orange[1], (byte)150, (byte)180);
        Assert.InRange(orange[2], (byte)0, (byte)15);
        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [Theory]
    [InlineData(true, 20, 10)]
    [InlineData(false, 20, 20)]
    public async Task Resize_honors_dimensions_and_aspect_ratio(bool preserve, uint width, uint height)
    {
        var result = await engine.ExecuteAsync(Request("image.resize", Fixture(), new() { Width = 20, Height = 20, PreserveAspectRatio = preserve }), null, default);
        using var resized = new MagickImage(Assert.Single(result.Outputs));
        Assert.Equal(width, resized.Width);
        Assert.Equal(height, resized.Height);
    }

    [Fact]
    public async Task Rotation_creates_a_copy_with_swapped_dimensions()
    {
        var source = Fixture();
        var result = await engine.ExecuteAsync(Request("image.rotate", source), null, default);
        using var image = new MagickImage(Assert.Single(result.Outputs));
        Assert.Equal(40u, image.Width);
        Assert.Equal(80u, image.Height);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task Strip_removes_exif_and_comments_and_keeps_source()
    {
        var source = Fixture("jpeg");
        using (var before = new MagickImage(source)) Assert.NotNull(before.GetExifProfile());
        var result = await engine.ExecuteAsync(Request("image.strip", source), null, default);
        using var after = new MagickImage(Assert.Single(result.Outputs));
        Assert.Null(after.GetExifProfile());
        Assert.Null(after.GetAttribute("comment"));
        using var original = new MagickImage(source);
        Assert.NotNull(original.GetExifProfile());
    }

    [Fact]
    public async Task Quality_is_applied_and_invalid_options_create_nothing()
    {
        var source = Fixture("jpeg");
        var result = await engine.ExecuteAsync(Request("image.compress", source, new() { Quality = 27 }), null, default);
        using var image = new MagickImage(Assert.Single(result.Outputs));
        Assert.Equal(27u, image.Quality);
        var before = Directory.GetFiles(directory).Length;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => engine.ExecuteAsync(Request("image.compress", source, new() { Quality = 101 }), null, default));
        await Assert.ThrowsAsync<ArgumentException>(() => engine.ExecuteAsync(Request("image.resize", source), null, default));
        Assert.Equal(before, Directory.GetFiles(directory).Length);
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("heic")]
    [InlineData("txt")]
    public void Unsupported_and_delegate_formats_are_absent(string extension)
    {
        string path = Path.Combine(directory, "input." + extension);
        File.WriteAllText(path, "not an image");
        Assert.Empty(new OperationCatalog([engine]).GetOperations([path], OperationMode.Convert));
        Assert.False(engine.CanExecute(Request("image.convert.png", directory)));
    }

    [Fact]
    public async Task Svg_uses_bundled_renderer_as_input_only_and_produces_real_pixels()
    {
        string path = Path.Combine(directory, "vector.svg");
        File.WriteAllText(path, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"60\" height=\"30\"><rect width=\"60\" height=\"30\" fill=\"#ff0000\"/></svg>");
        var request = Request("image.convert.png", path);
        Assert.True(engine.CanExecute(request));
        Assert.DoesNotContain(engine.Operations, operation => operation.OutputExtension == ".svg");
        Assert.Empty(new OperationCatalog([engine]).GetOperations([path], OperationMode.Tools));
        var result = await engine.ExecuteAsync(request, null, default);
        using var image = new MagickImage(Assert.Single(result.Outputs));
        Assert.Equal(MagickFormat.Png, image.Format);
        Assert.Equal(60u, image.Width);
        Assert.Equal(30u, image.Height);
        using var pixels = image.GetPixels();
        var red = pixels.GetPixel(30, 15).ToArray();
        Assert.Equal((byte)255, red[0]);
        Assert.Equal((byte)0, red[1]);
        Assert.Equal((byte)0, red[2]);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Svg_external_resources_are_rejected_before_rendering()
    {
        var referenced = Fixture();
        string path = Path.Combine(directory, "external.svg");
        File.WriteAllText(path, $"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"80\" height=\"40\"><image xlink:href=\"{referenced}\" width=\"80\" height=\"40\"/></svg>");
        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => engine.ExecuteAsync(Request("image.convert.png", path), null, default));
        Assert.Contains("self-contained", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, Directory.GetFiles(directory).Length);
    }

    [Theory]
    [InlineData("gif")]
    [InlineData("webp")]
    [InlineData("tiff")]
    public async Task Animated_and_multipage_sources_are_rejected_instead_of_silently_truncated(string extension)
    {
        string path = Path.Combine(directory, "multiple." + extension);
        using (var frames = new MagickImageCollection())
        {
            frames.Add(new MagickImage(MagickColors.Red, 20, 10));
            frames.Add(new MagickImage(MagickColors.Blue, 20, 10));
            foreach (var frame in frames) frame.AnimationDelay = 10;
            frames.Write(path);
        }
        using (var check = new MagickImageCollection(path)) Assert.Equal(2, check.Count);
        var original = File.ReadAllBytes(path);
        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => engine.ExecuteAsync(Request("image.convert.png", path), null, default));
        Assert.Contains("single", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task Corrupt_or_disguised_document_and_cancellation_leave_no_output()
    {
        string path = Path.Combine(directory, "fake.png");
        File.WriteAllText(path, "%PDF-1.7\nnot a raster");
        await Assert.ThrowsAnyAsync<MagickException>(() => engine.ExecuteAsync(Request("image.convert.jpeg", path), null, default));
        Assert.Single(Directory.GetFiles(directory));
        var source = Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.ExecuteAsync(Request("image.convert.jpeg", source), null, cancellation.Token));
        Assert.Equal(2, Directory.GetFiles(directory).Length);
    }
}
