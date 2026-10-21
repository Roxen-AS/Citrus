using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Citrus.Core.Updates;

namespace Citrus.Core.Tests;

public sealed class AutoUpdateTests
{
    private static readonly byte[] Installer = Encoding.UTF8.GetBytes("MZ owned installer test fixture");
    private static readonly string Digest = Convert.ToHexStringLower(SHA256.HashData(Installer));
    private static readonly Uri Download = new("https://github.com/Skytuhua/Citrus/releases/download/v0.3.0-preview.2/Citrus-Setup.exe");
    private static UpdateCandidate Candidate => new("0.3.0-preview.2", 1, 2, Download, Installer.Length, Digest);

    [Theory]
    [InlineData("0.3.0-preview.2", "0.3.0-preview.10")]
    [InlineData("0.3.0-preview.10", "0.3.0")]
    [InlineData("0.3.0", "0.3.1-alpha")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-9999999999999999999", "1.0.0-10000000000000000000")]
    public void Release_ordering_does_not_confuse_preview_ten_with_preview_two(string older, string newer)
    {
        Assert.True(ReleaseVersion.TryParse(older, out var a));
        Assert.True(ReleaseVersion.TryParse(newer, out var b));
        Assert.True(a!.CompareTo(b) < 0);
        Assert.True(b!.CompareTo(a) > 0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("01.3.0")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.3-preview.01")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3/evil")]
    [InlineData("1.2.3\n")]
    public void Invalid_release_versions_are_rejected(string value) => Assert.False(ReleaseVersion.TryParse(value, out _));

    [Fact]
    public void Build_metadata_does_not_change_precedence()
    {
        Assert.True(ReleaseVersion.TryParse("1.2.3+commit.abc", out var a));
        Assert.True(ReleaseVersion.TryParse("1.2.3+commit.def", out var b));
        Assert.Equal(0, a!.CompareTo(b));
    }

    [Fact]
    public async Task Published_preview_is_discovered_with_semantic_ordering_and_no_authentication()
    {
        var handler = new ReplyHandler(request =>
        {
            Assert.Equal("https://api.github.com/repos/Skytuhua/Citrus/releases?per_page=30", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            Assert.NotEmpty(request.Headers.UserAgent);
            return Json(new[] { Release("0.3.0-preview.2"), Release("0.3.0-preview.10"), Release("0.3.0-preview.1") });
        });
        var result = await new GitHubUpdateClient(new(handler)).CheckAsync("0.3.0-preview.1", true);
        Assert.Equal("0.3.0-preview.10", result!.Version);
        Assert.Equal(Digest, result.Sha256);
    }

    [Fact]
    public async Task Stable_install_does_not_receive_previews_or_downgrades()
    {
        using var client = new HttpClient(new ReplyHandler(_ => Json(new[] { Release("0.3.0-preview.10"), Release("0.2.0") })));
        Assert.Null(await new GitHubUpdateClient(client).CheckAsync("0.3.0", false));
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("digest")]
    [InlineData("host")]
    [InlineData("size")]
    [InlineData("state")]
    [InlineData("duplicate")]
    [InlineData("unpublished")]
    public async Task Untrusted_or_incomplete_release_cannot_supply_an_executable(string change)
    {
        var release = Release("0.3.0-preview.2");
        if (change == "draft") release["draft"] = true;
        if (change == "unpublished") release["published_at"] = null!;
        var assets = (Dictionary<string, object>[])release["assets"];
        if (change == "digest") assets[0]["digest"] = "sha256:not-a-digest";
        if (change == "host") assets[0]["browser_download_url"] = "https://evil.example/Citrus-Setup.exe";
        if (change == "size") assets[0]["size"] = 0;
        if (change == "state") assets[0]["state"] = "starter";
        if (change == "duplicate") release["assets"] = new[] { assets[0], assets[0] };
        using var client = new HttpClient(new ReplyHandler(_ => Json(new[] { release })));
        Assert.Null(await new GitHubUpdateClient(client).CheckAsync("0.3.0-preview.1", true));
    }

    [Fact]
    public async Task Conditional_feed_reuses_cached_result_without_another_body()
    {
        int count = 0;
        using var client = new HttpClient(new ReplyHandler(request =>
        {
            if (++count == 1)
            {
                var response = Json(new[] { Release("0.3.0-preview.2") });
                response.Headers.ETag = new("\"owned-fixture\"");
                return response;
            }
            Assert.Equal("\"owned-fixture\"", request.Headers.IfNoneMatch.Single().Tag);
            return new(HttpStatusCode.NotModified);
        }));
        var updates = new GitHubUpdateClient(client);
        Assert.Equal("0.3.0-preview.2", (await updates.CheckAsync("0.3.0-preview.1", true))!.Version);
        Assert.Equal("0.3.0-preview.2", (await updates.CheckAsync("0.3.0-preview.1", true))!.Version);
        Assert.Null(await updates.CheckAsync("0.3.0-preview.2", true));
    }

    [Fact]
    public async Task Complete_download_is_hashed_before_publication_and_reused_only_after_reverification()
    {
        using var directory = new OwnedDirectory();
        int requests = 0;
        using var client = new HttpClient(new ReplyHandler(_ => { requests++; return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Installer) }; }));
        var downloader = new VerifiedUpdateDownloader(client);
        string path = await downloader.DownloadAsync(Candidate, directory.Path);
        Assert.Equal(Installer, await File.ReadAllBytesAsync(path));
        Assert.True(await VerifiedUpdateDownloader.VerifyAsync(Candidate, path));
        Assert.Equal(path, await downloader.DownloadAsync(Candidate, directory.Path));
        Assert.Equal(1, requests);
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("tampered"));
        Assert.False(await VerifiedUpdateDownloader.VerifyAsync(Candidate, path));
        Assert.Equal(path, await downloader.DownloadAsync(Candidate, directory.Path));
        Assert.Equal(2, requests);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.part"));
    }

    [Theory]
    [InlineData("wrong-hash")]
    [InlineData("truncated")]
    [InlineData("overlong")]
    [InlineData("html")]
    [InlineData("redirect")]
    [InlineData("partial-response")]
    public async Task Bad_downloads_never_become_ready_executables(string failure)
    {
        using var directory = new OwnedDirectory();
        byte[] bytes = failure switch { "truncated" => Installer[..^1], "overlong" => [.. Installer, 0], "wrong-hash" => [.. Installer.Select(b => (byte)(b ^ 1))], _ => Installer };
        using var client = new HttpClient(new ReplyHandler(_ =>
        {
            var response = new HttpResponseMessage(failure == "partial-response" ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            if (failure == "html") response.Content.Headers.ContentType = new("text/html");
            if (failure == "redirect") response.RequestMessage = new(HttpMethod.Get, "http://evil.example/payload");
            return response;
        }));
        await Assert.ThrowsAnyAsync<Exception>(() => new VerifiedUpdateDownloader(client).DownloadAsync(Candidate, directory.Path));
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task Canceled_download_leaves_no_partial_or_ready_file()
    {
        using var directory = new OwnedDirectory();
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new ReplyHandler(_ => { cancellation.Cancel(); return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Installer) }; }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new VerifiedUpdateDownloader(client).DownloadAsync(Candidate, directory.Path, cancellation.Token));
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task Externally_constructed_candidate_is_validated_before_network_or_file_access()
    {
        using var directory = new OwnedDirectory();
        using var client = new HttpClient(new ReplyHandler(_ => throw new Xunit.Sdk.XunitException("Network must not be reached")));
        await Assert.ThrowsAsync<ArgumentException>(() => new VerifiedUpdateDownloader(client).DownloadAsync(Candidate with { DownloadUrl = new("https://evil.example/installer.exe") }, directory.Path));
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    private static Dictionary<string, object> Release(string version) => new()
    {
        ["id"] = 1L, ["tag_name"] = "v" + version, ["draft"] = false, ["prerelease"] = version.Contains('-'), ["published_at"] = "2026-10-21T12:00:00Z",
        ["assets"] = new[] { new Dictionary<string, object> { ["id"] = 2L, ["name"] = "Citrus-Setup.exe", ["state"] = "uploaded", ["size"] = Installer.Length, ["digest"] = "sha256:" + Digest, ["browser_download_url"] = "https://github.com/Skytuhua/Citrus/releases/download/v" + version + "/Citrus-Setup.exe" } }
    };
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private sealed class ReplyHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(reply(request));
    }
    private sealed class OwnedDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "citrus-update-test-" + Guid.NewGuid().ToString("N"));
        public OwnedDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
