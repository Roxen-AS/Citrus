using Citrus.Core;
using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

public sealed class WheelMenuLayoutTests
{
    [Fact]
    public void Small_pdf_family_keeps_its_reference_geometry_separate_from_archives()
    {
        OperationDescriptor[] operations = [new("pdf.images", "Page images", OperationMode.Convert, ".png", false, true), new("pdf.text", "Text", OperationMode.Convert, ".txt", false, true),
            .. new[] { "zip", "tar", "gz", "bz2", "xz", "7z" }.Select(format => new OperationDescriptor("archive." + format, format.ToUpperInvariant(), OperationMode.Convert, "." + format, true, false))];
        var pages = WheelMenuLayout.Build(operations, OperationMode.Convert, ["document.pdf"]);
        Assert.Equal(new[] { 2, 6 }, pages.Select(page => page.Count));
        Assert.Equal(operations.Select(operation => operation.Id), pages.SelectMany(page => page).Select(operation => operation.Id));
    }

    [Theory]
    [InlineData("pdf.images", "Page images", ".png", "PNG")]
    [InlineData("pdf.text", "Text", ".txt", "TXT")]
    [InlineData("archive.tar.gz", "TAR + gzip", ".tar.gz", "TAR.GZ")]
    public void Format_targets_use_complete_concise_destination_names(string id, string name, string extension, string expected)
    {
        Assert.Equal(expected, WheelDisplayLabels.Petal(new(id, name, OperationMode.Convert, extension, false, true)));
    }

    [Fact]
    public void Video_primary_page_matches_reference_while_extra_formats_remain_available()
    {
        string[] formats = ["mp4", "mov", "mkv", "webm", "avi", "wmv", "gif", "mp3", "m4a"];
        var operations = formats.Select(format => new OperationDescriptor("media.convert." + format, format.ToUpperInvariant(), OperationMode.Convert, "." + format, false, false)).ToArray();
        var pages = WheelMenuLayout.Build(operations, OperationMode.Convert, ["geese.mov"]);
        Assert.Equal(new[] { "mp4", "mkv", "webm", "avi", "wmv", "gif", "mp3" }, pages[0].Select(operation => operation.OutputExtension!.TrimStart('.')));
        Assert.Equal(operations.Length - 1, pages.Sum(page => page.Count));
        Assert.Contains(pages.Skip(1).SelectMany(page => page), operation => operation.OutputExtension == ".m4a");
    }

    [Fact]
    public void Current_format_is_omitted_and_a_single_archive_keeps_its_own_family()
    {
        OperationDescriptor[] operations = [Convert("png"), Convert("jpeg"), Convert("webp"), Convert("tiff"), Convert("bmp"), Convert("gif"), Convert("avif"), new("archive.zip", "ZIP", OperationMode.Convert, ".zip", true, false), new("pdf.create", "PDF", OperationMode.Convert, ".pdf", false, false)];
        var pages = WheelMenuLayout.Build(operations, OperationMode.Convert, ["photo.png"]);
        Assert.Equal(new[] { 5, 2, 1 }, pages.Select(page => page.Count));
        Assert.Equal(new[] { "image.convert.webp", "image.convert.tiff", "image.convert.avif", "image.convert.bmp", "pdf.create" }, pages[0].Select(operation => operation.Id));
        Assert.Equal(new[] { "image.convert.jpeg", "image.convert.gif" }, pages[1].Select(operation => operation.Id));
        Assert.DoesNotContain(pages.SelectMany(page => page), operation => operation.Id == "image.convert.png");
        Assert.Equal("archive.zip", pages[2][0].Id);
    }
    [Fact]
    public void Jpg_alias_is_omitted_only_when_every_source_already_has_that_format()
    {
        OperationDescriptor[] operations = [Convert("jpeg"), Convert("png")];
        Assert.DoesNotContain(WheelMenuLayout.Build(operations, OperationMode.Convert, ["first.jpg", "second.jpeg"]).SelectMany(page => page), operation => operation.Id == "image.convert.jpeg");
        Assert.Contains(WheelMenuLayout.Build(operations, OperationMode.Convert, ["first.jpg", "second.png"]).SelectMany(page => page), operation => operation.Id == "image.convert.jpeg");
    }
    private static OperationDescriptor Convert(string format) => new("image.convert." + format, format.ToUpperInvariant(), OperationMode.Convert, "." + format, false, false);
}
