using System.Collections.Immutable;
using System.Security.Cryptography;
using Citrus.Core.Editing;
using Citrus.Conversion.Editing.Image;

namespace Citrus.Conversion.Tests;

public sealed class FontArtifactTests
{
    [Fact]
    public async Task Two_artifacts_stage_exact_distinct_bytes_and_capture_correct_collection_face()
    {
        using var f=new ImageLayerFixture(); var service=new FontArtifactService();
        var one=await service.CaptureAsync(AnnotationRenderingTests.Font(1).OriginalPath,0,default);
        var two=await service.CaptureAsync(AnnotationRenderingTests.Font(2).OriginalPath,0,default);
        var staged=await service.StageVerifiedAsync([one,two],f.PathIn("stage"),default);
        Assert.Equal(2,staged.Count); Assert.NotEqual(one.Sha256,two.Sha256);
        Assert.Equal(File.ReadAllBytes(one.OriginalPath),File.ReadAllBytes(staged[one.Id]));
        Assert.Equal(File.ReadAllBytes(two.OriginalPath),File.ReadAllBytes(staged[two.Id]));
        var collection=AnnotationRenderingTests.Font(collection:true);
        var face=await service.CaptureAsync(collection.OriginalPath,1,default);
        Assert.Equal("Citrus Fixture 2",face.Family); Assert.Equal(1,face.FaceIndex);
        await Assert.ThrowsAsync<ArgumentException>(()=>service.CaptureAsync(collection.OriginalPath,2,default));
        await Assert.ThrowsAsync<ArgumentException>(()=>service.CaptureAsync(one.OriginalPath,1,default));
    }

    [Theory]
    [InlineData("ABC中文012",true)]
    [InlineData("AB\n中文",true)]
    [InlineData("Q",false)]
    [InlineData("A\u0301",false)]
    [InlineData("שלום",false)]
    [InlineData("العربية",false)]
    [InlineData("😀",false)]
    [InlineData("क",false)]
    [InlineData("A\u200DB",false)]
    public void Coverage_and_supported_text_are_checked_without_fallback(string text,bool supported)
    {
        var issues=FontTextValidator.Validate(AnnotationRenderingTests.Font(),text);
        Assert.Equal(supported,issues.IsEmpty);
        if(!supported) Assert.Contains(issues,i=>i.Message.Contains("U+",StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("fvar","Variable")]
    [InlineData("COLR","Color")]
    public async Task Variable_and_color_fonts_fail_with_concrete_reason(string table,string reason)
    {
        using var f=new ImageLayerFixture(); byte[] bytes=File.ReadAllBytes(AnnotationRenderingTests.Font().OriginalPath);
        System.Text.Encoding.ASCII.GetBytes(table).CopyTo(bytes,12); string path=f.PathIn("unsupported.ttf");File.WriteAllBytes(path,bytes);
        var error=await Assert.ThrowsAsync<NotSupportedException>(()=>new FontArtifactService().CaptureAsync(path,0,default));
        Assert.Contains(reason,error.Message,StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changed_missing_and_cancelled_fonts_leave_no_partial_owned_stage()
    {
        using var f=new ImageLayerFixture(); var one=AnnotationRenderingTests.Font(); var two=AnnotationRenderingTests.Font(2) with {Sha256=new string('0',64)};
        var service=new FontArtifactService(); string stage=f.PathIn("stage");
        await Assert.ThrowsAsync<IOException>(()=>service.StageVerifiedAsync([one,two],stage,default)); Assert.False(Directory.Exists(stage));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.StageVerifiedAsync([one],stage,cancelled.Token)); Assert.False(Directory.Exists(stage));
        var missing=one with {OriginalPath=f.PathIn("missing.ttf")};
        Assert.Contains(FontTextValidator.Validate(missing,"ABC"),i=>i.FieldPath=="Font");
        await Assert.ThrowsAnyAsync<IOException>(()=>service.StageVerifiedAsync([missing],stage,default)); Assert.False(Directory.Exists(stage));
    }

    [Fact]
    public async Task Malformed_exact_hash_font_returns_validation_issue_and_cannot_be_staged()
    {
        using var fixture = new ImageLayerFixture();
        string path = fixture.PathIn("malformed.ttf");
        byte[] bytes = [0, 0, 0, 0];
        File.WriteAllBytes(path, bytes);
        var artifact = new FontArtifact(Guid.NewGuid(), path, bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)), 0, "Malformed fixture", "Regular");

        var issue = Assert.Single(FontTextValidator.Validate(artifact, "A"));
        Assert.Equal("Font", issue.FieldPath);
        Assert.Contains("Invalid static TrueType/OpenType font face", issue.Message, StringComparison.Ordinal);

        string stage = fixture.PathIn("stage");
        await Assert.ThrowsAsync<InvalidDataException>(() => new FontArtifactService().StageVerifiedAsync([artifact], stage, default));
        Assert.False(Directory.Exists(stage));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Captured_font_replaced_by_directory_returns_validation_issue_and_cannot_be_staged()
    {
        using var fixture = new ImageLayerFixture();
        string path = fixture.PathIn("selected.ttf");
        byte[] original = File.ReadAllBytes(AnnotationRenderingTests.Font().OriginalPath);
        File.WriteAllBytes(path, original);
        var service = new FontArtifactService();
        var artifact = await service.CaptureAsync(path, 0, default);
        Assert.Empty(FontTextValidator.Validate(artifact, "ABC中文"));
        var validStage = await service.StageVerifiedAsync([artifact], fixture.PathIn("valid-stage"), default);
        Assert.Equal(original, File.ReadAllBytes(validStage[artifact.Id]));

        File.Delete(path);
        Directory.CreateDirectory(path);
        var issue = Assert.Single(FontTextValidator.Validate(artifact, "A"));
        Assert.Equal("Font", issue.FieldPath);
        Assert.Contains(path, issue.Message, StringComparison.Ordinal);

        string deniedStage = fixture.PathIn("denied-stage");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.StageVerifiedAsync([artifact], deniedStage, default));
        Assert.False(Directory.Exists(deniedStage));
        Assert.True(Directory.Exists(path));
        Assert.Equal(original, File.ReadAllBytes(validStage[artifact.Id]));
    }
}
