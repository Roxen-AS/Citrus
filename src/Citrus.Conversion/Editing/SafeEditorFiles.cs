namespace Citrus.Conversion.Editing;

internal static class SafeEditorFiles
{
    // Inspect ancestors as well: a regular payload leaf behind a linked directory still escapes ownership.
    internal static void RejectLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            var entry = new FileInfo(current);
            if (entry.LinkTarget is not null || ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0))
                throw new IOException("Symbolic links and reparse points are not permitted in editor staging.");
        }
    }
}
