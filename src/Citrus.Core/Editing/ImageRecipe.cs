using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Citrus.Core.Editing;

public sealed record ImageRecipe(Guid SourceId, ImageGeometry Geometry, ImageAdjustments Adjustments, ImmutableArray<PixelMask> Masks, CanvasSpec? Canvas, ImmutableArray<Annotation> Annotations, ImmutableArray<MetadataEdit> MetadataEdits) : EditorRecipe;
public sealed record ImageGeometry(int QuarterTurns, bool FlipX, bool FlipY, RectD? Crop, SizeI? OutputSize, bool AllowUpscale);
public sealed record ImageAdjustments(int AlgorithmVersion, double ExposureEv, double Brightness, double Contrast, double Gamma, double Highlights, double Shadows, double Saturation, double Vibrance, double HueDegrees, double Warmth, double Tint, double Clarity, double Dehaze, double Sharpen, double Denoise, double BlurSigma, double Vignette, double Grain, uint GrainSeed, PhotoEffect Effect, double EffectAmount);
public enum PhotoEffect { None, Grayscale, Sepia, Invert, Posterize }
public sealed record PixelMask(Guid Id, RectD Bounds, MaskEffect Effect, Rgba32 Fill, double Radius);
public enum MaskEffect { Solid, Gaussian, Pixelate }
public sealed record CanvasSpec(SizeI Size, CanvasBackground Background, RectD Placement, ImageFit Fit, double CornerRadius, ShadowSpec? Shadow);
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TransparentBackground), "transparent")]
[JsonDerivedType(typeof(SolidBackground), "solid")]
[JsonDerivedType(typeof(GradientBackground), "gradient")]
[JsonDerivedType(typeof(ImageBackground), "image")]
public abstract record CanvasBackground;
public sealed record TransparentBackground : CanvasBackground;
public sealed record SolidBackground(Rgba32 Color) : CanvasBackground;
public sealed record GradientBackground(double Angle, ImmutableArray<GradientStop> Stops) : CanvasBackground;
public sealed record ImageBackground(Guid SourceId, ImageFit Fit, PointD Anchor, double Opacity, double BlurSigma) : CanvasBackground;
public sealed record GradientStop(double Position, Rgba32 Color);
public sealed record ShadowSpec(double X, double Y, double Sigma, Rgba32 Color);
public enum ImageFit { Contain, Cover, Stretch }
public sealed record Annotation(Guid Id, bool Visible, AnnotationGeometry Geometry, AnnotationStyle Style);
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(StrokeGeometry), "stroke")]
[JsonDerivedType(typeof(ShapeGeometry), "shape")]
[JsonDerivedType(typeof(TextGeometry), "text")]
[JsonDerivedType(typeof(NumberGeometry), "number")]
[JsonDerivedType(typeof(InsetImageGeometry), "image")]
[JsonDerivedType(typeof(MagnifierGeometry), "magnifier")]
public abstract record AnnotationGeometry;
public sealed record StrokeGeometry(ImmutableArray<PointD> Points, bool ArrowStart, bool ArrowEnd) : AnnotationGeometry;
public sealed record ShapeGeometry(RectD Bounds, bool Ellipse, double Radius) : AnnotationGeometry;
public sealed record TextGeometry(PointD Origin, string Text, double WrapWidth, double Rotation) : AnnotationGeometry;
public sealed record NumberGeometry(RectD Bounds, int Number) : AnnotationGeometry;
public sealed record InsetImageGeometry(Guid SourceId, RectD Bounds) : AnnotationGeometry;
public sealed record MagnifierGeometry(RectD SampleBounds, RectD LensBounds, double Scale) : AnnotationGeometry;
public sealed record AnnotationStyle(Rgba32 Stroke, Rgba32 Fill, double StrokeWidth, double Opacity, FontSpec? Font);
public sealed record FontSpec(Guid ArtifactId, double SizePixels, double LineHeight);
