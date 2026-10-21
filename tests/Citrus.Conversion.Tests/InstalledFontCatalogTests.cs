using Citrus.Windows.Editors.Image;

namespace Citrus.Conversion.Tests;

public sealed class InstalledFontCatalogTests
{
    [Fact]
    public async Task Catalog_uses_actual_family_and_style_with_exact_file_selection_kept_separate()
    {
        string directory=Path.Combine(AppContext.BaseDirectory,"Fixtures","Fonts");using var catalog=new InstalledFontCatalog(default,[directory]);
        var choices=await catalog.GetAsync(default);Assert.Contains(choices,c=>c.Family=="Citrus Fixture 1"&&c.Style=="Regular"&&Path.GetFileName(c.Path)=="Fixture1.ttf");
        Assert.DoesNotContain(choices,c=>Path.GetExtension(c.Path).Equals(".ttc",StringComparison.OrdinalIgnoreCase));
        Assert.All(choices,c=>Assert.DoesNotContain(".ttf",c.Label));
        var selected=choices.Single(c=>Path.GetFileName(c.Path)=="Fixture1.ttf");var captured=await new Citrus.Conversion.Editing.Image.FontArtifactService().CaptureAsync(selected.Path,0,default);
        Assert.Equal(selected.Family,captured.Family);Assert.Equal(Path.GetFullPath(selected.Path),captured.OriginalPath);Assert.Equal(64,captured.Sha256.Length);
    }
    [Fact]
    public async Task Closed_or_precancelled_catalog_leaves_no_usable_new_choices()
    {
        using var cancel=new CancellationTokenSource();cancel.Cancel();using var catalog=new InstalledFontCatalog(cancel.Token,[Path.Combine(AppContext.BaseDirectory,"Fixtures","Fonts")]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>catalog.GetAsync(cancel.Token));catalog.Dispose();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>catalog.GetAsync(default));
    }
}
