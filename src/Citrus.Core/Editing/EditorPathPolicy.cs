namespace Citrus.Core.Editing;

/// <summary>Windows filename rules also apply when validating on another host platform.</summary>
public static class EditorPathPolicy
{
    public static bool IsLeaf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.EndsWith(' ') || name.EndsWith('.') || name.Length > 240 ||
            name.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c))) return false;
        var basename = name.Split('.')[0].TrimEnd(' ', '.');
        return !(basename.Equals("CON",StringComparison.OrdinalIgnoreCase) || basename.Equals("PRN",StringComparison.OrdinalIgnoreCase) || basename.Equals("AUX",StringComparison.OrdinalIgnoreCase) || basename.Equals("NUL",StringComparison.OrdinalIgnoreCase) ||
            basename.Equals("CONIN$",StringComparison.OrdinalIgnoreCase) || basename.Equals("CONOUT$",StringComparison.OrdinalIgnoreCase) ||
            (basename.Length == 4 && (basename.StartsWith("COM",StringComparison.OrdinalIgnoreCase) || basename.StartsWith("LPT",StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(basename[3])));
    }
    public static string Normalize(string path) => Path.GetFullPath(path);
    public static bool Same(string left, string right) => StringComparer.OrdinalIgnoreCase.Equals(Normalize(left),Normalize(right));
}
