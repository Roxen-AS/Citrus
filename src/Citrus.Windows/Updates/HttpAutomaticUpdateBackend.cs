using System.Net.Http;
using System.Diagnostics;
using Citrus.Core.Updates;

namespace Citrus.Windows.Updates;

internal sealed class HttpAutomaticUpdateBackend(HttpClient client, string currentVersion, string stagingDirectory) : IAutomaticUpdateBackend
{
    private readonly GitHubUpdateClient feed = new(client);
    private readonly VerifiedUpdateDownloader downloader = new(client);
    public async Task<UpdateCandidate?> CheckAsync(CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        return await feed.CheckAsync(currentVersion, currentVersion.Contains('-'), deadline.Token).ConfigureAwait(false);
    }
    public async Task<string> DownloadAsync(UpdateCandidate candidate, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromHours(2));
        string installer = await downloader.DownloadAsync(candidate, stagingDirectory, deadline.Token).ConfigureAwait(false);
        if (!InstallerVersionMatches(installer, candidate.Version))
        {
            File.Delete(installer);
            throw new InvalidDataException("The verified installer version differs from its GitHub release.");
        }
        await UpdateStagingReceipts.RecordAsync(candidate, installer, stagingDirectory, deadline.Token).ConfigureAwait(false);
        await UpdateStagingReceipts.CleanupAsync(stagingDirectory, currentVersion, new(candidate, installer), deadline.Token).ConfigureAwait(false);
        return installer;
    }
    public async Task<bool> VerifyAsync(ReadyUpdate update, CancellationToken ct) =>
        await VerifiedUpdateDownloader.VerifyAsync(update.Candidate, update.InstallerPath, ct).ConfigureAwait(false) &&
        InstallerVersionMatches(update.InstallerPath, update.Candidate.Version);

    internal static bool InstallerVersionMatches(string file, string version)
    {
        try { return StringComparer.Ordinal.Equals(FileVersionInfo.GetVersionInfo(file).FileVersion, version); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
}
