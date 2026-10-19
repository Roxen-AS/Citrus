using System.Diagnostics;
using System.IO;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class ResidentLifecycleTests
{
    [Fact]
    public async Task Resident_hides_reopens_activates_pauses_quits_and_releases_identity()
    {
        using var files = new NativeFiles();
        string identity = Guid.NewGuid().ToString("N");
        using var first = Start(identity, files.Root, background: true);
        Assert.StartsWith("READY:", await ReadLine(first));
        Assert.Contains("home=False", await Command(first, "state"));
        Assert.Contains("home=True", await Command(first, "open"));
        Assert.Contains("home=False", await Command(first, "close"));
        Assert.False(first.HasExited);
        using (var second = Start(identity, files.Root, background: false))
        {
            await second.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(0, second.ExitCode);
        }
        Assert.Contains("home=True", await Command(first, "state"));
        Assert.Contains("wheel=False", await Command(first, "pause"));
        await Quit(first);
        using var restarted = Start(identity, files.Root, background: true);
        Assert.StartsWith("READY:", await ReadLine(restarted));
        await Quit(restarted);
    }

    [Fact]
    public async Task Quit_waits_for_owned_shell_move_then_releases_resources()
    {
        using var files = new NativeFiles();
        string source = files.File("move-source.txt", "owned move");
        Directory.CreateDirectory(Path.Combine(files.Root, "move-target"));
        string identity = Guid.NewGuid().ToString("N");
        using var release = new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\Citrus-test-move-" + identity);
        using var child = Start(identity, files.Root, background: true, ownedMove: true);
        Assert.StartsWith("READY:", await ReadLine(child));
        Assert.Equal("MOVE_READY", await Command(child, "move"));
        Assert.Equal("QUIT_REQUESTED", await Command(child, "quit"));
        // The worker is held at the real Shell boundary. Quit has started but must keep the process alive.
        await Task.Delay(500);
        Assert.False(child.HasExited);
        Assert.True(File.Exists(source));
        release.Set();
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(0, child.ExitCode);
        Assert.Contains("DISPOSED:True", await child.Process.StandardOutput.ReadToEndAsync());
        Assert.False(File.Exists(source));
        Assert.Equal("owned move", File.ReadAllText(Path.Combine(files.Root, "move-target", "move-source.txt")));
    }

    private static ChildProcess Start(string identity, string root, bool background, bool ownedMove = false)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(LifecycleHost).Assembly.Location);
        start.ArgumentList.Add("--resident-test");
        start.ArgumentList.Add(identity);
        start.ArgumentList.Add(root);
        if (background) start.ArgumentList.Add("--background");
        if (ownedMove) start.ArgumentList.Add("--owned-move");
        return new ChildProcess(Process.Start(start) ?? throw new IOException("Could not start the isolated WPF test resident."));
    }
    private static async Task<string> ReadLine(ChildProcess child) =>
        await child.Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(45)) ?? throw new IOException("Resident exited before responding. " + await child.Errors);
    private static async Task<string> Command(ChildProcess child, string command)
    {
        await child.Process.StandardInput.WriteLineAsync(command);
        await child.Process.StandardInput.FlushAsync();
        return await ReadLine(child);
    }
    private static async Task Quit(ChildProcess child)
    {
        await child.Process.StandardInput.WriteLineAsync("quit");
        await child.Process.StandardInput.FlushAsync();
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(0, child.ExitCode);
        Assert.Contains("DISPOSED:True", await child.Process.StandardOutput.ReadToEndAsync());
        Assert.Equal("", await child.Errors);
    }
    private sealed class ChildProcess : IDisposable
    {
        public Process Process { get; }
        public Task<string> Errors { get; }
        public bool HasExited => Process.HasExited;
        public int ExitCode => Process.ExitCode;
        public ChildProcess(Process process) { Process = process; Errors = Process.StandardError.ReadToEndAsync(); }
        public Task WaitForExitAsync() => Process.WaitForExitAsync();
        public void Dispose()
        {
            if (!Process.HasExited) { Process.Kill(entireProcessTree: true); Process.WaitForExit(10000); }
            Process.Dispose();
        }
    }
}
