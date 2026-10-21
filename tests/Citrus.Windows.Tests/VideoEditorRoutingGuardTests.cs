using Citrus.Core;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class VideoEditorRoutingGuardTests(DesktopFixture desktop)
{
    [Theory]
    [InlineData("video.trim")]
    [InlineData("video.resize")]
    [InlineData("video.speed")]
    [InlineData("video.compress")]
    public Task Video_tools_cannot_regress_to_the_generic_numeric_options_form(string id) => desktop.RunAsync(() =>
    {
        var draft=new SubmissionDraft(new(id,"Video tool",OperationMode.Tools,".mp4",false,true),["clip.mp4"],new(),null,[]);
        var failure=Assert.Throws<ArgumentException>(()=>new ToolOptionsWindow(draft));
        Assert.Contains("video editor",failure.Message,StringComparison.OrdinalIgnoreCase);
        return Task.CompletedTask;
    });
}
