using System.Text.RegularExpressions;

namespace Citrus.Core.Updates;

/// <summary>SemVer precedence, including numeric prerelease identifiers of arbitrary length.</summary>
public sealed partial class ReleaseVersion : IComparable<ReleaseVersion>
{
    private readonly string[] numbers;
    private readonly string[] prerelease;
    public bool IsPrerelease => prerelease.Length != 0;

    private ReleaseVersion(string[] numbers, string[] prerelease) { this.numbers = numbers; this.prerelease = prerelease; }

    [GeneratedRegex(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool TryParse(string? text, out ReleaseVersion? version)
    {
        version = null;
        if (text is null || text.Length > 256) return false;
        var match = Pattern().Match(text);
        if (!match.Success) return false;
        string[] pre = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];
        if (pre.Any(value => IsNumeric(value) && value.Length > 1 && value[0] == '0')) return false;
        version = new([match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value], pre);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        for (int i = 0; i < numbers.Length; i++)
        {
            int comparison = CompareNumeric(numbers[i], other.numbers[i]);
            if (comparison != 0) return comparison;
        }
        if (!IsPrerelease || !other.IsPrerelease) return IsPrerelease == other.IsPrerelease ? 0 : IsPrerelease ? -1 : 1;
        for (int i = 0; i < Math.Min(prerelease.Length, other.prerelease.Length); i++)
        {
            string a = prerelease[i], b = other.prerelease[i];
            bool an = IsNumeric(a), bn = IsNumeric(b);
            int comparison = an && bn ? CompareNumeric(a, b) : an != bn ? an ? -1 : 1 : string.CompareOrdinal(a, b);
            if (comparison != 0) return comparison;
        }
        return prerelease.Length.CompareTo(other.prerelease.Length);
    }

    private static bool IsNumeric(string value) => value.All(c => c is >= '0' and <= '9');
    private static int CompareNumeric(string a, string b) => a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
}
