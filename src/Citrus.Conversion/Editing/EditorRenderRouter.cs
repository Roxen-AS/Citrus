using Citrus.Core;
using Citrus.Core.Editing;

namespace Citrus.Conversion.Editing;

/// <summary>Explicitly injected renderer dispatch; construction does not register UI routes or queue jobs.</summary>
public sealed class EditorRenderRouter : IEditorRenderer
{
    private readonly IReadOnlyDictionary<Type,IEditorRenderer> renderers;
    public EditorRenderRouter(IReadOnlyDictionary<Type,IEditorRenderer> renderers)
    {
        ArgumentNullException.ThrowIfNull(renderers);
        Type[] families = [typeof(ImageRecipe),typeof(CollageRecipe),typeof(VideoRecipe),typeof(AudioRecipe),typeof(VisualizerRecipe),typeof(PdfRecipe)];
        if (renderers.Any(pair => !families.Contains(pair.Key) || pair.Value is null)) throw new ArgumentException("Only known recipe families may be registered.");
        this.renderers = new Dictionary<Type,IEditorRenderer>(renderers);
    }
    public async Task<PreviewFrame> RenderPreviewAsync(EditorDocument document,PreviewRequest request,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request.Bounds.Width <= 0 || request.Bounds.Height <= 0 || request.Bounds.Width > 2048 || request.Bounds.Height > 2048) throw new ArgumentException("Preview bounds exceed the preview budget.");
        return await Renderer(document).RenderPreviewAsync(document,request,ct).ConfigureAwait(false);
    }
    public async Task<StagedExport> RenderExportAsync(EditorDocument document,string privateDirectory,IProgress<ConversionProgress>? progress,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await Renderer(document).RenderExportAsync(document,privateDirectory,progress,ct).ConfigureAwait(false);
    }
    private IEditorRenderer Renderer(EditorDocument document)
    {
        if (EditorRecipeValidator.Validate(document).Length > 0) throw new ArgumentException("Invalid editor document.");
        return renderers.TryGetValue(document.Recipe.GetType(),out var renderer) ? renderer : throw new NotSupportedException("No enabled renderer for this editor family.");
    }
}
