using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Citrus.Core.Editing;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ImageRecipe), "image")]
[JsonDerivedType(typeof(CollageRecipe), "collage")]
[JsonDerivedType(typeof(VideoRecipe), "video")]
[JsonDerivedType(typeof(AudioRecipe), "audio")]
[JsonDerivedType(typeof(VisualizerRecipe), "visualizer")]
[JsonDerivedType(typeof(PdfRecipe), "pdf")]
public abstract record EditorRecipe;
