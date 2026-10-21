using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

public sealed class SingleBackingShaderTests
{
    [Fact]
    public void White_desktop_keeps_a_pale_neutral_backing_beneath_white_petals()
    {
        var scene = GlassScene.Wheel(7, .5);
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        byte[] source = Enumerable.Repeat((byte)255, scene.CaptureWidth * scene.CaptureHeight * 4).ToArray();
        byte[] target = new byte[scene.PixelWidth * scene.PixelHeight * 4];
        renderer.RenderPixels(source, scene.CaptureWidth, scene.CaptureHeight, 0, 0, new(), target);
        int center = (92 * scene.PixelWidth + 92) * 4;
        Assert.InRange(target[center + 2], 234, 237);
        Assert.InRange(target[center + 1], 238, 241);
        Assert.InRange(target[center], 238, 241);
        Assert.Equal(255, target[center + 3]);
    }

    [Fact]
    public void Pagination_visibility_masks_only_the_cached_capsule_without_changing_the_disk()
    {
        var scene = GlassScene.WheelPresentation(7, .5, true);
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        byte[] source = new byte[scene.CaptureWidth * scene.CaptureHeight * 4];
        for (int p = 0; p < source.Length; p += 4)
        { source[p] = 70; source[p + 1] = 120; source[p + 2] = 170; source[p + 3] = 255; }
        byte[] hidden = new byte[scene.PixelWidth * scene.PixelHeight * 4], visible = new byte[hidden.Length];
        renderer.RenderPixels(source, scene.CaptureWidth, scene.CaptureHeight, 0, 0, new(NavigationVisible: false), hidden);
        renderer.RenderPixels(source, scene.CaptureWidth, scene.CaptureHeight, 0, 0, new(NavigationVisible: true), visible);
        int nav = ((int)(390 * scene.Scale) * scene.PixelWidth + (int)(184 * scene.Scale)) * 4;
        Assert.Equal(0, hidden[nav + 3]); Assert.Equal(255, visible[nav + 3]);
        for (int y = 0; y < 368 * scene.Scale; y++)
            Assert.Equal(hidden.AsSpan(y * scene.PixelWidth * 4, scene.PixelWidth * 4).ToArray(),
                visible.AsSpan(y * scene.PixelWidth * 4, scene.PixelWidth * 4).ToArray());
    }
}
