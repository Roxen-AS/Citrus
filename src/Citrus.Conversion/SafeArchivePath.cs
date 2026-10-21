namespace Citrus.Conversion;

/// <summary>Validates Windows-compatible archive names on every host; tracks explicit and implied paths.</summary>
internal sealed class SafeArchivePath(string root, bool allowDotPrefixes = false)
{
    private readonly string rootPrefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
    private readonly Dictionary<string, bool> paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> explicitPaths = new(StringComparer.OrdinalIgnoreCase);

    public string Claim(string? name, bool directory)
    {
        string normalized = (name ?? "").Replace('\\', '/');
        if (allowDotPrefixes)
        {
            bool dotRoot = normalized == "." || normalized.StartsWith("./", StringComparison.Ordinal);
            while (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized[2..];
            if (dotRoot && directory && normalized is "" or ".")
            {
                if (!explicitPaths.Add("")) throw new InvalidDataException($"Duplicate archive path '{name}'.");
                return Path.GetFullPath(root);
            }
        }
        if (directory) normalized = normalized.TrimEnd('/');
        string[] components = normalized.Split('/');
        if (components.Any(InvalidComponent)) throw new InvalidDataException($"Unsafe archive path '{name}'.");
        string relative = string.Join(Path.DirectorySeparatorChar, components);
        string full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Archive path escapes extraction directory: '{name}'.");
        if (!explicitPaths.Add(relative)) throw new InvalidDataException($"Duplicate archive path '{name}'.");
        string ancestor = "";
        for (int i = 0; i < components.Length; i++)
        {
            ancestor = i == 0 ? components[i] : Path.Combine(ancestor, components[i]);
            bool needsDirectory = i < components.Length - 1 || directory;
            if (paths.TryGetValue(ancestor, out bool existingDirectory) && (!existingDirectory || !needsDirectory))
                throw new InvalidDataException($"Conflicting archive path '{name}'.");
            paths[ancestor] = needsDirectory;
            string resolved = Path.Combine(root, ancestor);
            if ((File.Exists(resolved) || Directory.Exists(resolved)) && File.GetAttributes(resolved).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException($"Archive path traverses a link: '{name}'.");
        }
        return full;
    }

    private static bool InvalidComponent(string component)
    {
        if (string.IsNullOrEmpty(component) || component is "." or ".." || component.EndsWith('.') || component.EndsWith(' ') ||
            component.Any(c => c < 32 || "<>:\"|?*".Contains(c))) return true;
        string stem = component.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(stem[3]));
    }
}
