using System.Globalization;

namespace Citrus.Conversion;

/// <summary>One-based user ranges, in requested order; blank selects every page. Duplicate pages are rejected.</summary>
public static class PdfPageSelection
{
    public static IReadOnlyList<int> Parse(string? selection, int pageCount)
    {
        if (pageCount < 1) throw new ArgumentOutOfRangeException(nameof(pageCount));
        if (string.IsNullOrWhiteSpace(selection)) return Enumerable.Range(1, pageCount).ToArray();
        var pages = new List<int>();
        var seen = new HashSet<int>();
        foreach (string item in selection.Split(','))
        {
            string[] range = item.Trim().Split('-');
            if (range.Length is < 1 or > 2 || !TryPage(range[0], out int first) ||
                (range.Length == 2 && !TryPage(range[1], out _))) throw Invalid();
            int last = range.Length == 2 ? int.Parse(range[1].Trim(), CultureInfo.InvariantCulture) : first;
            if (first < 1 || first > last || last > pageCount) throw Invalid();
            for (int page = first; ; page++)
            {
                if (!seen.Add(page)) throw Invalid();
                pages.Add(page);
                if (page == last) break;
            }
        }
        return pages.ToArray();

        static bool TryPage(string value, out int page) => int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out page);
        static ArgumentException Invalid() => new("Choose unique one-based pages or ascending ranges within the document, for example 1,3-5.", nameof(selection));
    }
}
