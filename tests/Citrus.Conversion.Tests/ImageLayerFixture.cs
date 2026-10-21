using Citrus.Core.Editing;
using Citrus.Conversion.Editing;
using ImageMagick;

namespace Citrus.Conversion.Tests;

internal sealed class ImageLayerFixture : IDisposable
{
    internal readonly string Root = Path.Combine(Path.GetTempPath(), "citrus-layers-" + Guid.NewGuid().ToString("N"));
    internal static readonly ImageAdjustments Identity = new(1, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 7, PhotoEffect.None, 0);
    internal static readonly AnnotationStyle Red = new(new(255,0,0,255), new(255,0,0,255), 0, 1, null);
    internal string PathIn(string name) => Path.Combine(Root, name);
    internal ImageLayerFixture() => Directory.CreateDirectory(Root);
    internal async Task<EditorDocument> Create(int width = 80, int height = 60)
    {
        using var image = new MagickImage(MagickColors.White, (uint)width, (uint)height);
        image.SetProfile(ColorProfiles.SRGB);
        string path = PathIn(Guid.NewGuid() + ".png"); image.Write(path);
        var source = (await new SourceSnapshotService().CaptureAsync([path], default))[0].Snapshot!;
        return new(1, 3, [source], [], new ImageRecipe(source.Id, new(0,false,false,null,null,false), Identity, [],null,[],[]), new("png",null,"layers",new QualityGoal(90),MetadataPolicy.PreserveSupported,new(255,255,255,255)));
    }
    internal static byte[] Pixel(PreviewFrame frame, int x, int y) => frame.Bgra32.Skip(y * frame.Stride + x * 4).Take(4).ToArray();
    internal static byte[] Pixels(string path) { using var image = new MagickImage(path); return image.GetPixels().ToByteArray(PixelMapping.BGRA)!; }
    public void Dispose() => Directory.Delete(Root, true);
}
