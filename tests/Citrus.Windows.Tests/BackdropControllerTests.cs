using Citrus.Core.Presentation;
using Citrus.Windows.Presentation;
using System.Windows;
using System.Windows.Interop;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class BackdropControllerTests(DesktopFixture desktop)
{
    internal static MaterialDecision Eligible => MaterialPolicy.Resolve(new(), new(22621, true, true, true, false, false, true));

    [Fact]
    public void Dwm_none_is_one_not_auto()
    {
        var api = new RecordingBackdropApi();
        var result = BackdropConfiguration.Apply(api, 123, Eligible with { UseNativeAcrylic = false }, false);
        Assert.False(result.IsNative);
        Assert.Contains((38, 1), api.Attributes);
        Assert.DoesNotContain((38, 0), api.Attributes);
        Assert.Equal(0, api.Extensions.Last());
    }

    [Theory]
    [InlineData(20)] [InlineData(38)] [InlineData(-1)]
    public void Unavailable_winrt_or_failed_dwm_call_falls_back(int failingAttribute)
    {
        var api = new RecordingBackdropApi(failingAttribute);
        var result = BackdropConfiguration.Apply(api, 123, Eligible, true);
        Assert.False(result.IsNative);
        Assert.Equal(unchecked((int)0x80070057), result.HResult);
        Assert.Equal((38, 1), api.Attributes.Last());
        Assert.Equal(0, api.Extensions.Last());
    }

    [Fact]
    public Task Ordinary_window_backdrop_does_not_set_layered_style() => desktop.RunAsync(() =>
    {
        var window = new Window { Width = 200, Height = 150, ShowActivated = false };
        using var controller = new WindowBackdropController();
        try
        {
            controller.Attach(window);
            window.Show();
            var hwnd = new WindowInteropHelper(window).Handle;
            var before = BackdropNativeMethods.GetWindowLongPtr(hwnd, -20);
            controller.Apply(Eligible, false);
            Assert.Equal(before, BackdropNativeMethods.GetWindowLongPtr(hwnd, -20));
            Assert.Equal(0L, before.ToInt64() & BackdropNativeMethods.WsExLayered);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    private sealed class RecordingBackdropApi(int failingAttribute = int.MinValue) : IBackdropApi
    {
        public List<(int Attribute, int Value)> Attributes { get; } = [];
        public List<int> Extensions { get; } = [];
        public int SetAttribute(nint hwnd, int attribute, int value)
        {
            Attributes.Add((attribute, value));
            return attribute == failingAttribute && (attribute != 38 || value != 1) ? unchecked((int)0x80070057) : 0;
        }
        public int ExtendFrame(nint hwnd, int margin)
        {
            Extensions.Add(margin);
            return failingAttribute == -1 && margin == -1 ? unchecked((int)0x80070057) : 0;
        }
    }
}
