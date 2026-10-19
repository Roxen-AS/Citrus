using System.IO;
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
}
