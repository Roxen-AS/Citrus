namespace Citrus.Core;

public static class DropValidation
{
    private const FileAttributes Unavailable = FileAttributes.Offline | (FileAttributes)0x40000 | (FileAttributes)0x400000;
    public static bool AttributesAllowed(FileAttributes attributes, OperationMode mode) =>
        (attributes & Unavailable) == 0 && (mode == OperationMode.Actions || !attributes.HasFlag(FileAttributes.Directory));

    /// <summary>Only inspect filesystem metadata; never hydrate an offline/recall cloud placeholder.</summary>
    public static bool Accepts(IReadOnlyList<string> paths, OperationMode mode)
    {
        if (paths.Count == 0) return false;
        try
        {
            return paths.All(path => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) &&
                AttributesAllowed(File.GetAttributes(path), mode));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
