using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public class FilePreviewTests(DesktopFixture desktop)
{
    [Theory]
    [InlineData(2000,1000)]
    [InlineData(1000,2000)]
    public Task Decode_is_frozen_bounded_and_preserves_aspect_ratio(int width,int height) => desktop.RunAsync(async () =>
    {
        string path = Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".png");
        try
        {
            var source = BitmapSource.Create(width,height,96,96,PixelFormats.Gray8,null,new byte[width*height],width);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
            using (var stream=File.Create(path)) encoder.Save(stream);
            var result = Assert.IsAssignableFrom<BitmapSource>(await new FilePreviewService().LoadAsync(path,192,CancellationToken.None));
            Assert.True(result.IsFrozen);
            Assert.InRange(Math.Max(result.PixelWidth,result.PixelHeight),1,192);
            Assert.Equal(width/(double)height,result.PixelWidth/(double)result.PixelHeight,2);
            // OnLoad must release the stream; exclusive reopen succeeds immediately.
            using var exclusive = new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        }
        finally { File.Delete(path); }
    });
    [Fact]
    public async Task Canceled_preview_does_not_touch_a_missing_file()
    {
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FilePreviewService().LoadAsync("missing.png",192,canceled.Token));
    }
    [Fact]
    public async Task Corrupt_and_oversized_previews_fall_back_without_throwing()
    {
        string path = Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".png");
        try
        {
            await File.WriteAllTextAsync(path,"not an image");
            Assert.Null(await new FilePreviewService().LoadAsync(path,192,CancellationToken.None));
            using (var stream=File.Create(path)) stream.SetLength(33L*1024*1024);
            Assert.Null(await new FilePreviewService().LoadAsync(path,192,CancellationToken.None));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public Task Offline_raster_is_rejected_before_decode() => desktop.RunAsync(async () =>
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            WriteRaster(path);
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Offline);
            Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.Offline));
            Assert.Null(await new FilePreviewService().LoadAsync(path, 192, CancellationToken.None));
        }
        finally { if (File.Exists(path)) { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); } }
    });

    [Fact]
    public Task Availability_is_rechecked_after_waiting_for_a_decoder_worker() => desktop.RunAsync(async () =>
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        // Occupy the real shared limiter from test code; no injected production hooks or OS preference changes.
        using var first = await Citrus.Core.Editing.PreviewWorkScheduler.AcquireAsync(default);
        using var second = await Citrus.Core.Editing.PreviewWorkScheduler.AcquireAsync(default);
        Task<System.Windows.Media.ImageSource?>? queued = null;
        try
        {
            WriteRaster(path);
            queued = new FilePreviewService().LoadAsync(path, 192, CancellationToken.None);
            Assert.False(queued.IsCompleted);
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Offline);
            Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.Offline));
        }
        finally { first.Dispose(); second.Dispose(); }
        try { Assert.Null(await queued!); }
        finally { if (File.Exists(path)) { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); } }
    });

    [Fact]
    public Task Available_local_reparse_image_is_allowed_and_directory_is_rejected() => desktop.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "citrus-preview-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            string original = Path.Combine(root, "source.png"), link = Path.Combine(root, "link.png"), directory = Path.Combine(root, "folder.png");
            WriteRaster(original);
            File.CreateSymbolicLink(link, original);
            Assert.True(File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint));
            Assert.NotNull(await new FilePreviewService().LoadAsync(link, 192, CancellationToken.None));
            Directory.CreateDirectory(directory);
            Assert.Null(await new FilePreviewService().LoadAsync(directory, 192, CancellationToken.None));
        }
        finally { Directory.Delete(root, true); }
    });

    [Theory]
    [InlineData(1, "RGBY")]
    [InlineData(2, "GRYB")]
    [InlineData(3, "YBGR")]
    [InlineData(4, "BYRG")]
    [InlineData(5, "RBGY")]
    [InlineData(6, "BRYG")]
    [InlineData(7, "YGBR")]
    [InlineData(8, "GYRB")]
    public Task Exif_orientation_preserves_expected_corner_pixels_and_bounded_display_size(int orientation, string corners) => desktop.RunAsync(async () =>
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg");
        try
        {
            const int width = 60, height = 40;
            byte[] pixels = new byte[width * height * 3];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var color = CornerColor(y < height / 2 ? (x < width / 2 ? 'R' : 'G') : (x < width / 2 ? 'B' : 'Y'));
                int offset = (y * width + x) * 3;
                pixels[offset] = color.R; pixels[offset + 1] = color.G; pixels[offset + 2] = color.B;
            }
            var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, pixels, width * 3);
            var metadata = new BitmapMetadata("jpg");
            metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)orientation);
            var encoder = new JpegBitmapEncoder { QualityLevel = 100 };
            encoder.Frames.Add(BitmapFrame.Create(source, null, metadata, null));
            using (var stream = File.Create(path)) encoder.Save(stream);
            using (var stream = File.OpenRead(path))
            {
                var fixture = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Assert.Equal((ushort)orientation, ((BitmapMetadata)fixture.Frames[0].Metadata).GetQuery("/app1/ifd/{ushort=274}"));
            }
            var result = Assert.IsAssignableFrom<BitmapSource>(await new FilePreviewService().LoadAsync(path, 30, CancellationToken.None));
            Assert.True(result.IsFrozen);
            Assert.Equal(orientation >= 5 ? 20 : 30, result.PixelWidth);
            Assert.Equal(orientation >= 5 ? 30 : 20, result.PixelHeight);
            var converted = new FormatConvertedBitmap(result, PixelFormats.Bgra32, null, 0);
            byte[] actual = new byte[result.PixelWidth * result.PixelHeight * 4];
            converted.CopyPixels(actual, result.PixelWidth * 4, 0);
            (int X, int Y)[] locations = [(3, 3), (result.PixelWidth - 4, 3), (3, result.PixelHeight - 4), (result.PixelWidth - 4, result.PixelHeight - 4)];
            for (int i = 0; i < locations.Length; i++)
            {
                int offset = (locations[i].Y * result.PixelWidth + locations[i].X) * 4;
                var expected = CornerColor(corners[i]);
                Assert.InRange(Math.Abs(actual[offset] - expected.B), 0, 35);
                Assert.InRange(Math.Abs(actual[offset + 1] - expected.G), 0, 35);
                Assert.InRange(Math.Abs(actual[offset + 2] - expected.R), 0, 35);
            }
        }
        finally { File.Delete(path); }
    });

    private static (byte R, byte G, byte B) CornerColor(char corner) => corner switch
    {
        'R' => (255, 0, 0), 'G' => (0, 255, 0), 'B' => (0, 0, 255), _ => (255, 255, 0)
    };

    private static void WriteRaster(string path)
    {
        var source = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Gray8, null, new byte[] { 30, 70, 110, 150 }, 2);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
