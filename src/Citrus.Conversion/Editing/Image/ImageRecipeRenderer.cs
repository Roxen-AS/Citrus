using PointD = Citrus.Core.Editing.PointD;
using System.Collections.Immutable;
using Citrus.Core;
using Citrus.Core.Editing;
using ImageMagick;

namespace Citrus.Conversion.Editing.Image;

/// <summary>Own one instance per editor session. Cached verified source copies live until disposal;
/// export always stages and verifies the original snapshots again in the supplied private scope.</summary>
public sealed class ImageRecipeRenderer : IEditorRenderer, IDisposable, IAsyncDisposable
{
    private readonly ISourceSnapshotService sources;
    private readonly string cacheDirectory;
    private readonly SemaphoreSlim serial = new(1, 1);
    private readonly Dictionary<string, ImmutableDictionary<Guid, string>> cached = new();
    private bool ownsCache, disposed;
    public ImageRecipeRenderer(ISourceSnapshotService sources, string? cacheDirectory = null)
    {
        this.sources = sources ?? throw new ArgumentNullException(nameof(sources));
        this.cacheDirectory = Path.GetFullPath(cacheDirectory ?? Path.Combine(Path.GetTempPath(), "citrus-image-preview-" + Guid.NewGuid().ToString("N")));
        SafeEditorFiles.RejectLinks(this.cacheDirectory);
        if (File.Exists(this.cacheDirectory) || Directory.Exists(this.cacheDirectory)) throw new IOException("Preview cache must be a fresh owned directory.");
    }
    public async Task<PreviewFrame> RenderPreviewAsync(EditorDocument document, PreviewRequest request, CancellationToken ct)
    {
        Validate(document);
        if (request.Bounds.Width is <= 0 or > 2048 || request.Bounds.Height is <= 0 or > 2048 || request.Time is not null || request.PageId is not null) throw new ArgumentException("Invalid raster preview request.");
        using var worker = await PreviewWorkScheduler.AcquireAsync(ct).ConfigureAwait(false);
        await serial.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return await Task.Run(async () =>
            {
                ct.ThrowIfCancellationRequested(); ImageRasterSource.CheckAvailable(document.Sources);
                string key = string.Join("|", document.Sources.Select(s => $"{s.Id}:{s.Length}:{s.Sha256}:{s.Path}"));
                if (!cached.TryGetValue(key, out var staged))
                {
                    if (!ownsCache) { Directory.CreateDirectory(cacheDirectory); ownsCache = true; }
                    string scope = Path.Combine(cacheDirectory, Guid.NewGuid().ToString("N"));
                    staged = await sources.StageVerifiedAsync(document.Sources, scope, ct).ConfigureAwait(false);
                    cached.Add(key, staged);
                }
                var recipe = (ImageRecipe)document.Recipe;
                using var image = Render(document, staged[recipe.SourceId], ct, out SizeI sourceSize);
                PrepareOutput(image, document.Export);
                if (request.SourceTile is { } tile) CropDetail(image, recipe, sourceSize, tile);
                double scale = Math.Min(1, Math.Min(request.Bounds.Width / (double)image.Width, request.Bounds.Height / (double)image.Height));
                if (scale < 1) image.Resize(new MagickGeometry((uint)Math.Max(1, Math.Floor(image.Width * scale)), (uint)Math.Max(1, Math.Floor(image.Height * scale))) { IgnoreAspectRatio = true });
                ct.ThrowIfCancellationRequested();
                byte[] bytes = image.GetPixels().ToByteArray(PixelMapping.BGRA)!;
                return new PreviewFrame(document.Revision, request.Generation, new((int)image.Width, (int)image.Height), checked((int)image.Width * 4), ImmutableArray.CreateRange(bytes), null, null, []);
            }, ct).ConfigureAwait(false);
        }
        finally { serial.Release(); }
    }
    public async Task<StagedExport> RenderExportAsync(EditorDocument document, string privateDirectory, IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        Validate(document); ct.ThrowIfCancellationRequested();
        string root = Path.GetFullPath(privateDirectory); SafeEditorFiles.RejectLinks(root);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any() || File.Exists(root)) throw new IOException("Export requires a fresh private directory.");
        await serial.WaitAsync(ct).ConfigureAwait(false);
        bool createdRoot = false;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return await Task.Run(async () =>
            {
                ct.ThrowIfCancellationRequested(); ImageRasterSource.CheckAvailable(document.Sources);
                if (!Directory.Exists(root)) { Directory.CreateDirectory(root); createdRoot = true; }
                var staged = await sources.StageVerifiedAsync(document.Sources, root, ct).ConfigureAwait(false);
                var recipe = (ImageRecipe)document.Recipe;
                progress?.Report(new(null, "Rendering original pixels"));
                using var image = Render(document, staged[recipe.SourceId], ct, out _);
                PrepareOutput(image, document.Export);
                string payload = Path.Combine(root, "payload"); Directory.CreateDirectory(payload);
                string name = "image." + document.Export.FormatId, path = Path.Combine(payload, name);
                image.Quality = (uint)((QualityGoal)document.Export.Compression).Value;
                // Explicit format prevents generated paths or user filenames from selecting delegates.
                await image.WriteAsync(path, ImageRasterSource.Format(document.Export.FormatId), ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                long bytes = new FileInfo(path).Length;
                return new StagedExport(ExportShape.File, [new(name, bytes)], new(document.Sources.Single(s => s.Id == recipe.SourceId).Length, bytes, null, true, 1), []);
            }, ct).ConfigureAwait(false);
        }
        catch
        {
            foreach (string name in new[] { "sources", "payload" }) { string path = Path.Combine(root, name); if (Directory.Exists(path)) Directory.Delete(path, true); }
            if (createdRoot && Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
            throw;
        }
        finally { serial.Release(); }
    }
    internal static ImageRecipe Validate(EditorDocument document)
    {
        var errors = EditorRecipeValidator.Validate(document);
        if (!errors.IsEmpty) throw new ArgumentException(string.Join(" ", errors.Select(e => $"{e.FieldPath}: {e.Message}")));
        if (document.Recipe is not ImageRecipe recipe) throw new NotSupportedException("An image recipe is required.");
        if (recipe.Canvas is not null || !recipe.Annotations.IsEmpty || !recipe.Masks.IsEmpty || !recipe.MetadataEdits.IsEmpty || !document.Fonts.IsEmpty) throw new NotSupportedException("Canvas, annotations, masks and metadata edits are not available in this renderer yet.");
        if (document.Export.Compression is not QualityGoal) throw new NotSupportedException("Measured byte/reduction targets are not available yet. Choose a quality value.");
        ImageRasterSource.Format(document.Export.FormatId);
        if (document.Sources.Length != 1 || document.Sources[0].Id != recipe.SourceId) throw new ArgumentException("This renderer requires exactly one root source.");
        if (document.Export.Matte.A != 255 && document.Export.FormatId is "jpg" or "jpeg" or "bmp" or "gif") throw new ArgumentException("This output format requires an opaque matte.");
        return recipe;
    }
    private static MagickImage Render(EditorDocument document, string source, CancellationToken ct, out SizeI sourceSize)
    {
        var recipe = (ImageRecipe)document.Recipe; var image = ImageRasterSource.Load(source, ct);
        try
        {
            sourceSize = new((int)image.Width, (int)image.Height); var (crop, size) = ImageGeometryMapper.Resolve(recipe, sourceSize);
            if (recipe.Geometry.FlipX) image.Flop(); if (recipe.Geometry.FlipY) image.Flip();
            if (recipe.Geometry.QuarterTurns != 0) image.Rotate(90 * recipe.Geometry.QuarterTurns);
            image.Crop(new MagickGeometry((int)crop.X, (int)crop.Y, (uint)crop.Width, (uint)crop.Height)); image.ResetPage();
            ImageAdjustmentsPipeline.Apply(image, recipe, sourceSize, ct);
            if (image.Width != size.Width || image.Height != size.Height) { image.FilterType = FilterType.Lanczos; image.Resize(new MagickGeometry((uint)size.Width, (uint)size.Height) { IgnoreAspectRatio = true }); }
            ct.ThrowIfCancellationRequested(); return image;
        }
        catch { image.Dispose(); throw; }
    }
    private static void CropDetail(MagickImage image, ImageRecipe recipe, SizeI sourceSize, RectD tile)
    {
        if (!double.IsFinite(tile.X) || !double.IsFinite(tile.Y) || !double.IsFinite(tile.Width) || !double.IsFinite(tile.Height) || tile.Width <= 0 || tile.Height <= 0 || tile.X < 0 || tile.Y < 0 || tile.X + tile.Width > sourceSize.Width || tile.Y + tile.Height > sourceSize.Height) throw new ArgumentException("Detail tile must fit normalized source-plane S.");
        PointD[] corners = [new(tile.X, tile.Y), new(tile.X + tile.Width, tile.Y), new(tile.X, tile.Y + tile.Height), new(tile.X + tile.Width, tile.Y + tile.Height)];
        var mapped = corners.Select(p => ImageGeometryMapper.MapToCanvas(recipe, sourceSize, p)).ToArray();
        int left = (int)Math.Max(0, Math.Floor(mapped.Min(p => p.X))), top = (int)Math.Max(0, Math.Floor(mapped.Min(p => p.Y))), right = (int)Math.Min(image.Width, Math.Ceiling(mapped.Max(p => p.X))), bottom = (int)Math.Min(image.Height, Math.Ceiling(mapped.Max(p => p.Y)));
        if (right <= left || bottom <= top) throw new ArgumentException("Source tile does not intersect the visible output.");
        image.Crop(new MagickGeometry(left, top, (uint)(right - left), (uint)(bottom - top))); image.ResetPage();
    }
    private static void PrepareOutput(MagickImage image, ExportPolicy policy)
    {
        if (policy.FormatId is "jpg" or "jpeg" or "bmp" or "gif") { image.BackgroundColor = new MagickColor(policy.Matte.R, policy.Matte.G, policy.Matte.B); image.Alpha(AlphaAction.Remove); }
        if (policy.Metadata != MetadataPolicy.PreserveSupported) image.Strip();
        if (policy.Metadata != MetadataPolicy.StripAll) { image.SetProfile(ColorProfiles.SRGB); image.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", "true"); }
        image.Orientation = Orientation.TopLeft;
        var exif = image.GetExifProfile(); if (exif is not null) { exif.SetValue(ExifTag.Orientation, (ushort)1); exif.RemoveValue(ExifTag.PixelXDimension); exif.RemoveValue(ExifTag.PixelYDimension); image.SetProfile(exif); }
    }
    public void Dispose()
    {
        serial.Wait(); try { if (disposed) return; disposed = true; if (ownsCache && Directory.Exists(cacheDirectory)) Directory.Delete(cacheDirectory, true); cached.Clear(); } finally { serial.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        await serial.WaitAsync().ConfigureAwait(false); try { if (disposed) return; disposed = true; if (ownsCache && Directory.Exists(cacheDirectory)) Directory.Delete(cacheDirectory, true); cached.Clear(); } finally { serial.Release(); }
    }
}
