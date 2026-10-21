using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Citrus.Core.Updates;

public sealed record UpdateCandidate(string Version, long ReleaseId, long AssetId, Uri DownloadUrl, long Bytes, string Sha256);

/// <summary>Anonymous public-release checks. Source commits, drafts and media edits are never executable updates.</summary>
public sealed class GitHubUpdateClient(HttpClient client)
{
    private static readonly Uri Feed = new("https://api.github.com/repos/Skytuhua/Citrus/releases?per_page=30");
    private readonly SemaphoreSlim pending = new(1, 1);
    private EntityTagHeaderValue? etag;
    private byte[]? cached;

    public async Task<UpdateCandidate?> CheckAsync(string currentVersion, bool includePrereleases, CancellationToken cancellationToken = default)
    {
        if (!ReleaseVersion.TryParse(currentVersion, out var current)) throw new ArgumentException("Invalid current application version.", nameof(currentVersion));
        await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Feed);
            request.Headers.UserAgent.ParseAdd("Citrus-Update/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            if (etag is not null && cached is not null) request.Headers.IfNoneMatch.Add(etag);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var final = response.RequestMessage?.RequestUri ?? Feed;
            if (final.Scheme != Uri.UriSchemeHttps || final.Host != Feed.Host || !final.IsDefaultPort)
                throw new InvalidDataException("Update metadata left the GitHub API.");
            byte[] bytes;
            if (response.StatusCode == HttpStatusCode.NotModified)
                bytes = cached ?? throw new InvalidDataException("No cached release metadata.");
            else
            {
                response.EnsureSuccessStatusCode();
                bytes = await ReadBoundedAsync(response.Content, 4 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
            }
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid GitHub release response.");
            UpdateCandidate? newest = null;
            ReleaseVersion? newestVersion = null;
            foreach (var release in document.RootElement.EnumerateArray())
            {
                var candidate = ReadCandidate(release, includePrereleases);
                if (candidate is null || !ReleaseVersion.TryParse(candidate.Version, out var version) || version!.CompareTo(current) <= 0) continue;
                if (newestVersion is null || version.CompareTo(newestVersion) > 0) { newest = candidate; newestVersion = version; }
            }
            // Never cache a malformed response, including a valid JSON object in place of the release list.
            if (response.StatusCode != HttpStatusCode.NotModified) { cached = bytes; etag = response.Headers.ETag; }
            return newest;
        }
        finally { pending.Release(); }
    }

    private static UpdateCandidate? ReadCandidate(JsonElement release, bool includePrereleases)
    {
        try
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("published_at").ValueKind != JsonValueKind.String) return null;
            string tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!tag.StartsWith('v') || !ReleaseVersion.TryParse(tag[1..], out var version) || (!includePrereleases && (version!.IsPrerelease || release.GetProperty("prerelease").GetBoolean()))) return null;
            var matches = release.GetProperty("assets").EnumerateArray().Where(asset => asset.TryGetProperty("name", out var name) && name.GetString() == "Citrus-Setup.exe").ToArray();
            if (matches.Length != 1 || matches[0].GetProperty("state").GetString() != "uploaded") return null;
            var asset = matches[0];
            string digest = asset.GetProperty("digest").GetString() ?? "";
            if (!digest.StartsWith("sha256:", StringComparison.Ordinal)) return null;
            var candidate = new UpdateCandidate(tag[1..], release.GetProperty("id").GetInt64(), asset.GetProperty("id").GetInt64(),
                new Uri(asset.GetProperty("browser_download_url").GetString() ?? "", UriKind.Absolute), asset.GetProperty("size").GetInt64(), digest[7..].ToLowerInvariant());
            UpdateSecurity.Validate(candidate);
            return candidate;
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException or OverflowException) { return null; }
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > limit) throw new InvalidDataException("Release metadata is too large.");
        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var result = new MemoryStream();
        byte[] buffer = new byte[16 * 1024];
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (result.Length + count > limit) throw new InvalidDataException("Release metadata is too large.");
            result.Write(buffer, 0, count);
        }
        return result.ToArray();
    }
}

internal static class UpdateSecurity
{
    internal static void Validate(UpdateCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!ReleaseVersion.TryParse(candidate.Version, out _) || candidate.Version.Contains('+') || candidate.ReleaseId <= 0 || candidate.AssetId <= 0 ||
            candidate.Bytes is <= 0 or > int.MaxValue || candidate.Sha256 is null || candidate.Sha256.Length != 64 || !candidate.Sha256.All(Uri.IsHexDigit) ||
            candidate.DownloadUrl is null || candidate.DownloadUrl.AbsoluteUri != $"https://github.com/Skytuhua/Citrus/releases/download/v{candidate.Version}/Citrus-Setup.exe")
            throw new ArgumentException("Invalid Citrus release candidate.", nameof(candidate));
    }
    internal static void CheckPath(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("An update path contains a link.");
    }
}
