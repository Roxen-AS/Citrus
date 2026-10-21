using Citrus.Core;
using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

public sealed class ReferenceWheelOrderingTests
{
    [Fact]
    public void Nine_video_tools_remain_on_one_page_in_reference_order()
    {
        string[] ids = ["compress", "metadata", "mute", "trim", "crop", "speed", "snapshot", "split", "redact"];
        var operations = ids.Reverse().Select(id => new OperationDescriptor("video." + id, id, OperationMode.Tools, null, false, true)).ToArray();
        var pages = WheelMenuLayout.Build(operations, OperationMode.Tools, ["sample.mp4"]);
        Assert.Single(pages);
        Assert.Equal(ids.Select(id => "video." + id), pages[0].Select(operation => operation.Id));
    }

    [Fact]
    public void Metadata_opens_a_viewer_while_direct_strip_keeps_an_honest_label()
    {
        Assert.Equal("METADATA", WheelDisplayLabels.Petal(new("image.metadata", "Metadata", OperationMode.Tools, null, false, true)));
        Assert.Equal("STRIP", WheelDisplayLabels.Petal(new("image.strip", "Strip metadata", OperationMode.Tools, null, false, false)));
        Assert.Equal("ADD BG", WheelDisplayLabels.Petal(new("image.background", "Add background", OperationMode.Tools, null, false, true)));
    }
}
