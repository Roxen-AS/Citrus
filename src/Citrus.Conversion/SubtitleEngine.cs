using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Citrus.Core;

namespace Citrus.Conversion;

/// <summary>Converts UTF-8 SRT/WebVTT cues without changing the cue text.</summary>
public sealed class SubtitleEngine : IConversionEngine
{
    private static readonly Regex Timing = new(@"^(?<start>\S+)\s+-->\s+(?<end>\S+)(?<settings>[^\r\n]*)$", RegexOptions.CultureInvariant);
    private static readonly Regex Timestamp = new(@"^(?:(?<h>[0-9]{2,}):)?(?<m>[0-9]{2}):(?<s>[0-9]{2})[.,](?<ms>[0-9]{3})$", RegexOptions.CultureInvariant);
    private readonly OutputWriter writer = new();
    public IReadOnlyList<OperationDescriptor> Operations { get; } = Array.AsReadOnly(new[]
    {
        new OperationDescriptor("subtitle.vtt", "VTT", OperationMode.Convert, ".vtt", false, false),
        new OperationDescriptor("subtitle.srt", "SRT", OperationMode.Convert, ".srt", false, false),
        new OperationDescriptor("subtitle.txt", "Text", OperationMode.Convert, ".txt", false, false)
    });

    public bool CanExecute(OperationRequest request) =>
        Operations.Any(o => o.Id == request.Operation.Id) && request.Inputs.Count == 1 &&
        DropValidation.Accepts(request.Inputs, OperationMode.Convert) &&
        new[] { ".srt", ".vtt" }.Contains(Path.GetExtension(request.Inputs[0]), StringComparer.OrdinalIgnoreCase) &&
        !File.GetAttributes(request.Inputs[0]).HasFlag(FileAttributes.ReparsePoint);

    public Task<ConversionResult> ExecuteAsync(OperationRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanExecute(request)) throw new NotSupportedException("Choose a regular SRT or VTT file.");
            string input = request.Inputs[0];
            string content = await File.ReadAllTextAsync(input, new UTF8Encoding(false, true), cancellationToken).ConfigureAwait(false);
            bool vtt = Path.GetExtension(input).Equals(".vtt", StringComparison.OrdinalIgnoreCase);
            var cues = Parse(content, vtt, cancellationToken);
            var operation = Operations.Single(o => o.Id == request.Operation.Id);
            string desired = Path.Combine(request.DestinationDirectory ?? Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input) + operation.OutputExtension);
            string output = await writer.WriteFileAsync(desired, async (temporary, token) =>
            {
                await using var text = new StreamWriter(temporary, false, new UTF8Encoding(false)) { NewLine = "\n" };
                if (operation.Id == "subtitle.vtt") await text.WriteAsync("WEBVTT\n\n".AsMemory(), token).ConfigureAwait(false);
                for (int index = 0; index < cues.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var cue = cues[index];
                    string serialized = operation.Id switch
                    {
                        "subtitle.txt" => cue.Text + (index == cues.Count - 1 ? "\n" : "\n\n"),
                        "subtitle.srt" => $"{index + 1}\n{Format(cue.Start, ',')} --> {Format(cue.End, ',')}\n{cue.Text}\n\n",
                        _ => (cue.Identifier is null ? "" : cue.Identifier + "\n") + $"{Format(cue.Start, '.')} --> {Format(cue.End, '.')}{cue.Settings}\n{cue.Text}\n\n"
                    };
                    await text.WriteAsync(serialized.AsMemory(), token).ConfigureAwait(false);
                }
            }, cancellationToken).ConfigureAwait(false);
            progress?.Report(new(1, "Subtitle copy ready"));
            return new ConversionResult([output]);
        }, cancellationToken);

    private static List<Cue> Parse(string content, bool vtt, CancellationToken token)
    {
        string[] lines = content.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int position = 0;
        if (vtt)
        {
            if (lines.Length == 0 || !(lines[0] == "WEBVTT" || lines[0].StartsWith("WEBVTT ", StringComparison.Ordinal) || lines[0].StartsWith("WEBVTT\t", StringComparison.Ordinal)))
                throw Malformed(1, "Expected WEBVTT header.");
            while (position < lines.Length && !string.IsNullOrWhiteSpace(lines[position])) position++;
        }
        var cues = new List<Cue>();
        while (position < lines.Length)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(lines[position])) { position++; continue; }
            int start = position;
            while (position < lines.Length && !string.IsNullOrWhiteSpace(lines[position])) position++;
            int end = position;
            string first = lines[start];
            if (vtt && (first == "NOTE" || first.StartsWith("NOTE ", StringComparison.Ordinal) || first.StartsWith("NOTE\t", StringComparison.Ordinal) || first is "STYLE" or "REGION")) continue;
            string? identifier = null;
            if (!first.Contains("-->", StringComparison.Ordinal)) { identifier = vtt ? first : null; start++; }
            if (start >= end) throw Malformed(start + 1, "Missing cue timestamps.");
            var timing = Timing.Match(lines[start]);
            if (!timing.Success) throw Malformed(start + 1, "Expected start --> end timestamps.");
            long from = ParseTimestamp(timing.Groups["start"].Value, start + 1);
            long to = ParseTimestamp(timing.Groups["end"].Value, start + 1);
            if (to <= from) throw Malformed(start + 1, "End timestamp must be after start.");
            if (start + 1 == end) throw Malformed(start + 1, "Cue text is missing.");
            string settings = timing.Groups["settings"].Value.Trim();
            cues.Add(new(from, to, string.Join('\n', lines[(start + 1)..end]), identifier, settings.Length == 0 || !vtt ? "" : " " + settings));
        }
        if (cues.Count == 0) throw Malformed(1, "No subtitle cues found.");
        return cues;
    }

    private static long ParseTimestamp(string text, int line)
    {
        var match = Timestamp.Match(text);
        if (!match.Success) throw Malformed(line, $"Invalid timestamp '{text}'.");
        try
        {
            long hours = match.Groups["h"].Success ? long.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture) : 0;
            int minutes = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
            int seconds = int.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);
            int milliseconds = int.Parse(match.Groups["ms"].Value, CultureInfo.InvariantCulture);
            if (minutes > 59 || seconds > 59) throw Malformed(line, $"Invalid timestamp '{text}'.");
            return checked(((hours * 60 + minutes) * 60 + seconds) * 1000 + milliseconds);
        }
        catch (OverflowException) { throw Malformed(line, "Timestamp is too large."); }
    }
    private static string Format(long milliseconds, char separator) =>
        FormattableString.Invariant($"{milliseconds / 3600000:D2}:{milliseconds / 60000 % 60:D2}:{milliseconds / 1000 % 60:D2}{separator}{milliseconds % 1000:D3}");
    private static InvalidDataException Malformed(int line, string detail) => new($"Malformed subtitle at line {line}: {detail}");
    private sealed record Cue(long Start, long End, string Text, string? Identifier, string Settings);
}
