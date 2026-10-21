using System.Text.Json;
using Citrus.Core.Updates;

namespace Citrus.Windows.Updates;

/// <summary>Only verified installers with an exact owned receipt are eligible for cache cleanup.</summary>
internal static class UpdateStagingReceipts
{
    private sealed record Receipt(int Schema, UpdateCandidate Candidate, string File);

    internal static async Task RecordAsync(UpdateCandidate candidate, string installer, string directory, CancellationToken ct)
    {
        string expected = ExpectedPath(directory, candidate);
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(installer), expected) || UpdateInstallation.HasLinkedAncestor(installer))
            throw new InvalidDataException("Update receipt path is not the verified staging output.");
        string receipt = expected + ".update.json";
        if (File.Exists(receipt) && UpdateInstallation.HasLinkedAncestor(receipt)) throw new InvalidDataException("Linked update receipt.");
        string temporary = Path.Combine(directory, "receipt-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(new Receipt(1, candidate, Path.GetFileName(expected)));
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            { await stream.WriteAsync(data, ct).ConfigureAwait(false); await stream.FlushAsync(ct).ConfigureAwait(false); stream.Flush(flushToDisk: true); }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, receipt, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static async Task CleanupAsync(string directory, string currentVersion, ReadyUpdate? keep, CancellationToken ct)
    {
        if (!ReleaseVersion.TryParse(keep?.Candidate.Version ?? currentVersion, out var maximum) || !Directory.Exists(directory)) return;
        try
        {
            if (UpdateInstallation.HasLinkedAncestor(directory)) return;
            // A valid app cache has one pending entry. Bound malformed/externally expanded caches without recursive traversal.
            foreach (string receiptPath in Directory.EnumerateFiles(directory, "*.exe.update.json", SearchOption.TopDirectoryOnly).Take(64))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (UpdateInstallation.HasLinkedAncestor(receiptPath) || new FileInfo(receiptPath).Length > 64 * 1024) continue;
                    var receipt = JsonSerializer.Deserialize<Receipt>(await File.ReadAllTextAsync(receiptPath, ct).ConfigureAwait(false));
                    if (receipt?.Candidate is not { } candidate || receipt.Schema != 1 || !ReleaseVersion.TryParse(candidate.Version, out var version) ||
                        version!.CompareTo(maximum) > 0) continue;
                    string path = ExpectedPath(directory, candidate);
                    if (receipt.File != Path.GetFileName(path) || !StringComparer.OrdinalIgnoreCase.Equals(receiptPath, path + ".update.json") ||
                        StringComparer.OrdinalIgnoreCase.Equals(path, keep?.InstallerPath)) continue;
                    if (File.Exists(path))
                    {
                        if (!await VerifiedUpdateDownloader.VerifyAsync(candidate, path, ct).ConfigureAwait(false)) continue;
                        File.Delete(path); // A still-running installer remains locked and is retried on the next successful boot.
                    }
                    File.Delete(receiptPath);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
                { /* Unknown, modified or locked files remain untouched. */ }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static string ExpectedPath(string directory, UpdateCandidate candidate)
    {
        if (!ReleaseVersion.TryParse(candidate.Version, out _) || !UpdateInstallation.IsHash(candidate.Sha256))
            throw new InvalidDataException("Invalid update staging identity.");
        return Path.GetFullPath(Path.Combine(directory, $"Citrus-{candidate.Version}-{candidate.Sha256[..12].ToLowerInvariant()}.exe"));
    }
}
