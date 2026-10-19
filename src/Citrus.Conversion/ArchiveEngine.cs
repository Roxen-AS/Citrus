using System.Formats.Tar;
using System.IO.Compression;
using Citrus.Core;
using SharpCompress.Archives.Rar;
using SharpCompress.Common.Rar;
using SharpCompress.Readers;

namespace Citrus.Conversion;

/// <summary>Offline archive creation and transactional, bounded extraction of regular files.</summary>
public sealed class ArchiveEngine : IConversionEngine
{
    public const int MaximumEntries = 100000;
    public const long MaximumExpandedBytes = 20L * 1024 * 1024 * 1024;
    private readonly int maxEntries;
    private readonly long maxExpandedBytes;
    private readonly OutputWriter writer = new();

    /// <summary>Hosts may lower extraction budgets, but cannot exceed the release safety ceilings.</summary>
    public ArchiveEngine(int maxEntries = MaximumEntries, long maxExpandedBytes = MaximumExpandedBytes)
    {
        if (maxEntries is < 1 or > MaximumEntries) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        if (maxExpandedBytes is < 1 or > MaximumExpandedBytes) throw new ArgumentOutOfRangeException(nameof(maxExpandedBytes));
        this.maxEntries = maxEntries;
        this.maxExpandedBytes = maxExpandedBytes;
    }

    public IReadOnlyList<OperationDescriptor> Operations { get; } = Array.AsReadOnly(new[]
    {
        new OperationDescriptor("archive.zip", "ZIP", OperationMode.Convert, ".zip", true, false),
        new OperationDescriptor("archive.tar", "TAR", OperationMode.Convert, ".tar", true, false),
        new OperationDescriptor("archive.gzip", "GZIP", OperationMode.Convert, ".gz", false, false),
        new OperationDescriptor("archive.extract", "Extract", OperationMode.Tools, null, false, false)
    });

    public bool CanExecute(OperationRequest request)
    {
        var operation = Operations.FirstOrDefault(o => o.Id == request.Operation.Id);
        if (operation is null || (!operation.IsGroup && request.Inputs.Count != 1) ||
            !DropValidation.Accepts(request.Inputs, operation.Mode)) return false;
        if (request.Inputs.Any(p => File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint))) return false;
        return operation.Id != "archive.extract" || new[] { ".zip", ".tar", ".gz", ".gzip", ".rar" }
            .Contains(Path.GetExtension(request.Inputs[0]), StringComparer.OrdinalIgnoreCase);
    }

    public Task<ConversionResult> ExecuteAsync(OperationRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanExecute(request)) throw new NotSupportedException("Choose compatible regular files; GZIP and extraction take one input.");
            string input = request.Inputs[0];
            string parent = Path.GetDirectoryName(input)!;
            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            if (request.DestinationDirectory is null && request.Inputs.Any(p => !comparer.Equals(parent, Path.GetDirectoryName(p))))
                throw new ArgumentException("Choose an explicit destination when inputs come from different directories.", nameof(request));
            string destination = request.DestinationDirectory ?? parent;
            string output;
            if (request.Operation.Id == "archive.extract")
            {
                string desired = Path.Combine(destination, Path.GetFileNameWithoutExtension(input) + "-extracted");
                output = await writer.WriteDirectoryAsync(desired, (temporary, token) => ExtractAsync(input, temporary, progress, token), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var operation = Operations.Single(o => o.Id == request.Operation.Id);
                string name = operation.Id == "archive.gzip" ? Path.GetFileName(input) : Path.GetFileNameWithoutExtension(input);
                string desired = Path.Combine(destination, name + operation.OutputExtension);
                output = await writer.WriteFileAsync(desired, (temporary, token) => CreateAsync(request, temporary, token), cancellationToken).ConfigureAwait(false);
            }
            progress?.Report(new(1, "Archive copy ready"));
            return new ConversionResult([output]);
        }, cancellationToken);

    private static async Task CreateAsync(OperationRequest request, string temporary, CancellationToken token)
    {
        // Use the same portable name/collision rules as extraction before creating an archive.
        var names = new SafeArchivePath(Path.GetDirectoryName(temporary)!);
        foreach (string input in request.Inputs) names.Claim(Path.GetFileName(input), false);
        await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        switch (request.Operation.Id)
        {
            case "archive.zip":
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                    foreach (string input in request.Inputs)
                    {
                        token.ThrowIfCancellationRequested();
                        await using var source = OpenInput(input);
                        await using var target = zip.CreateEntry(Path.GetFileName(input), CompressionLevel.Optimal).Open();
                        await source.CopyToAsync(target, token).ConfigureAwait(false);
                    }
                break;
            case "archive.tar":
                await using (var tar = new TarWriter(output, leaveOpen: true))
                    foreach (string input in request.Inputs)
                    {
                        token.ThrowIfCancellationRequested();
                        await using var source = OpenInput(input);
                        await tar.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, Path.GetFileName(input)) { DataStream = source }, token).ConfigureAwait(false);
                    }
                break;
            case "archive.gzip":
                await using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
                await using (var source = OpenInput(request.Inputs[0]))
                    await source.CopyToAsync(gzip, token).ConfigureAwait(false);
                break;
        }
    }

    private async Task ExtractAsync(string input, string staging, IProgress<ConversionProgress>? progress, CancellationToken token)
    {
        var paths = new SafeArchivePath(staging, allowDotPrefixes: Path.GetExtension(input).Equals(".tar", StringComparison.OrdinalIgnoreCase));
        var budget = new ExtractionBudget(maxEntries, maxExpandedBytes);
        await using var source = OpenInput(input);
        switch (Path.GetExtension(input).ToLowerInvariant())
        {
            case ".zip":
                using (var zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
                {
                    if (zip.Entries.Count > maxEntries) throw budget.EntryLimit();
                    foreach (var entry in zip.Entries)
                    {
                        token.ThrowIfCancellationRequested();
                        RejectAttributes(entry.ExternalAttributes >> 16, entry.ExternalAttributes & 0xFFFF);
                        bool directory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
                        string output = Claim(entry.FullName, directory);
                        if (directory) continue;
                        await using var data = entry.Open();
                        await CopyAsync(data, output).ConfigureAwait(false);
                    }
                }
                break;
            case ".tar":
                using (var tar = new TarReader(source, leaveOpen: true))
                {
                    TarEntry? entry;
                    while ((entry = await tar.GetNextEntryAsync(copyData: false, cancellationToken: token).ConfigureAwait(false)) is not null)
                    {
                        token.ThrowIfCancellationRequested();
                        if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
                            throw new InvalidDataException("Archive links are not supported.");
                        if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.Directory))
                            throw new InvalidDataException("Archive contains an unsupported special entry.");
                        bool directory = entry.EntryType == TarEntryType.Directory;
                        string output = Claim(entry.Name, directory);
                        if (!directory) await CopyAsync(entry.DataStream ?? Stream.Null, output).ConfigureAwait(false);
                    }
                }
                break;
            case ".rar":
                using (var rar = RarArchive.OpenArchive(source, new ReaderOptions { LeaveStreamOpen = true }))
                    foreach (var entry in rar.Entries)
                    {
                        token.ThrowIfCancellationRequested();
                        // Generic LinkTarget is always null for this pinned RAR reader. Inspect both generations explicitly.
                        if (entry is not RarEntry metadata) throw new InvalidDataException("Unsupported RAR entry metadata.");
                        if (metadata.IsRedir || metadata.RedirTargetName is not null || entry.LinkTarget is not null)
                            throw new InvalidDataException("Archive links and redirections are not supported.");
                        int attributes = entry.Attrib ?? 0;
                        RejectAttributes(attributes, attributes);
                        if (entry.IsEncrypted) throw new InvalidDataException("Encrypted archive entries are not supported.");
                        if (!entry.IsComplete) throw new InvalidDataException("Incomplete or multi-volume archive entries are not supported.");
                        string output = Claim(entry.Key, entry.IsDirectory);
                        if (entry.IsDirectory) continue;
                        await using var data = entry.OpenEntryStream();
                        await CopyAsync(data, output).ConfigureAwait(false);
                    }
                break;
            case ".gz":
            case ".gzip":
                string target = Claim(Path.GetFileNameWithoutExtension(input), false);
                await using (var gzip = new GZipStream(source, CompressionMode.Decompress, leaveOpen: true))
                    await CopyAsync(gzip, target).ConfigureAwait(false);
                break;
        }

        string Claim(string? name, bool directory)
        {
            token.ThrowIfCancellationRequested();
            budget.AddEntry();
            string path = paths.Claim(name, directory);
            if (directory) Directory.CreateDirectory(path);
            else Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return path;
        }
        async Task CopyAsync(Stream data, string path)
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            byte[] buffer = new byte[81920];
            while (true)
            {
                token.ThrowIfCancellationRequested();
                // Read at most the remaining budget plus one detection byte; never write that excess byte.
                int length = (int)Math.Min(buffer.Length, budget.Remaining + 1);
                int read = await data.ReadAsync(buffer.AsMemory(0, length), token).ConfigureAwait(false);
                if (read == 0) break;
                budget.AddBytes(read);
                await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                progress?.Report(new(null, $"Extracting ({budget.Bytes:N0} expanded bytes)"));
                token.ThrowIfCancellationRequested();
            }
        }
    }

    private static FileStream OpenInput(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    private static void RejectAttributes(int unix, int windows)
    {
        int kind = unix & 0xF000;
        if (kind == 0xA000 || (windows & (int)FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Archive links and reparse entries are not supported.");
        if (kind is not (0 or 0x8000 or 0x4000)) throw new InvalidDataException("Archive contains an unsupported special entry.");
    }
    private sealed class ExtractionBudget(int entriesLimit, long bytesLimit)
    {
        private int entries;
        public long Bytes { get; private set; }
        public long Remaining => bytesLimit - Bytes;
        public InvalidDataException EntryLimit() => new(FormattableString.Invariant($"Archive exceeds the extraction limit of {entriesLimit:N0} entries (maximum 100,000)."));
        public void AddEntry() { if (entries >= entriesLimit) throw EntryLimit(); entries++; }
        public void AddBytes(int count)
        {
            if (count > Remaining) throw new InvalidDataException(FormattableString.Invariant($"Archive exceeds the expanded data limit of {bytesLimit:N0} bytes (maximum 20 GiB)."));
            Bytes += count;
        }
    }
}
