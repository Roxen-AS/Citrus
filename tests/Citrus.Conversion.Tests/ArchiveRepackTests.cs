using System.Formats.Tar;
using System.IO.Compression;
using Citrus.Core;

namespace Citrus.Conversion.Tests;

public sealed class ArchiveRepackTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "citrus-repack-" + Guid.NewGuid().ToString("N"));
    public ArchiveRepackTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private string Zip()
    {
        string path = Path.Combine(root, "members.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var pair in new[] { ("folder/a.txt", "Alpha"), ("b.txt", "Beta") })
        { using var sink = new StreamWriter(archive.CreateEntry(pair.Item1).Open()); sink.Write(pair.Item2); }
        return path;
    }
    private static Task<ConversionResult> Run(ArchiveEngine engine, string id, string path) =>
        engine.ExecuteAsync(new(engine.Operations.Single(o => o.Id == id), [path], new(), null), null, default);

    [Fact]
    public async Task Zip_to_tar_preserves_members_instead_of_nesting_source_archive()
    {
        string input = Zip(); byte[] original = File.ReadAllBytes(input);
        var engine = new ArchiveEngine();
        string tarPath = Assert.Single((await Run(engine, "archive.tar", input)).Outputs);
        using var file = File.OpenRead(tarPath); using var tar = new TarReader(file);
        var entries = new Dictionary<string, string>(); TarEntry? entry;
        while ((entry = tar.GetNextEntry()) is not null)
            if (entry.EntryType != TarEntryType.Directory) { using var reader = new StreamReader(entry.DataStream!); entries.Add(entry.Name, reader.ReadToEnd()); }
        Assert.Equal("Alpha", entries["folder/a.txt"]); Assert.Equal("Beta", entries["b.txt"]);
        Assert.DoesNotContain("members.zip", entries.Keys); Assert.Equal(original, File.ReadAllBytes(input));
    }

    [Fact]
    public async Task Archive_to_gzip_is_tar_gzip_and_extracts_members_in_one_operation()
    {
        var engine = new ArchiveEngine();
        string output = Assert.Single((await Run(engine, "archive.gzip", Zip())).Outputs);
        Assert.EndsWith(".tar.gz", output, StringComparison.OrdinalIgnoreCase);
        string extracted = Assert.Single((await Run(engine, "archive.extract", output)).Outputs);
        Assert.Equal("Alpha", File.ReadAllText(Path.Combine(extracted, "folder", "a.txt")));
        Assert.Equal("Beta", File.ReadAllText(Path.Combine(extracted, "b.txt")));
        Assert.Empty(Directory.GetFiles(extracted, "*.tar", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Repacking_rejects_unsafe_members_before_publishing()
    {
        string path = Path.Combine(root, "bad.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) { using var sink = new StreamWriter(zip.CreateEntry("../escaped.txt").Open()); sink.Write("bad"); }
        var engine = new ArchiveEngine();
        await Assert.ThrowsAsync<InvalidDataException>(() => Run(engine, "archive.tar", path));
        Assert.Equal(new[] { path }, Directory.GetFiles(root));
    }
    [Fact]
    public async Task Multiple_selected_archives_repack_members_without_nesting_containers()
    {
        string first=Zip(),second=Path.Combine(root,"second.zip");using(var zip=ZipFile.Open(second,ZipArchiveMode.Create)){using var sink=new StreamWriter(zip.CreateEntry("other.txt").Open());sink.Write("Other");}
        var engine=new ArchiveEngine();var result=await engine.ExecuteAsync(new(engine.Operations.Single(o=>o.Id=="archive.tar"),[first,second],new(),null),null,default);
        using var stream=File.OpenRead(Assert.Single(result.Outputs));using var tar=new TarReader(stream);var names=new List<string>();TarEntry? entry;while((entry=tar.GetNextEntry())is not null)names.Add(entry.Name);
        Assert.Contains("folder/a.txt",names);Assert.Contains("other.txt",names);Assert.DoesNotContain("members.zip",names);Assert.DoesNotContain("second.zip",names);
    }
    [Fact]
    public async Task Multiple_archive_repacking_shares_the_extraction_entry_budget()
    {
        var paths=new List<string>();for(int index=0;index<3;index++){string path=Path.Combine(root,$"{index}.zip");using var zip=ZipFile.Open(path,ZipArchiveMode.Create);using var sink=new StreamWriter(zip.CreateEntry($"{index}.txt").Open());sink.Write("value");paths.Add(path);}
        var engine=new ArchiveEngine(maxEntries:2);await Assert.ThrowsAsync<InvalidDataException>(()=>engine.ExecuteAsync(new(engine.Operations.Single(o=>o.Id=="archive.tar"),paths,new(),null),null,default));
        Assert.Equal(paths.Order(),Directory.GetFiles(root).Order());
    }
}
