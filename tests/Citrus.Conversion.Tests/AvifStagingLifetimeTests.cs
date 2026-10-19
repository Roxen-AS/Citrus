using Citrus.Core;
using ImageMagick;
using System.Runtime.InteropServices;
using System.Text;
using Xunit.Abstractions;

namespace Citrus.Conversion.Tests;

/// <summary>Phase diagnostics for the native Windows AVIF sharing violation. No retries.</summary>
public sealed class AvifStagingLifetimeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Production_avif_pipeline_releases_each_output_for_immediate_move_and_delete()
    {
        string root = CreateRoot();
        try
        {
            var engine = new ImageEngine();
            string source = CreateSource(root);
            var operation = engine.Operations.Single(operation => operation.Id == "image.convert.avif");
            for (int iteration = 0; iteration < 16; iteration++)
            {
                output.WriteLine($"Production iteration {iteration}: ExecuteAsync starting; process {Environment.ProcessId}");
                var result = await engine.ExecuteAsync(new(operation, [source], new(), null), null, default);
                string path = Assert.Single(result.Outputs);
                AssertAvailable(path, "production output committed");
                string moved = path + ".moved";
                File.Move(path, moved);
                File.Delete(moved);
                Assert.Empty(Directory.GetFiles(root, ".citrus-*"));
            }
        }
        catch (Exception failure) { LogFailure(failure); LogWindowsOwners(root); throw; }
        finally { Cleanup(root); }
    }

    [Theory]
    [InlineData("filename")]
    [InlineData("stream")]
    [InlineData("bytes")]
    public async Task Avif_staging_is_released_at_encode_verify_and_commit_boundaries(string verification)
    {
        string root = CreateRoot();
        string phase = "load source";
        try
        {
            string source = CreateSource(root);
            using var input = File.OpenRead(source);
            using var image = new MagickImage(input, new MagickReadSettings { Format = MagickFormat.Png });
            image.Quality = 85;
            for (int iteration = 0; iteration < 16; iteration++)
            {
                output.WriteLine($"Verifier {verification}, iteration {iteration}, process {Environment.ProcessId}");
                string result = await new OutputWriter().WriteFileAsync(Path.Combine(root, "output.avif"), async (staging, token) =>
                {
                    try
                    {
                        phase = "encode AVIF";
                        await image.WriteAsync(staging, MagickFormat.Avif, token);
                        phase = "exclusive open after encode";
                        AssertAvailable(staging, phase);
                        phase = $"verify via {verification}";
                        var settings = new MagickReadSettings { Format = MagickFormat.Avif, FrameCount = 1 };
                        if (verification == "stream")
                        {
                            using var stream = File.OpenRead(staging);
                            using var check = new MagickImage(stream, settings);
                            AssertDimensions(check);
                        }
                        else
                        {
                            using var check = verification == "bytes"
                                ? new MagickImage(await File.ReadAllBytesAsync(staging, token), settings)
                                : new MagickImage(staging, settings);
                            AssertDimensions(check);
                        }
                        phase = "exclusive open after verifier disposal";
                        AssertAvailable(staging, phase);
                        phase = "atomic commit";
                    }
                    catch (Exception failure)
                    {
                        output.WriteLine($"CALLBACK FAILURE PHASE (before cleanup/source disposal): {phase}");
                        LogFailure(failure);
                        LogWindowsOwners(root);
                        throw;
                    }
                }, default);
                phase = "exclusive open after commit";
                AssertAvailable(result, phase);
                File.Delete(result);
                Assert.Empty(Directory.GetFiles(root, ".citrus-*"));
            }
        }
        catch (Exception failure)
        {
            output.WriteLine($"FIRST FAILURE PHASE: {phase}");
            LogFailure(failure);
            LogWindowsOwners(root);
            throw;
        }
        finally { Cleanup(root); }
    }

    private static void AssertDimensions(MagickImage check)
    {
        Assert.Equal(80u, check.Width);
        Assert.Equal(40u, check.Height);
    }

    private void AssertAvailable(string path, string phase)
    {
        // Windows FileShare.None conflicts with any surviving native/managed read handle.
        // Unix success does not establish Windows delete-sharing behavior.
        using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        output.WriteLine($"{phase}: exclusive access succeeded ({probe.Length} bytes)");
    }

    private static string CreateRoot() => Directory.CreateTempSubdirectory("citrus-avif-lifetime-").FullName;

    private static string CreateSource(string root)
    {
        string source = Path.Combine(root, "original.png");
        using var image = new MagickImage(MagickColors.Orange, 80, 40);
        image.SetAttribute("comment", "private metadata");
        var profile = new ExifProfile();
        profile.SetValue(ExifTag.Artist, "private author");
        image.SetProfile(profile);
        image.Write(source);
        return source;
    }

    private void LogFailure(Exception failure)
    {
        output.WriteLine($"Original failure: {failure}");
        if (failure.Data["Citrus.OutputWriter.CleanupFailure"] is Exception cleanup)
            output.WriteLine($"Secondary staging cleanup failure: {cleanup}");
    }

    private void LogWindowsOwners(string root)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            foreach (string path in Directory.GetFiles(root, "*.avif"))
            {
                // Restart Manager is read-only here: identify owners; never shut down processes.
                uint result = RmStartSession(out uint session, 0, new StringBuilder(33));
                if (result != 0) { output.WriteLine($"RmStartSession: {result}"); continue; }
                try
                {
                    result = RmRegisterResources(session, 1, [path], 0, IntPtr.Zero, 0, IntPtr.Zero);
                    if (result != 0) { output.WriteLine($"RmRegisterResources: {result}"); continue; }
                    uint count = 0, reasons = 0;
                    result = RmGetList(session, out uint needed, ref count, null, ref reasons);
                    if (result == 0) { output.WriteLine($"No Restart Manager owners at inspection: {path}"); continue; }
                    if (result != 234) { output.WriteLine($"RmGetList: {result}"); continue; }
                    var owners = new RestartManagerProcess[needed];
                    count = needed;
                    result = RmGetList(session, out needed, ref count, owners, ref reasons);
                    output.WriteLine($"Restart Manager result {result} for {path}; current process {Environment.ProcessId}");
                    if (result == 0)
                        for (int index = 0; index < count; index++)
                            output.WriteLine($"Owner PID {owners[index].Process.ProcessId}: {owners[index].ApplicationName}; service {owners[index].ServiceName}");
                }
                finally { RmEndSession(session); }
            }
        }
        catch (Exception diagnosticFailure)
        {
            output.WriteLine($"Owner inspection failed: {diagnosticFailure}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RestartManagerUniqueProcess
    {
        public uint ProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME StartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RestartManagerProcess
    {
        public RestartManagerUniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ApplicationName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string ServiceName;
        public uint ApplicationType;
        public uint Status;
        public uint SessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern uint RmStartSession(out uint session, uint flags, StringBuilder key);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern uint RmRegisterResources(uint session, uint fileCount, string[] files, uint applicationCount, IntPtr applications, uint serviceCount, IntPtr services);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern uint RmGetList(uint session, out uint needed, ref uint count, [In, Out] RestartManagerProcess[]? processes, ref uint reasons);

    [DllImport("rstrtmgr.dll")]
    private static extern uint RmEndSession(uint session);

    private void Cleanup(string root)
    {
        try { Directory.Delete(root, true); }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // A fixture cleanup failure must not mask the phase and original exception.
            output.WriteLine($"Diagnostic fixture cleanup failure: {failure}");
        }
    }
}
