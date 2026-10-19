using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Citrus.Core.Editing;

public sealed record PdfRecipe(ImmutableArray<PdfPageRef> Pages, PdfOutputMode Mode, int Dpi, PdfCompressionMode CompressionMode, int RasterQuality, ImmutableArray<MetadataEdit> MetadataEdits) : EditorRecipe;
public sealed record PdfPageRef(Guid Id, Guid SourceId, int OneBasedPage, int QuarterTurns);
public enum PdfOutputMode { Document, SeparatePageDocuments, PageImages, Text, Markdown }
public enum PdfCompressionMode { PreserveStructure, RasterRebuild }
