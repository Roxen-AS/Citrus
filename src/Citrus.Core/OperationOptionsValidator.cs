using System.Globalization;

namespace Citrus.Core;

/// <summary>Shared dialog/submission validation. Inspection failures belong to individual jobs.</summary>
public static class OperationOptionsValidator
{
    public static string? GetError(OperationDescriptor operation, OperationOptions options, IReadOnlyList<InputInspection> inspections)
    {
        if (options.Quality is < 1 or > 100) return "Quality must be between 1 and 100.";
        string id = operation.Id;
        if (id is "image.resize" or "video.resize" && (options.Width is <= 0 || options.Height is <= 0 || options.Width is null && options.Height is null))
            return "Enter at least one positive pixel dimension.";
        if (id == "image.rotate" && options.RotationDegrees is not (90 or 180 or 270)) return "Choose 90, 180 or 270 degrees.";
        if (id == "video.speed" && (!double.IsFinite(options.Speed) || options.Speed is < .25 or > 4)) return "Speed must be between 0.25 and 4 times.";
        if (id == "audio.normalize" && (!double.IsFinite(options.NormalizeLufs) || options.NormalizeLufs is < -70 or > -5)) return "Normalization must be between -70 and -5 LUFS.";
        if (id is "video.trim" or "audio.trim")
        {
            if (!double.IsFinite(options.StartSeconds) || options.StartSeconds < 0 || options.EndSeconds is double end && (!double.IsFinite(end) || end <= options.StartSeconds))
                return "Start must be nonnegative and end must be later than start.";
            foreach (var input in inspections.Where(i => i.Error is null && i.DurationSeconds is not null))
                if (options.StartSeconds >= input.DurationSeconds || options.EndSeconds > input.DurationSeconds)
                    return $"{Path.GetFileName(input.Path)} is {input.DurationSeconds:0.###} seconds long. Trim must fit within every readable input.";
        }
        if (id is "pdf.split" or "pdf.images" or "pdf.text")
        {
            // Validate syntax even when all inputs are unreadable, then bounds for every readable input.
            if (!PagesValid(options.PageSelection, int.MaxValue)) return "Use unique one-based pages or ascending ranges, for example 1,3-5.";
            foreach (var input in inspections.Where(i => i.Error is null && i.PageCount is not null))
                if (!PagesValid(options.PageSelection, input.PageCount!.Value)) return $"Page selection exceeds {Path.GetFileName(input.Path)} ({input.PageCount} pages).";
        }
        return null;
    }

    private static bool PagesValid(string? value, int count)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var ranges = new List<(int Start, int End)>();
        foreach (string part in value.Split(','))
        {
            string[] pair = part.Trim().Split('-');
            if (pair.Length is < 1 or > 2 || !int.TryParse(pair[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int start)) return false;
            int end = start;
            if (pair.Length == 2 && !int.TryParse(pair[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out end)) return false;
            if (start < 1 || end < start || end > count || ranges.Any(r => start <= r.End && end >= r.Start)) return false;
            ranges.Add((start, end));
        }
        return true;
    }
}
