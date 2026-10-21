using Citrus.Core;
using PdfSharp.Pdf.IO;

namespace Citrus.Conversion;

/// <summary>Reads only metadata required by options, off the UI thread. Failures stay associated with their input.</summary>
public sealed class InputInspector(MediaProcess process)
{
    public Task<IReadOnlyList<InputInspection>> InspectAsync(SubmissionDraft draft, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<InputInspection>>(async () =>
        {
            bool trim = draft.Operation.Id is "audio.trim" or "video.trim";
            bool pages = draft.Operation.Id is "pdf.split" or "pdf.images" or "pdf.text";
            if (!trim && !pages) return [];
            var results = new List<InputInspection>();
            foreach (string path in draft.Inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!DropValidation.Accepts([path], draft.Operation.Mode)) throw new IOException("Choose an available local file; offline cloud placeholders cannot be read.");
                    if (trim)
                    {
                        var info = await process.ProbeAsync(path, cancellationToken).ConfigureAwait(false);
                        if (!double.IsFinite(info.DurationSeconds) || info.DurationSeconds <= 0) throw new IOException("Media duration could not be read.");
                        results.Add(new(path, info.DurationSeconds, null, null));
                    }
                    else
                    {
                        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
                        if (document.SecuritySettings.IsEncrypted) throw new IOException("Encrypted PDFs are not supported. Choose an unencrypted local copy.");
                        if (document.PageCount < 1) throw new IOException("This PDF has no readable pages.");
                        results.Add(new(path, null, document.PageCount, null));
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    results.Add(new(path, null, null, "Could not inspect this input: " + error.Message));
                }
            }
            return results.AsReadOnly();
        }, cancellationToken);
}
