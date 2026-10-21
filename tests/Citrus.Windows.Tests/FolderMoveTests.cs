using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class FolderMoveTests(DesktopFixture desktop)
{
    [Fact]
    public Task Folder_move_preserves_contents_and_does_not_merge_or_replace() => desktop.RunAsync(async () =>
    {
        using var files = new NativeFiles();
        string source = Directory.CreateDirectory(Path.Combine(files.Root, "source Ω")).FullName;
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        File.WriteAllText(Path.Combine(source, "nested", "original.txt"), "original");
        string target = Directory.CreateDirectory(Path.Combine(files.Root, "target")).FullName;
        string conflict = Directory.CreateDirectory(Path.Combine(target, Path.GetFileName(source))).FullName;
        File.WriteAllText(Path.Combine(conflict, "keep.txt"), "keep");
        var actions = new FileActions(pickDestination: () => target);
        await Assert.ThrowsAsync<IOException>(() => actions.ExecuteAsync("action.move", [source]));
        Assert.True(File.Exists(Path.Combine(source, "nested", "original.txt")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(conflict, "keep.txt")));
        Directory.Delete(conflict, true);
        await actions.ExecuteAsync("action.move", [source]);
        Assert.False(Directory.Exists(source));
        Assert.Equal("original", File.ReadAllText(Path.Combine(conflict, "nested", "original.txt")));
    });

    [Fact]
    public Task Racing_shell_conflict_renames_instead_of_replacing_or_merging() => desktop.RunAsync(async () =>
    {
        using var files = new NativeFiles();
        string source = Directory.CreateDirectory(Path.Combine(files.Root, "source")).FullName;
        File.WriteAllText(Path.Combine(source, "content.txt"), "original");
        string target = Directory.CreateDirectory(Path.Combine(files.Root, "target")).FullName;
        string conflict = Path.Combine(target, "source");
        var actual = await ShellFileMover.MoveAsync([source], target, () =>
        {
            Directory.CreateDirectory(conflict);
            File.WriteAllText(Path.Combine(conflict, "content.txt"), "keep");
        });
        Assert.Equal("keep", File.ReadAllText(Path.Combine(conflict, "content.txt")));
        Assert.Single(actual);
        Assert.NotEqual(conflict, actual[0]);
        Assert.Equal("original", File.ReadAllText(Path.Combine(actual[0], "content.txt")));
        Assert.False(Directory.Exists(source));
    });

    [SecondVolumeFact]
    public Task Cross_volume_shell_move_preserves_folder_contents() => desktop.RunAsync(async () =>
    {
        using var files = new NativeFiles();
        string destinationRoot = Environment.GetEnvironmentVariable("CITRUS_TEST_SECOND_VOLUME")!;
        Assert.NotEqual(VolumeIdentity(files.Root), VolumeIdentity(Path.GetFullPath(destinationRoot)));
        string target = Directory.CreateDirectory(Path.Combine(destinationRoot, "Citrus-test-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string source = Directory.CreateDirectory(Path.Combine(files.Root, "folder Ω")).FullName;
            Directory.CreateDirectory(Path.Combine(source, "nested"));
            File.WriteAllBytes(Path.Combine(source, "nested", "payload.bin"), [0, 1, 255, 42]);
            await new FileActions(pickDestination: () => target).ExecuteAsync("action.move", [source]);
            Assert.False(Directory.Exists(source));
            Assert.Equal(new byte[] { 0, 1, 255, 42 }, File.ReadAllBytes(Path.Combine(target, "folder Ω", "nested", "payload.bin")));
        }
        finally { Directory.Delete(target, true); }
    });
    private static string VolumeIdentity(string path)
    {
        var mount = new StringBuilder(1024);
        if (!GetVolumePathName(path, mount, mount.Capacity)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var volume = new StringBuilder(1024);
        if (!GetVolumeNameForVolumeMountPoint(mount.ToString(), volume, volume.Capacity)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return volume.ToString().ToUpperInvariant();
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathName(string path, StringBuilder volumePath, int length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPoint(string volumePath, StringBuilder volumeName, int length);
}

public sealed class SecondVolumeFactAttribute : FactAttribute
{
    public SecondVolumeFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CITRUS_TEST_SECOND_VOLUME")))
            Skip = "Pending: set CITRUS_TEST_SECOND_VOLUME to a writable directory on another physical volume for the real Shell cross-volume test.";
    }
}
