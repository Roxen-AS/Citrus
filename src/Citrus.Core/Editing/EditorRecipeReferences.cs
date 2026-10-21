using System.Collections.Immutable;

namespace Citrus.Core.Editing;

/// <summary>References are derived from the recipe, never from source-array order.</summary>
public static class EditorRecipeReferences
{
    public static ImmutableArray<Guid> Roots(EditorRecipe recipe) => recipe switch
    {
        ImageRecipe image => [image.SourceId],
        CollageRecipe collage => collage.Items.IsDefault ? [] : [.. collage.Items.Select(i => i.SourceId).Distinct()],
        VideoRecipe video => ClipRoots(video.Clips),
        AudioRecipe audio => ClipRoots(audio.Clips),
        VisualizerRecipe visualizer => ClipRoots(visualizer.Audio.Clips),
        PdfRecipe pdf => pdf.Pages.IsDefault ? [] : [.. pdf.Pages.Select(p => p.SourceId).Distinct()],
        _ => []
    };
    private static ImmutableArray<Guid> ClipRoots(ImmutableArray<MediaClip> clips) => clips.IsDefault ? [] : [.. clips.Select(c => c.SourceId).Distinct()];
    public static ImmutableArray<Guid> Sources(EditorRecipe recipe)
    {
        var ids = Roots(recipe).ToBuilder();
        void Background(CanvasBackground? value) { if (value is ImageBackground image) ids.Add(image.SourceId); }
        switch (recipe)
        {
            case ImageRecipe image:
                Background(image.Canvas?.Background);
                if (!image.Annotations.IsDefault)
                    foreach (var annotation in image.Annotations)
                        if (annotation.Geometry is InsetImageGeometry inset) ids.Add(inset.SourceId);
                break;
            case CollageRecipe collage: Background(collage.Background); break;
            case VisualizerRecipe { BackgroundImageId: { } id }: ids.Add(id); break;
        }
        return [.. ids.Distinct()];
    }
    public static ImmutableArray<Guid> Fonts(EditorRecipe recipe) => recipe is ImageRecipe { Annotations.IsDefault: false } image
        ? [.. image.Annotations.Where(a => a.Style.Font is not null).Select(a => a.Style.Font!.ArtifactId).Distinct()] : [];
}
