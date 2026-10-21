using Citrus.Conversion;

namespace Citrus.Conversion.Tests;

public sealed class MediaBinaryProcessTests : IDisposable
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancellation_retries_the_owned_child_when_tree_enumeration_is_denied(bool aggregate)
    {
        using var cancel = new CancellationTokenSource();
        var calls = new List<bool>();
        using var registration = cancel.Token.Register(() => MediaProcess.TerminateChild(tree =>
        {
            calls.Add(tree);
            if (tree)
            {
                var denied = new System.ComponentModel.Win32Exception(5);
                throw aggregate ? new AggregateException(new AggregateException(denied)) : denied;
            }
        }));
        Assert.Null(Record.Exception(cancel.Cancel));
        Assert.Equal(new[] { true, false }, calls);
    }

    [Fact]
    public void Cancellation_tolerates_an_exited_child_and_denied_direct_stop()
    {
        Assert.Null(Record.Exception(() => MediaProcess.TerminateChild(_ => throw new InvalidOperationException())));
        Assert.Null(Record.Exception(() => MediaProcess.TerminateChild(_ => throw new System.ComponentModel.Win32Exception(5))));
        Assert.Throws<IOException>(() => MediaProcess.TerminateChild(_ => throw new IOException("Unexpected failure")));
    }

    private readonly string directory = Path.Combine(Path.GetTempPath(), "Citrus-binary-" + Guid.NewGuid().ToString("N"));
    public MediaBinaryProcessTests() => Directory.CreateDirectory(directory);
    private MediaProcess Script(string body)
    {
        string path = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, "#!/bin/sh\n" + body);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return new(path, path);
    }
    [Fact]
    public async Task Raw_binary_invalid_utf8_is_unchanged_and_stderr_drains()
    {
        if (OperatingSystem.IsWindows()) return;
        var process = Script("head -c 200000 /dev/zero >&2\nprintf '\\377\\200\\000\\376'\n");
        using var output = new MemoryStream();
        Assert.Equal(4, await process.RunBinaryAsync([], output, 4, CancellationToken.None));
        Assert.Equal(new byte[] {255,128,0,254}, output.ToArray());
    }
    [Fact]
    public async Task Overlong_binary_and_structured_probe_fail_instead_of_truncating()
    {
        if (OperatingSystem.IsWindows()) return;
        var process = Script("head -c 100000 /dev/zero\n");
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<MediaProcessException>(() => process.RunBinaryAsync([], output, 12, CancellationToken.None));
        Assert.True(output.Length <= 12);
        await Assert.ThrowsAsync<MediaProcessException>(() => process.ProbeJsonAsync([], 12, CancellationToken.None));
    }
    [Fact]
    public async Task Binary_cancel_kills_tree_and_reaps_child()
    {
        if (OperatingSystem.IsWindows()) return;
        string pid = Path.Combine(directory, "pid");
        var process = Script("sleep 60 &\necho $! > '" + pid + "'\nwait\n");
        using var cancel = new CancellationTokenSource(300);
        using var output = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => process.RunBinaryAsync([], output, 12, cancel.Token));
        string stat = "/proc/" + File.ReadAllText(pid).Trim() + "/stat";
        Assert.True(!File.Exists(stat) || File.ReadAllText(stat).Contains(") Z ", StringComparison.Ordinal));
    }
    [Fact]
    public async Task Binary_error_keeps_bounded_diagnostics()
    {
        if (OperatingSystem.IsWindows()) return;
        var process = Script("head -c 100000 /dev/zero | tr '\\0' x >&2\nexit 9\n");
        using var output = new MemoryStream();
        var error = await Assert.ThrowsAsync<MediaProcessException>(() => process.RunBinaryAsync([], output, 12, CancellationToken.None));
        Assert.InRange(error.Message.Length, 32000, 33000);
    }
    public void Dispose() => Directory.Delete(directory, true);
}
