using System.Collections.Immutable;
using System.Security.Cryptography;
using Citrus.Core.Editing;

namespace Citrus.Conversion.Editing;

public sealed class SourceSnapshotService : ISourceSnapshotService
{
    public Task<ImmutableArray<SourceCaptureResult>> CaptureAsync(ImmutableArray<string> paths, CancellationToken ct) => Task.Run(async () =>
    {
        if (paths.IsDefault) throw new ArgumentException("Paths must be initialized.");
        var results = ImmutableArray.CreateBuilder<SourceCaptureResult>();
        foreach (string path in paths)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                string fullPath = Path.GetFullPath(path);
                var before = new FileInfo(fullPath);
                long length = before.Length, ticks = before.LastWriteTimeUtc.Ticks;
                await using var input = OpenRead(fullPath);
                string hash = Convert.ToHexString(await SHA256.HashDataAsync(input,ct).ConfigureAwait(false));
                before.Refresh();
                if (!before.Exists || length != before.Length || ticks != before.LastWriteTimeUtc.Ticks || input.Length != length)
                    throw new IOException("Source changed during capture. Reload the file.");
                results.Add(new(path,new(Guid.NewGuid(),fullPath,length,ticks,hash),null));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
            {
                results.Add(new(path,null,error.Message));
            }
        }
        ct.ThrowIfCancellationRequested();
        return results.ToImmutable();
    },ct);

    public async Task<ImmutableDictionary<Guid,string>> StageVerifiedAsync(ImmutableArray<SourceSnapshot> sources, string privateDirectory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (sources.IsDefaultOrEmpty || sources.Any(s => s.Id == Guid.Empty || s.Length < 0 || s.Sha256.Length != 64 || !s.Sha256.All(Uri.IsHexDigit)) || sources.Select(s => s.Id).Distinct().Count() != sources.Length)
            throw new ArgumentException("Invalid source snapshots.");
        var root = Path.GetFullPath(privateDirectory);
        if (sources.Any(s => IsWithin(root,Path.GetFullPath(s.Path)))) throw new ArgumentException("Private staging cannot contain an original source.");
        SafeEditorFiles.RejectLinks(root);
        bool createdRoot = !Directory.Exists(root);
        string owned = Path.Combine(root,"sources");
        if (Directory.Exists(owned) || File.Exists(owned)) throw new IOException("Sources staging already exists; use a fresh private directory.");
        bool ownsSources = false;
        try
        {
            Directory.CreateDirectory(owned); ownsSources = true;
            var result = ImmutableDictionary.CreateBuilder<Guid,string>();
            foreach (var source in sources)
            {
                ct.ThrowIfCancellationRequested();
                // Generated names preserve the decoder suffix without accepting path components from an input.
                string suffix = Path.GetExtension(source.Path);
                if (suffix.Length > 20 || suffix.Any(c => c != '.' && !char.IsAsciiLetterOrDigit(c))) throw new ArgumentException("Invalid source suffix.");
                string copy = Path.Combine(owned,source.Id.ToString("N") + suffix);
                await using var input = OpenRead(source.Path);
                await using var output = new FileStream(copy,FileMode.CreateNew,FileAccess.Write,FileShare.None,128*1024,FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[128*1024]; long bytes = 0;
                int read;
                while ((read = await input.ReadAsync(buffer,ct).ConfigureAwait(false)) != 0)
                {
                    bytes = checked(bytes + read);
                    if (bytes > source.Length) throw new IOException("Source length changed. Reload the file.");
                    hash.AppendData(buffer,0,read);
                    await output.WriteAsync(buffer.AsMemory(0,read),ct).ConfigureAwait(false);
                }
                await output.FlushAsync(ct).ConfigureAwait(false);
                if (bytes != source.Length || !Convert.ToHexString(hash.GetHashAndReset()).Equals(source.Sha256,StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Source content changed. Reload the file.");
                result.Add(source.Id,copy);
            }
            ct.ThrowIfCancellationRequested();
            return result.ToImmutable();
        }
        catch
        {
            if (ownsSources && Directory.Exists(owned)) Directory.Delete(owned,true);
            if (createdRoot && Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
            throw;
        }
    }
    private static bool IsWithin(string root,string path) => path.Equals(root,StringComparison.OrdinalIgnoreCase) || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    private static FileStream OpenRead(string path) => new(path,FileMode.Open,FileAccess.Read,FileShare.Read,128*1024,FileOptions.Asynchronous | FileOptions.SequentialScan);
}
