using Citrus.Core;

namespace Citrus.Conversion.Tests;

public sealed class SubtitleEngineTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Citrus subtitles " + Guid.NewGuid());
    private readonly SubtitleEngine engine = new();
    public SubtitleEngineTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private async Task<string> Convert(string content, string extension, string operation)
    {
        string input = Path.Combine(directory, "captions" + extension);
        await File.WriteAllTextAsync(input, content);
        byte[] original = await File.ReadAllBytesAsync(input);
        var request = new OperationRequest(engine.Operations.Single(o => o.Id == operation), [input], new(), null);
        string output = Assert.Single((await engine.ExecuteAsync(request, null, default)).Outputs);
        Assert.Equal(original, await File.ReadAllBytesAsync(input));
        return await File.ReadAllTextAsync(output);
    }

    [Fact]
    public async Task Srt_bom_crlf_multiline_large_hours_translate_only_timestamps()
    {
        string result = await Convert("\uFEFF1\r\n27:03:04,005 --> 27:03:05.120\r\nHello, 1,234\r\nSecond line.\r\n", ".srt", "subtitle.vtt");
        Assert.StartsWith("WEBVTT\n\n", result);
        Assert.Contains("27:03:04.005 --> 27:03:05.120", result);
        Assert.Contains("Hello, 1,234\nSecond line.", result);
    }

    [Fact]
    public async Task Vtt_settings_identifiers_and_short_timestamps_survive_vtt_output()
    {
        string result = await Convert("WEBVTT\n\ncue-A\n00:01.005 --> 00:03.120 align:start position:10%\nOne\nTwo\n", ".vtt", "subtitle.vtt");
        Assert.Contains("cue-A\n00:00:01.005 --> 00:00:03.120 align:start position:10%", result);
        Assert.Contains("One\nTwo", result);
    }

    [Fact]
    public async Task Vtt_to_srt_omits_incompatible_settings_and_metadata()
    {
        string result = await Convert("WEBVTT\n\nNOTE ignore me\ncomment\n\ncue-A\n00:01.005 --> 00:03.120 align:start\nOne\nTwo\n", ".vtt", "subtitle.srt");
        Assert.Equal("1\n00:00:01,005 --> 00:00:03,120\nOne\nTwo\n\n", result);
    }

    [Theory]
    [InlineData("00:61:00,000 --> 00:62:00,000")]
    [InlineData("00:00:00,00 --> 00:00:01,000")]
    [InlineData("bad --> 00:00:01,000")]
    [InlineData("00:00:02,000 --> 00:00:01,000")]
    [InlineData("00:00:02,000 -> 00:00:03,000")]
    public async Task Malformed_cue_reports_location_and_leaves_only_original(string timing)
    {
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Convert("1\n" + timing + "\nText\n", ".srt", "subtitle.vtt"));
        Assert.Contains("line", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task Txt_contains_only_multiline_cue_text()
    {
        string result = await Convert("1\n00:00:01,000 --> 00:00:02,000\nFirst\nSecond\n\n2\n00:00:03,000 --> 00:00:04,000\nThird\n", ".srt", "subtitle.txt");
        Assert.Equal("First\nSecond\n\nThird\n", result);
    }

    [Fact]
    public async Task Vtt_region_definition_and_cue_reference_survive_round_trip()
    {
        const string region = "REGION\nid:bottom\nwidth:40%\nlines:3\nregionanchor:0%,100%\nviewportanchor:10%,90%\n";
        const string content = "WEBVTT\n\n" + region + "\ncue-A\n00:00:01.000 --> 00:00:02.000 region:bottom\nFirst\n\n";
        string result = await Convert(content, ".vtt", "subtitle.vtt");
        Assert.Equal(content, result);
        Assert.Equal(result, await Convert(result, ".vtt", "subtitle.vtt"));
    }

    [Theory]
    [InlineData("subtitle.srt", "1\n00:00:01,000 --> 00:00:02,000\nFirst\n\n")]
    [InlineData("subtitle.txt", "First\n")]
    public async Task Vtt_regions_are_omitted_from_srt_and_txt(string operation, string expected)
    {
        string result = await Convert("WEBVTT\n\nREGION\nid:bottom\nwidth:40%\nlines:3\n\n00:00:01.000 --> 00:00:02.000 region:bottom\nFirst\n", ".vtt", operation);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("First\n \nSecond")]
    [InlineData("First\n  ")]
    [InlineData("First\n\u00a0")]
    [InlineData("First\n \nSecond\n\u00a0")]
    public async Task Vtt_preserves_each_nonempty_payload_line_exactly(string payload)
    {
        string content = "WEBVTT\n\n00:00:01.000 --> 00:00:02.000\n" + payload + "\n\n";
        Assert.Equal(content, await Convert(content, ".vtt", "subtitle.vtt"));
        Assert.Equal(payload + "\n", await Convert(content, ".vtt", "subtitle.txt"));
    }

    [Fact]
    public async Task Srt_whitespace_separator_policy_is_unchanged()
    {
        string content = "1\n00:00:01,000 --> 00:00:02,000\nFirst\n \n2\n00:00:03,000 --> 00:00:04,000\nSecond\n";
        Assert.Equal("First\n\nSecond\n", await Convert(content, ".srt", "subtitle.txt"));
    }

}
