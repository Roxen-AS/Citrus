using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using Citrus.Conversion;
using Citrus.Conversion.Editing.Media;
using Citrus.Core;
using Citrus.Windows.Editors;
using Citrus.Windows.Editors.Media;
using Border = System.Windows.Controls.Border;
using Button = System.Windows.Controls.Button;
using Grid = System.Windows.Controls.Grid;
using Image = System.Windows.Controls.Image;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class VideoCropNoticeLayoutTests(DesktopFixture desktop)
{
    [Fact]
    public Task Crop_audio_notice_keeps_the_video_plane_stable_and_passes_pointer_to_resize_handles() => desktop.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "citrus-crop-notice-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var process = MediaProcess.Bundled(); string raw = Path.Combine(root, "frames.bgra"), input = Path.Combine(root, "source.mp4");
            await File.WriteAllBytesAsync(raw, Enumerable.Repeat(new byte[] { 32, 64, 128, 255 }, 32 * 24 * 12).SelectMany(pixel => pixel).ToArray());
            await process.RunAsync(["-f", "rawvideo", "-pixel_format", "bgra", "-video_size", "32x24", "-framerate", "12", "-i", raw, "-c:v", "mpeg4", input], null, default);
            var window = new VideoCropWindow(new(process, await process.DiscoverCapabilitiesAsync(default), new())); Task? checking = null;
            window.Loaded += (_, _) => checking = Check();
            await window.ShowEditorAsync(new(VideoCropEngine.Operation, [input], "Crop", new(), root, SubmissionOrigin.Home), default); await checking!;
            async Task Check()
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    while (!((Button)window.FindName("ApplyButton")).IsEnabled) await Task.Delay(20, timeout.Token);
                    window.UpdateLayout(); var area = (Grid)window.FindName("PreviewArea"); var handles = (CropOverlay)window.FindName("CropHandles");
                    Assert.IsAssignableFrom<BitmapSource>(((Image)window.FindName("PreviewFrame")).Source);
                    var beforeArea = Bounds(area); var beforeHandles = Bounds(handles);
                    const string notice = "Audio output is unavailable. Preview continues silently; the cropped copy keeps every original audio channel. This complete notice is available without moving the video.";
                    typeof(VideoCropWindow).GetMethod("Notice", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [notice]);
                    window.UpdateLayout(); Assert.Equal(beforeArea, Bounds(area)); Assert.Equal(beforeHandles, Bounds(handles));
                    var overlay = Assert.IsType<Border>(window.FindName("PlaybackNoticeOverlay"));
                    Assert.False(overlay.IsHitTestVisible); VisualArtifacts.AssertInside(overlay, area);
                    Assert.Equal(notice, area.ToolTip); Assert.Equal(notice, System.Windows.Automation.AutomationProperties.GetName((TextBlock)window.FindName("PlaybackNotice")));
                    double scale = Math.Min(area.ActualWidth / 32, area.ActualHeight / 24);
                    var handlePoint = new System.Windows.Point(area.ActualWidth / 2 + 16 * scale - 10, area.ActualHeight / 2 + 12 * scale - 10);
                    Assert.Same(handles, area.InputHitTest(handlePoint));
                    Rect Bounds(FrameworkElement element) => element.TransformToAncestor(window).TransformBounds(new Rect(new System.Windows.Point(), element.RenderSize));
                }
                finally { window.Close(); }
            }
        }
        finally { Directory.Delete(root, true); }
    });
}
