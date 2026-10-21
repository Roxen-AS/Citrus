using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Citrus.Core.Editing;

public sealed record CollageRecipe(ImmutableArray<CollageItem> Items, CollageLayout Layout, SizeI Size, int Columns, double Margin, double Gap, Guid? FeaturedItemId, double FeaturedFraction, CanvasBackground Background, double CornerRadius, ShadowSpec? Shadow) : EditorRecipe;
public sealed record CollageItem(Guid Id, Guid SourceId, RectD? Crop, ImageFit Fit, PointD Anchor, double Weight);
public enum CollageLayout { Grid, Row, Column, FeaturedLeft, FeaturedTop }
