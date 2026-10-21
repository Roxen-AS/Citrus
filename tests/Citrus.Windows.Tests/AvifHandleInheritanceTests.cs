using Citrus.Conversion;
using ImageMagick;
using System.Diagnostics;
using System.IO;
using Xunit.Abstractions;

namespace Citrus.Windows.Tests;

public sealed class AvifHandleInheritanceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("filename-control")]
    [InlineData("stream-control")]
    [InlineData("production-verifier")]
    [InlineData("production-source")]
    public async Task Owned_child_at_native_avif_read_boundary_preserves_file_lifetime(string scenario)
    {
        string root = Directory.CreateTempSubdirectory("citrus-avif-inheritance-").FullName;
        using var reader = AvifHandleInheritanceHost.Start("--avif-handle-reader", scenario, root);
        var stdout = reader.StandardOutput.ReadToEndAsync();
        var stderr = reader.StandardError.ReadToEndAsync();
        try
        {
            await reader.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));
            string observations = await stdout;
            output.WriteLine(observations);
            string trace = await stderr;
            Assert.True(reader.ExitCode == 0, observations + "\nNative trace tail:\n" + trace[^Math.Min(4000, trace.Length)..]);
            Assert.Contains("CHILD_READY_DURING_NATIVE_READ", observations);
            Assert.Contains("CHILD_EXITED", observations);
            Assert.Contains("UNLOCKED_AFTER_CHILD_EXIT", observations);
            if (scenario == "filename-control") Assert.Contains("LOCKED_WHILE_CHILD_ALIVE:32", observations);
            else Assert.Contains("USER_FILE_RELEASED_WHILE_CHILD_ALIVE", observations);
        }
        finally
        {
            if (!reader.HasExited) reader.Kill(entireProcessTree: true);
            await reader.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Directory.Delete(root, true);
        }
    }
}

/// <summary>Runs in its own test process, so the temporary native trace hook never affects other tests.</summary>
internal static class AvifHandleInheritanceHost
{
    // This child is deliberately given no image filename or directory and performs no file IO.
    internal static int Hold()
    {
        Console.WriteLine("READY");
        return Console.ReadLine() == "release" ? 0 : 3;
    }

    internal static Process Start(params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(LifecycleHost).Assembly.Location);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new IOException("Could not start the owned AVIF test child.");
    }

    internal static int Read(string scenario, string root)
    {
        Process? holder = null;
        Task<string>? holderErrors = null;
        Exception? callbackFailure = null;
        bool enteredAvif = false, startedHolder = false;
        void AtNativeRead(object? _, LogEventArgs args)
        {
            // ImageMagick 7.1.2-33 ReadHEICImage calls SetImageProperty in
            // ReadHEICImageHandle AFTER heif_context_read_from_file succeeds and
            // BEFORE heif_context_free. The first such callback is a real open-file barrier.
            if (args.Message.Contains("/ReadHEICImage/", StringComparison.Ordinal)) enteredAvif = true;
            if (!enteredAvif || startedHolder || !args.Message.Contains("/SetImageProperty/", StringComparison.Ordinal)) return;
            startedHolder = true;
            try
            {
                Console.WriteLine("NATIVE_READ_BARRIER:" + args.Message.Replace('\n', ' '));
                holder = Start("--avif-handle-holder");
                holderErrors = holder.StandardError.ReadToEndAsync();
                string? ready = holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
                if (ready != "READY") throw new IOException("Owned holder did not acknowledge startup.");
                Console.WriteLine($"CHILD_READY_DURING_NATIVE_READ:parent={Environment.ProcessId};child={holder.Id}");
            }
            catch (Exception failure) { callbackFailure = failure; } // Do not unwind through a native callback.
        }

        try
        {
            var engine = new ImageEngine(); // Initialize the same native policy as production.
            bool sourceRead = scenario == "production-source";
            string source = Path.Combine(root, scenario == "production-verifier" ? "input.png" : "input.avif");
            using (var image = new MagickImage(MagickColors.Orange, 80, 40))
            using (var encoded = File.Create(source))
                image.Write(encoded, scenario == "production-verifier" ? MagickFormat.Png : MagickFormat.Avif);

            string probePath = source;
            MagickNET.SetLogEvents(LogEventTypes.Trace);
            MagickNET.Log += AtNativeRead;
            try
            {
                if (scenario is "production-verifier" or "production-source")
                {
                    var operation = engine.Operations.Single(operation => operation.Id == (sourceRead ? "image.convert.png" : "image.convert.avif"));
                    var result = engine.ExecuteAsync(new(operation, [source], new(), null), null, default).GetAwaiter().GetResult();
                    if (!sourceRead) probePath = result.Outputs.Single();
                }
                else if (scenario == "filename-control")
                {
                    using var check = new MagickImage(source, new MagickReadSettings { Format = MagickFormat.Avif, FrameCount = 1 });
                    RequireDimensions(check);
                }
                else if (scenario == "stream-control")
                {
                    using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var check = new MagickImage(stream, new MagickReadSettings { Format = MagickFormat.Avif, FrameCount = 1 });
                    RequireDimensions(check);
                }
                else throw new ArgumentException("Unknown inheritance scenario.", nameof(scenario));
            }
            finally
            {
                MagickNET.Log -= AtNativeRead;
                MagickNET.SetLogEvents(LogEventTypes.None);
            }

            if (callbackFailure is not null) throw new InvalidOperationException("Native barrier child startup failed.", callbackFailure);
            if (!startedHolder || holder is null) throw new InvalidOperationException("The required native AVIF read barrier was not observed.");
            if (holder.HasExited) throw new InvalidOperationException("Owned holder exited before the file lifetime assertion.");
            if (scenario == "filename-control")
            {
                try { RequireExclusive(probePath); }
                catch (IOException failure) when ((failure.HResult & 0xffff) == 32)
                {
                    Console.WriteLine("LOCKED_WHILE_CHILD_ALIVE:32");
                    ReleaseHolder(holder);
                    AvifControlCleanup.RequireExclusiveAfterExit(() => RequireExclusive(probePath), Console.WriteLine);
                    Console.WriteLine("UNLOCKED_AFTER_CHILD_EXIT");
                    return 0;
                }
                throw new InvalidOperationException("Filename control did not reproduce the inherited sharing lock.");
            }

            RequireExclusive(probePath);
            string moved = probePath + ".moved";
            File.Move(probePath, moved);
            File.Move(moved, probePath);
            Console.WriteLine("USER_FILE_RELEASED_WHILE_CHILD_ALIVE");
            ReleaseHolder(holder);
            RequireExclusive(probePath);
            Console.WriteLine("UNLOCKED_AFTER_CHILD_EXIT");
            return 0;
        }
        catch (Exception failure)
        {
            Console.WriteLine("FAILURE:" + failure);
            if (failure.Data["Citrus.OutputWriter.CleanupFailure"] is Exception cleanup)
                Console.WriteLine("SECONDARY_CLEANUP:" + cleanup);
            return 1;
        }
        finally
        {
            if (holder is not null)
            {
                try
                {
                    if (!holder.HasExited) holder.Kill(entireProcessTree: true);
                    holder.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                    Console.WriteLine("HOLDER_REAPED");
                    if (holderErrors is not null) Console.WriteLine("HOLDER_STDERR:" + holderErrors.GetAwaiter().GetResult());
                }
                finally { holder.Dispose(); }
            }
        }
    }

    private static void ReleaseHolder(Process holder)
    {
        holder.StandardInput.WriteLine("release");
        holder.StandardInput.Flush();
        holder.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        if (holder.ExitCode != 0) throw new IOException($"Owned holder exited with code {holder.ExitCode}.");
        Console.WriteLine("CHILD_EXITED");
    }

    private static void RequireDimensions(MagickImage image)
    {
        if (image.Width != 80 || image.Height != 40) throw new InvalidDataException("AVIF dimensions changed.");
    }

    private static void RequireExclusive(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        if (file.Length == 0) throw new InvalidDataException("Image file is empty.");
    }
}
