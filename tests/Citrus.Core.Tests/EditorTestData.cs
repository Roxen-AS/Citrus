using System.Collections.Immutable;
using Citrus.Core.Editing;

namespace Citrus.Core.Tests;

internal static class EditorTestData
{
    public static SourceSnapshot Source(string path = "/tmp/source.png") => new(Guid.NewGuid(), path, 4, 1, new string('A', 64));
    public static ImageAdjustments Identity => new(1, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, PhotoEffect.None, 0);
    public static EditorDocument Image(SourceSnapshot? source = null)
    {
        source ??= Source();
        return new(1, 0, [source], [], new ImageRecipe(source.Id, new(0, false, false, null, null, false), Identity, [], null, [], []),
            new(Path.GetExtension(source.Path)[1..], null, Path.GetFileNameWithoutExtension(source.Path) + "-edited", new QualityGoal(85), MetadataPolicy.PreserveSupported, new(255,255,255,255)));
    }
    public static ImageRecipe Recipe(EditorDocument doc) => Assert.IsType<ImageRecipe>(doc.Recipe);
}
