using Citrus.Core;
using ImageMagick;

namespace Citrus.Conversion;

/// <summary>Local, explicitly selected raster decoders. Document delegates are never selected; SVG uses the internal MSVG renderer.</summary>
public sealed class ImageEngine : IConversionEngine
{
    private static IReadOnlyDictionary<string, MagickFormat> Formats => ValidatedImageLoader.Formats;
    private readonly OutputWriter writer = new();
    public IReadOnlyList<OperationDescriptor> Operations { get; } = Array.AsReadOnly(new[] { "png", "jpeg", "webp", "tiff", "bmp", "gif", "avif" }
        .Select(ext => new OperationDescriptor("image.convert." + ext, ext.ToUpperInvariant(), OperationMode.Convert, "." + ext, false, false))
        .Concat(new[]
        {
            new OperationDescriptor("image.resize", "Resize", OperationMode.Tools, null, false, true),
            new OperationDescriptor("image.rotate", "Rotate", OperationMode.Tools, null, false, true),
            new OperationDescriptor("image.compress", "Compress", OperationMode.Tools, null, false, true),
            new OperationDescriptor("image.strip", "Strip metadata", OperationMode.Tools, null, false, false)
        }).ToArray());

    public bool CanExecute(OperationRequest request) => request.Inputs.Count == 1 &&
        Operations.Any(operation => operation.Id == request.Operation.Id) &&
        DropValidation.Accepts(request.Inputs, request.Operation.Mode) &&
        Formats.ContainsKey(Path.GetExtension(request.Inputs[0])) &&
        (!Path.GetExtension(request.Inputs[0]).Equals(".svg", StringComparison.OrdinalIgnoreCase) || request.Operation.Mode == OperationMode.Convert);

    public Task<ConversionResult> ExecuteAsync(OperationRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanExecute(request)) throw new NotSupportedException("Choose an available single-frame raster image, or convert an SVG to a raster format.");
            var options = request.Options;
            if (options.Quality is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(request), "Quality must be between 1 and 100.");
            if (request.Operation.Id == "image.resize" &&
                (options.Width is <= 0 || options.Height is <= 0 || (options.Width is null && options.Height is null)))
                throw new ArgumentException("Resize needs at least one positive dimension.", nameof(request));
            string source = request.Inputs[0];
            string sourceExtension = Path.GetExtension(source);
            var operation = Operations.Single(op => op.Id == request.Operation.Id);
            string extension = operation.OutputExtension ?? sourceExtension;
            string suffix = operation.Mode == OperationMode.Tools ? "-" + operation.Id[6..] : "";
            string desired = Path.Combine(request.DestinationDirectory ?? Path.GetDirectoryName(source)!, Path.GetFileNameWithoutExtension(source) + suffix + extension);
            progress?.Report(new(null, "Reading image"));
            using var image = ValidatedImageLoader.Load(source, cancellationToken);
            switch (operation.Id)
            {
                case "image.resize":
                    image.Resize(new MagickGeometry((uint)(options.Width ?? 0), (uint)(options.Height ?? 0)) { IgnoreAspectRatio = !options.PreserveAspectRatio });
                    break;
                case "image.rotate": image.Rotate(options.RotationDegrees); break;
                case "image.strip": image.Strip(); break;
            }
            image.Quality = (uint)options.Quality;
            var format = Formats[extension];
            var output = await writer.WriteFileAsync(desired, async (temporary, token) =>
            {
                token.ThrowIfCancellationRequested();
                progress?.Report(new(null, "Writing copy"));
                await image.WriteAsync(temporary, format, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                using var check = new MagickImage(temporary, new MagickReadSettings { Format = format, FrameCount = 1 });
                if (check.Width != image.Width || check.Height != image.Height) throw new IOException("Encoded image dimensions did not match.");
            }, cancellationToken).ConfigureAwait(false);
            progress?.Report(new(1, "Copy ready"));
            return new ConversionResult([output]);
        }, cancellationToken);

}
