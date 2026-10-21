using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Citrus.Windows.Tests;

/// <summary>Original deterministic raster still life, decoded through the real file-preview service.</summary>
internal static class CitrusPreviewFixture
{
    internal static void Save(string path)
    {
        const int width = 640, height = 420;
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            double light = .96 - .08 * y / height;
            double shadow = Math.Exp(-Math.Pow((x - 331) / 153d, 2) - Math.Pow((y - 335) / 27d, 2)) * .23;
            double r = 225 * (light - shadow), g = 239 * (light - shadow), b = 242 * (light - shadow);
            PaintOrange(275, 236, 104, ref r, ref g, ref b);
            PaintOrange(400, 270, 69, ref r, ref g, ref b);
            double leafX = (x - 310) * .87 + (y - 124) * .5;
            double leafY = -(x - 310) * .5 + (y - 124) * .87;
            if (leafX * leafX / 3600 + leafY * leafY / 441 < 1)
            {
                double vein = Math.Abs(leafY) < 1.3 ? 1.3 : 1;
                double shade = .9 + leafY / 100;
                r = 37 * shade * vein; g = 112 * shade * vein; b = 63 * shade * vein;
            }
            int i = (y * width + x) * 4;
            pixels[i] = (byte)Math.Clamp(b, 0, 255); pixels[i + 1] = (byte)Math.Clamp(g, 0, 255);
            pixels[i + 2] = (byte)Math.Clamp(r, 0, 255); pixels[i + 3] = 255;

            void PaintOrange(double cx, double cy, double radius, ref double red, ref double green, ref double blue)
            {
                double nx = (x - cx) / radius, ny = (y - cy) / radius;
                double d = nx * nx + ny * ny;
                if (d >= 1) return;
                double z = Math.Sqrt(1 - d);
                double lighting = .66 + .34 * Math.Max(0, -.4 * nx - .5 * ny + .77 * z);
                double texture = Math.Sin(x * 2.17 + y * 3.51) * Math.Sin(y * 1.29 - x * .71) * 5;
                red = 255 * lighting + texture;
                green = (125 + 32 * z) * lighting + texture;
                blue = 19 * lighting + texture;
            }
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
