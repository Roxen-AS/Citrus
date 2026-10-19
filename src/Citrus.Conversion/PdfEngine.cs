using Citrus.Core;
using ImageMagick;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PDFtoImage;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace Citrus.Conversion;

/// <summary>Offline PDF creation, structural operations, rendering and text extraction.</summary>
public sealed class PdfEngine(OutputWriter? outputWriter = null) : IConversionEngine
{
    private static readonly SemaphoreSlim RenderGate = new(1, 1);
    private readonly OutputWriter writer = outputWriter ?? new();
    public IReadOnlyList<OperationDescriptor> Operations { get; } = Array.AsReadOnly(new[]
    {
        new OperationDescriptor("images.pdf", "PDF", OperationMode.Convert, ".pdf", true, false),
        new OperationDescriptor("pdf.merge", "Merge PDFs", OperationMode.Tools, ".pdf", true, false),
        new OperationDescriptor("pdf.split", "Split pages", OperationMode.Tools, ".pdf", false, true),
        new OperationDescriptor("pdf.images", "Page images", OperationMode.Convert, ".png", false, true),
        new OperationDescriptor("pdf.text", "Text", OperationMode.Convert, ".txt", false, true)
    });

    public bool CanExecute(OperationRequest request)
    {
        if (request.Editor is not null) return false;
        var operation = Operations.FirstOrDefault(op => op.Id == request.Operation.Id);
        if (operation is null || (!operation.IsGroup && request.Inputs.Count != 1) ||
            !DropValidation.Accepts(request.Inputs, operation.Mode)) return false;
        return request.Inputs.All(path => operation.Id == "images.pdf"
            ? ValidatedImageLoader.Formats.ContainsKey(Path.GetExtension(path))
            : Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase));
    }

    public Task<ConversionResult> ExecuteAsync(OperationRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => ExecuteCoreAsync(request, progress, cancellationToken), cancellationToken);

    private async Task<ConversionResult> ExecuteCoreAsync(OperationRequest request, IProgress<ConversionProgress>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!CanExecute(request)) throw new NotSupportedException("Choose compatible PDF files or supported single-frame images.");
        string first = request.Inputs[0];
        string parent = Path.GetDirectoryName(first)!;
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (request.DestinationDirectory is null && request.Inputs.Any(path => !pathComparer.Equals(parent, Path.GetDirectoryName(path))))
            throw new ArgumentException("Choose an explicit destination when inputs come from different directories.", nameof(request));
        string destination = request.DestinationDirectory ?? parent;
        string stem = Path.GetFileNameWithoutExtension(first);
        var outputs = new List<string>();
        try
        {
            progress?.Report(new(null, "Reading inputs"));
            if (request.Operation.Id == "images.pdf")
            {
                await CreateFromImagesAsync(request.Inputs, Path.Combine(destination, stem + "-combined.pdf"), outputs, token).ConfigureAwait(false);
            }
            else
            {
                var inputs = new List<PdfInput>();
                try
                {
                    // Keep input streams/documents alive through imports/save and lock originals against Windows writes.
                    foreach (string path in request.Inputs)
                    {
                        token.ThrowIfCancellationRequested();
                        inputs.Add(PdfInput.Open(path));
                    }
                    var pages = PdfPageSelection.Parse(request.Options.PageSelection, inputs[0].Document.PageCount);
                    switch (request.Operation.Id)
                    {
                        case "pdf.merge":
                            using (var merged = new PdfDocument())
                            {
                                foreach (var input in inputs)
                                    foreach (PdfPage page in input.Document.Pages)
                                    {
                                        token.ThrowIfCancellationRequested();
                                        merged.AddPage(page);
                                    }
                                outputs.Add(await SaveAsync(merged, Path.Combine(destination, stem + "-merged.pdf"), token).ConfigureAwait(false));
                            }
                            break;
                        case "pdf.split":
                            foreach (int page in pages)
                            {
                                token.ThrowIfCancellationRequested();
                                using var split = new PdfDocument();
                                split.AddPage(inputs[0].Document.Pages[page - 1]);
                                outputs.Add(await SaveAsync(split, Path.Combine(destination, $"{stem}-page-{page}.pdf"), token).ConfigureAwait(false));
                                token.ThrowIfCancellationRequested();
                                progress?.Report(new((double)outputs.Count / pages.Count, "Page ready"));
                            }
                            break;
                        case "pdf.images":
                            await RenderAsync(inputs[0], pages, destination, stem, outputs, progress, token).ConfigureAwait(false);
                            break;
                        case "pdf.text":
                            inputs[0].Stream.Position = 0;
                            using (var textDocument = PigDocument.Open(inputs[0].Stream))
                            {
                                outputs.Add(await writer.WriteFileAsync(Path.Combine(destination, stem + "-text.txt"), async (temporary, cancellation) =>
                                {
                                    await using var text = new StreamWriter(temporary, append: false, new System.Text.UTF8Encoding(false));
                                    foreach (int page in pages)
                                    {
                                        cancellation.ThrowIfCancellationRequested();
                                        await text.WriteLineAsync(ContentOrderTextExtractor.GetText(textDocument.GetPage(page)).AsMemory(), cancellation).ConfigureAwait(false);
                                        await text.WriteLineAsync().ConfigureAwait(false);
                                    }
                                }, token).ConfigureAwait(false));
                            }
                            break;
                    }
                }
                finally { foreach (var input in inputs) input.Dispose(); }
            }
            token.ThrowIfCancellationRequested();
            progress?.Report(new(1, "Copies ready"));
            return new(outputs.ToArray());
        }
        catch (Exception failure)
        {
            List<Exception>? cleanupFailures = null;
            foreach (string output in outputs)
            {
                try { File.Delete(output); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { (cleanupFailures ??= []).Add(error); }
            }
            if (cleanupFailures is not null)
                throw new AggregateException("PDF conversion failed and some owned outputs could not be removed.", new[] { failure }.Concat(cleanupFailures));
            throw;
        }
    }

    private async Task CreateFromImagesAsync(IReadOnlyList<string> paths, string desired, List<string> outputs, CancellationToken token)
    {
        var images = new List<MagickImage>();
        var resources = new List<IDisposable>();
        try
        {
            // Validate every input before starting a final output, using the same explicit decoders as ImageEngine.
            foreach (string path in paths) images.Add(ValidatedImageLoader.Load(path, token));
            using var document = new PdfDocument();
            foreach (var image in images)
            {
                token.ThrowIfCancellationRequested();
                // Fixed 96 pixels/inch avoids untrusted/absent density metadata and preserves aspect ratio.
                var page = document.AddPage();
                page.Width = XUnit.FromPoint(image.Width * 72d / 96);
                page.Height = XUnit.FromPoint(image.Height * 72d / 96);
                var stream = new MemoryStream(image.ToByteArray(MagickFormat.Png));
                resources.Add(stream);
                var pdfImage = XImage.FromStream(stream);
                resources.Add(pdfImage);
                using var graphics = XGraphics.FromPdfPage(page);
                graphics.DrawImage(pdfImage, 0, 0, page.Width.Point, page.Height.Point);
            }
            outputs.Add(await SaveAsync(document, desired, token).ConfigureAwait(false));
        }
        finally
        {
            for (int i = resources.Count - 1; i >= 0; i--) resources[i].Dispose();
            foreach (var image in images) image.Dispose();
        }
    }

    private Task<string> SaveAsync(PdfDocument document, string desired, CancellationToken token) =>
        writer.WriteFileAsync(desired, (temporary, cancellation) =>
        {
            cancellation.ThrowIfCancellationRequested();
            document.Save(temporary);
            cancellation.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }, token);

    private async Task RenderAsync(PdfInput input, IReadOnlyList<int> pages, string destination, string stem,
        List<string> outputs, IProgress<ConversionProgress>? progress, CancellationToken token)
    {
        foreach (int page in pages)
        {
            token.ThrowIfCancellationRequested();
            outputs.Add(await writer.WriteFileAsync(Path.Combine(destination, $"{stem}-page-{page}.png"), async (temporary, cancellation) =>
            {
                await RenderGate.WaitAsync(cancellation).ConfigureAwait(false);
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    input.Stream.Position = 0;
                    // The string overload expects Base64. Render the stream while holding the global engine gate.
                    if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
                    {
                        PDFtoImage.Conversion.SavePng(temporary, input.Stream, page - 1, leaveOpen: true,
                            options: new RenderOptions(Dpi: 300, WithAnnotations: true, WithFormFill: true));
                    }
                    else throw new PlatformNotSupportedException("PDF rendering is supported on Windows and Linux.");
                    cancellation.ThrowIfCancellationRequested();
                }
                finally { RenderGate.Release(); }
            }, token).ConfigureAwait(false));
            token.ThrowIfCancellationRequested();
            progress?.Report(new((double)outputs.Count / pages.Count, "Page image ready"));
        }
    }

    private sealed class PdfInput(FileStream stream, PdfDocument document) : IDisposable
    {
        public FileStream Stream { get; } = stream;
        public PdfDocument Document { get; } = document;
        public static PdfInput Open(string path)
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            PdfDocument? document = null;
            try
            {
                using (var check = PigDocument.Open(stream))
                {
                    if (check.IsEncrypted) throw new NotSupportedException("Encrypted PDFs are not supported. Export an unencrypted copy first.");
                    if (check.NumberOfPages < 1) throw new InvalidDataException("The PDF has no pages.");
                }
                stream.Position = 0;
                document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
                if (document.SecuritySettings.IsEncrypted) throw new NotSupportedException("Encrypted PDFs are not supported. Export an unencrypted copy first.");
                return new(stream, document);
            }
            catch { document?.Dispose(); stream.Dispose(); throw; }
        }
        public void Dispose() { Document.Dispose(); Stream.Dispose(); }
    }
}
