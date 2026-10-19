using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using Citrus.Core;

namespace Citrus.Conversion.Tests;

public sealed class ArchiveEngineTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Citrus archives ü " + Guid.NewGuid());
    public ArchiveEngineTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private string Input(string name, string text = "Hello ü\n")
    {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, text);
        return path;
    }
    private static Task<ConversionResult> Run(ArchiveEngine engine, string id, string[] paths, IProgress<ConversionProgress>? progress = null, CancellationToken token = default) =>
        engine.ExecuteAsync(new(engine.Operations.Single(o => o.Id == id), paths, new(), null), progress, token);
    private string Zip(params (string Name, string Text, int Attributes)[] entries)
    {
        string path = Path.Combine(directory, "input.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var item in entries)
        {
            var entry = zip.CreateEntry(item.Name);
            entry.ExternalAttributes = item.Attributes;
            using var stream = new StreamWriter(entry.Open());
            stream.Write(item.Text);
        }
        return path;
    }
    private async Task Reject(string path, ArchiveEngine? engine = null, string? message = null)
    {
        var before = Directory.GetFileSystemEntries(directory).Order().ToArray();
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Run(engine ?? new(), "archive.extract", [path]));
        if (message is not null) Assert.Contains(message, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, Directory.GetFileSystemEntries(directory).Order().ToArray());
    }

    [Theory]
    [InlineData("zip")]
    [InlineData("tar")]
    [InlineData("gzip")]
    public async Task Round_trip_preserves_original_and_collision_safe_output(string format)
    {
        var engine = new ArchiveEngine();
        string input = Input("first.txt");
        byte[] original = File.ReadAllBytes(input);
        string archive = Assert.Single((await Run(engine, "archive." + format, [input])).Outputs);
        Assert.Equal(format == "gzip" ? "first.txt.gz" : "first-archive." + format, Path.GetFileName(archive));
        byte[] archiveBytes = File.ReadAllBytes(archive);
        string extracted = Assert.Single((await Run(engine, "archive.extract", [archive])).Outputs);
        Assert.Equal(original, File.ReadAllBytes(Assert.Single(Directory.GetFiles(extracted))));
        string second = Assert.Single((await Run(engine, "archive.extract", [archive])).Outputs);
        Assert.NotEqual(extracted, second);
        Assert.Equal(original, File.ReadAllBytes(input));
        Assert.Equal(archiveBytes, File.ReadAllBytes(archive));
    }

    [Theory]
    [InlineData("zip")]
    [InlineData("tar")]
    public async Task Group_creation_preserves_order_and_bytes(string format)
    {
        var engine = new ArchiveEngine();
        string a = Input("a.txt", "A"), b = Input("b.txt", "B");
        string forward = Assert.Single((await Run(engine, "archive." + format, [a, b])).Outputs);
        Assert.Equal("a-archive." + format, Path.GetFileName(forward));
        string occupied = Input("b-archive." + format, "existing archive bytes");
        string archive = Assert.Single((await Run(engine, "archive." + format, [b, a])).Outputs);
        Assert.Equal("b-archive (1)." + format, Path.GetFileName(archive));
        Assert.Equal("existing archive bytes", File.ReadAllText(occupied));
        string extracted = Assert.Single((await Run(engine, "archive.extract", [archive])).Outputs);
        Assert.Equal("A", File.ReadAllText(Path.Combine(extracted, "a.txt")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(extracted, "b.txt")));
        if (format == "zip") { using var zip = ZipFile.OpenRead(archive); Assert.Equal(new[] { "b.txt", "a.txt" }, zip.Entries.Select(e => e.FullName)); }
        else
        {
            using var file = File.OpenRead(archive);
            using var tar = new TarReader(file);
            Assert.Equal("b.txt", tar.GetNextEntry()!.Name);
            Assert.Equal("a.txt", tar.GetNextEntry()!.Name);
            Assert.Null(tar.GetNextEntry());
        }
    }

    [LinuxFileLinkTheory]
    [InlineData("zip")]
    [InlineData("tar")]
    [InlineData("gzip")]
    public async Task Ready_source_link_copies_bytes_and_linked_outer_archive_extracts(string format)
    {
        var engine = new ArchiveEngine();
        string target = Input("target.txt", "Readable source ü");
        string selected = Path.Combine(directory, "selected.txt");
        File.CreateSymbolicLink(selected, target);
        Assert.True(File.GetAttributes(selected).HasFlag(FileAttributes.ReparsePoint));
        string[] inputs = format == "gzip" ? [selected] : [selected, Input("other.txt", "other")];
        var request = new OperationRequest(engine.Operations.Single(o => o.Id == "archive." + format), inputs, new(), null);
        Assert.True(DropValidation.Accepts(inputs, OperationMode.Convert));
        Assert.True(engine.CanExecute(request));
        string archive = Assert.Single((await engine.ExecuteAsync(request, null, default)).Outputs);
        if (format == "zip")
        {
            using var zip = ZipFile.OpenRead(archive);
            var entry = zip.Entries[0];
            Assert.Equal("selected.txt", entry.FullName);
            Assert.NotEqual(0xA000, (entry.ExternalAttributes >> 16) & 0xF000);
            Assert.Equal(0, entry.ExternalAttributes & (int)FileAttributes.ReparsePoint);
        }
        else if (format == "tar")
        {
            using var file = File.OpenRead(archive);
            using var tar = new TarReader(file);
            var entry = tar.GetNextEntry()!;
            Assert.Equal("selected.txt", entry.Name);
            Assert.Equal(TarEntryType.RegularFile, entry.EntryType);
        }
        string outer = Path.Combine(directory, "linked" + Path.GetExtension(archive));
        File.CreateSymbolicLink(outer, archive);
        Assert.True(File.GetAttributes(outer).HasFlag(FileAttributes.ReparsePoint));
        var extract = new OperationRequest(engine.Operations.Single(o => o.Id == "archive.extract"), [outer], new(), null);
        Assert.True(engine.CanExecute(extract));
        string output = Assert.Single((await engine.ExecuteAsync(extract, null, default)).Outputs);
        Assert.Equal(File.ReadAllBytes(target), File.ReadAllBytes(Path.Combine(output, format == "gzip" ? "linked" : "selected.txt")));
        Assert.Equal("Readable source ü", File.ReadAllText(target));
        Assert.DoesNotContain(Directory.GetFileSystemEntries(directory), p => Path.GetFileName(p).StartsWith('.'));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("..\\escape.txt")]
    [InlineData("C:\\escape.txt")]
    [InlineData("C:escape.txt")]
    [InlineData("\\\\server\\share\\escape.txt")]
    [InlineData("/escape.txt")]
    [InlineData("folder/../../escape.txt")]
    [InlineData("file.txt:stream")]
    [InlineData("NUL.txt")]
    [InlineData("NUL .txt")]
    [InlineData("folder./file")]
    public async Task Unsafe_zip_paths_are_rejected_before_commit(string name) => await Reject(Zip((name, "bad", 0)), message: "path");

    [Theory]
    [InlineData("same.txt", "same.txt")]
    [InlineData("same.txt", "SAME.TXT")]
    [InlineData("folder/name.txt", "folder\\name.txt")]
    [InlineData("file", "file/child")]
    public async Task Duplicate_and_conflicting_paths_roll_back(string a, string b) => await Reject(Zip((a, "A", 0), (b, "B", 0)));

    [Theory]
    [InlineData(unchecked((int)0xA1FF0000))]
    [InlineData(0x400)]
    public async Task Zip_unix_symlinks_and_reparse_attributes_rejected(int attributes) => await Reject(Zip(("link", "target", attributes)), message: "link");

    [Theory]
    [InlineData(TarEntryType.SymbolicLink)]
    [InlineData(TarEntryType.HardLink)]
    public async Task Tar_links_rejected(TarEntryType type)
    {
        string path = Path.Combine(directory, "input.tar");
        using (var file = File.Create(path)) using (var tar = new TarWriter(file))
            tar.WriteEntry(new PaxTarEntry(type, "link") { LinkName = "../target" });
        await Reject(path, message: "link");
    }

    [Theory]
    [InlineData("test_read_format_rar.rar")]
    [InlineData("test_read_format_rar5_symlink.rar")]
    [InlineData("test_read_format_rar5_hardlink.rar")]
    public async Task Real_rar_link_metadata_is_rejected_and_staging_removed(string name)
    {
        string path = Path.Combine(directory, name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Archives", name), path);
        await Reject(path, message: "link");
    }

    [Theory]
    [InlineData("Rar4.rar")]
    [InlineData("Rar5.rar")]
    public async Task Real_rar_extracts_original_payload_bytes(string name)
    {
        string path = Path.Combine(directory, name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Archives", name), path);
        byte[] original = SHA256.HashData(File.ReadAllBytes(path));
        string output = Assert.Single((await Run(new(), "archive.extract", [path])).Outputs);
        var files = Directory.GetFiles(output, "*", SearchOption.AllDirectories);
        Assert.Equal(3, files.Length);
        Assert.Contains(files, p => Path.GetExtension(p) == ".txt");
        Assert.Equal(45056, new FileInfo(Path.Combine(output, "exe", "test.exe")).Length);
        // Independently decoded from the RAR4 fixture with the system libarchive reader.
        Assert.Equal("8557928804f57ecc340b3bb38b095a3607474ec8deb0076f316fcfe02b562106", Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(output, "exe", "test.exe")))));
        Assert.Equal("b251c7501fb0f55dd4a92feabe0a6f5733bc40a02679498155fae9b30138fc53", Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(output, "jpg", "test.jpg")))));
        Assert.Equal("4d581d93d369f6e1c9b295ff38d82dabd577f927dfaf0c35818c015c85e322d9", Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(files.Single(p => Path.GetExtension(p) == ".txt")))));
        Assert.Equal(original, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Theory]
    [InlineData("zip")]
    [InlineData("tar")]
    [InlineData("gzip")]
    public async Task Expanded_byte_limit_counts_actual_copied_bytes_and_rolls_back(string format)
    {
        string source = Input("large.txt", new string('x', 8193));
        string archive = Assert.Single((await Run(new(), "archive." + format, [source])).Outputs);
        await Reject(archive, new ArchiveEngine(maxExpandedBytes: 8192), "expanded");
        string output = Assert.Single((await Run(new ArchiveEngine(maxExpandedBytes: 8193), "archive.extract", [archive])).Outputs);
        Assert.Equal(8193, new FileInfo(Assert.Single(Directory.GetFiles(output))).Length);
    }

    [Fact]
    public async Task Entry_limit_includes_directories_and_rolls_back() =>
        await Reject(Zip(("dir/", "", 0), ("dir/file", "", 0)), new ArchiveEngine(maxEntries: 1), "entries");

    [Fact]
    public async Task Default_entry_limit_rejects_entry_100001()
    {
        string path = Path.Combine(directory, "many.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            for (int i = 0; i <= 100000; i++) zip.CreateEntry($"{i}/");
        await Reject(path, message: "100,000");
    }

    [Fact]
    public async Task Cancellation_during_extract_cleans_staging_and_preserves_source()
    {
        string path = Zip(("one", new string('x', 1000000), 0), ("two", "second", 0));
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(_ => cancellation.Cancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(new(), "archive.extract", [path], progress, cancellation.Token));
        Assert.Equal(new[] { path }, Directory.GetFileSystemEntries(directory));
    }

    [Fact]
    public void Directory_selection_and_multi_file_gzip_are_not_offered()
    {
        var engine = new ArchiveEngine();
        Assert.False(engine.CanExecute(new(engine.Operations.Single(o => o.Id == "archive.zip"), [directory], new(), null)));
        Assert.False(engine.CanExecute(new(engine.Operations.Single(o => o.Id == "archive.gzip"), [Input("a"), Input("b")], new(), null)));
    }
    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("..\\escape.txt")]
    [InlineData("C:\\escape.txt")]
    [InlineData("\\\\server\\share\\escape.txt")]
    public async Task Tar_unsafe_paths_are_rejected(string name)
    {
        string path = Path.Combine(directory, "unsafe.tar");
        using (var file = File.Create(path)) using (var tar = new TarWriter(file))
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream([1, 2, 3]) });
        await Reject(path, message: "path");
    }

    [Fact]
    public async Task Cumulative_limit_applies_across_files_and_empty_files_count()
    {
        string path = Zip(("a", "12345", 0), ("b", "12345", 0));
        await Reject(path, new ArchiveEngine(maxExpandedBytes: 9), "expanded");
        string output = Assert.Single((await Run(new ArchiveEngine(maxEntries: 2, maxExpandedBytes: 10), "archive.extract", [path])).Outputs);
        Assert.Equal(2, Directory.GetFiles(output).Length);
    }

    [Fact]
    public async Task Rar_payloads_obey_actual_byte_budget()
    {
        string path = Path.Combine(directory, "Rar5.rar");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Archives", "Rar5.rar"), path);
        await Reject(path, new ArchiveEngine(maxExpandedBytes: 10), "expanded");
    }

    [Fact]
    public void Hosts_cannot_raise_release_limits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArchiveEngine(maxEntries: 100001));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArchiveEngine(maxExpandedBytes: 20L * 1024 * 1024 * 1024 + 1));
    }

    [Fact]
    public async Task Implicit_parent_then_explicit_directory_is_valid()
    {
        string path = Zip(("folder/child.txt", "child", 0), ("folder/", "", 0));
        string output = Assert.Single((await Run(new(), "archive.extract", [path])).Outputs);
        Assert.Equal("child", File.ReadAllText(Path.Combine(output, "folder", "child.txt")));
    }

    [Fact]
    public async Task Conventional_tar_dot_root_and_prefixed_files_extract_with_bytes()
    {
        string path = Path.Combine(directory, "conventional.tar");
        using (var file = File.Create(path)) using (var tar = new TarWriter(file))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "./"));
            tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "./nested/"));
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "./nested/child.txt") { DataStream = new MemoryStream([1, 2, 3]) });
        }
        byte[] original = File.ReadAllBytes(path);
        string output = Assert.Single((await Run(new(), "archive.extract", [path])).Outputs);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(output, "nested", "child.txt")));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Tar_dot_prefix_alias_duplicate_rejects_complete_extraction()
    {
        string path = Path.Combine(directory, "aliases.tar");
        using (var file = File.Create(path)) using (var tar = new TarWriter(file))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "./name") { DataStream = new MemoryStream([1]) });
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "name") { DataStream = new MemoryStream([2]) });
        }
        await Reject(path, message: "duplicate");
    }

    [Theory]
    [InlineData(".")]
    [InlineData("./")]
    [InlineData("./../escape")]
    [InlineData(".//absolute")]
    [InlineData("./C:escape")]
    [InlineData("./file:stream")]
    public async Task Tar_dot_prefix_does_not_allow_file_root_or_unsafe_names(string name)
    {
        string path = Path.Combine(directory, "unsafe-dot.tar");
        using (var file = File.Create(path)) using (var tar = new TarWriter(file))
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream([1]) });
        await Reject(path, message: "path");
    }

    [Fact]
    public async Task Tar_dot_root_counts_against_entry_budget()
    {
        string path = Path.Combine(directory, "dot-limit.tar");
        using (var file = File.Create(path)) using (var tar = new TarWriter(file))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "./"));
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "./file") { DataStream = new MemoryStream([1]) });
        }
        await Reject(path, new ArchiveEngine(maxEntries: 1), "entries");
    }

    private sealed class InlineProgress(Action<ConversionProgress> report) : IProgress<ConversionProgress>
    { public void Report(ConversionProgress value) => report(value); }
}
