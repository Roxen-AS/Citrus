using System.Net;
using System.Security.Cryptography;

namespace Citrus.Core.Updates;

/// <summary>Streams an installer without blocking input; only exact verified bytes become a ready file.</summary>
public sealed class VerifiedUpdateDownloader(HttpClient client)
{
    private readonly SemaphoreSlim pending = new(1, 1);

    public async Task<string> DownloadAsync(UpdateCandidate candidate, string directory, CancellationToken cancellationToken = default)
    {
        UpdateSecurity.Validate(candidate);
        await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            directory = Path.GetFullPath(directory);
            UpdateSecurity.CheckPath(directory);
            Directory.CreateDirectory(directory);
            string ready = Path.Combine(directory, $"Citrus-{candidate.Version}-{candidate.Sha256[..12].ToLowerInvariant()}.exe");
            if (await VerifyAsync(candidate, ready, cancellationToken).ConfigureAwait(false)) return ready;
            temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".part");
            using var request = new HttpRequestMessage(HttpMethod.Get, candidate.DownloadUrl);
            request.Headers.UserAgent.ParseAdd("Citrus-Update/1.0");
            request.Headers.Accept.ParseAdd("application/octet-stream");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException("The installer server did not return a complete file.");
            var final = response.RequestMessage?.RequestUri ?? candidate.DownloadUrl;
            if (final.Scheme != Uri.UriSchemeHttps || !final.IsDefaultPort ||
                final.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                throw new InvalidDataException("Installer redirected outside GitHub HTTPS storage.");
            string? media = response.Content.Headers.ContentType?.MediaType;
            if (media is not null && media is not ("application/octet-stream" or "application/x-msdownload" or "application/vnd.microsoft.portable-executable" or "binary/octet-stream"))
                throw new InvalidDataException("The installer response is not an executable download.");
            if (response.Content.Headers.ContentLength is long size && size != candidate.Bytes) throw new InvalidDataException("Installer byte count differs from its release.");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 256 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                byte[] buffer = new byte[256 * 1024];
                long total = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    total += count;
                    if (total > candidate.Bytes) throw new InvalidDataException("Installer exceeds its published size.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                }
                if (total != candidate.Bytes || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(candidate.Sha256)))
                    throw new InvalidDataException("Installer SHA-256 or byte count does not match its GitHub release.");
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            UpdateSecurity.CheckPath(ready);
            File.Move(temporary, ready, overwrite: true);
            temporary = null;
            return ready;
        }
        finally
        {
            try { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
            finally { pending.Release(); }
        }
    }

    public static async Task<bool> VerifyAsync(UpdateCandidate candidate, string path, CancellationToken cancellationToken = default)
    {
        UpdateSecurity.Validate(candidate);
        try
        {
            UpdateSecurity.CheckPath(path);
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 256 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (file.Length != candidate.Bytes) return false;
            byte[] digest = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);
            return CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(candidate.Sha256));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }
}
