using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Citrus.Core;

/// <summary>Stages outputs beside their destination, then claims a name without replacing user files.</summary>
public sealed class OutputWriter
{
    public Task<string> WriteFileAsync(string desiredPath, Func<string, CancellationToken, Task> writeTemporary, CancellationToken cancellationToken) =>
        WriteAsync(desiredPath, writeTemporary, false, cancellationToken);

    public Task<string> WriteDirectoryAsync(string desiredPath, Func<string, CancellationToken, Task> writeTemporary, CancellationToken cancellationToken) =>
        WriteAsync(desiredPath, writeTemporary, true, cancellationToken);

    private static async Task<string> WriteAsync(string desiredPath, Func<string, CancellationToken, Task> writeTemporary, bool directory, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desiredPath);
        ArgumentNullException.ThrowIfNull(writeTemporary);
        token.ThrowIfCancellationRequested();
        var destination = Path.GetFullPath(desiredPath);
        var parent = Path.GetDirectoryName(destination)!;
        var extension = directory ? "" : Path.GetExtension(destination);
        var stem = directory ? Path.GetFileName(destination) : Path.GetFileNameWithoutExtension(destination);
        var staging = Path.Combine(parent, $".{stem}.citrus-{Guid.NewGuid():N}{extension}");
        try
        {
            if (directory) Directory.CreateDirectory(staging);
            await writeTemporary(staging, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (directory ? !Directory.Exists(staging) : !File.Exists(staging))
                throw new IOException("The conversion did not create its temporary output.");
            for (long suffix = 0; ; suffix++)
            {
                token.ThrowIfCancellationRequested();
                var candidate = suffix == 0 ? destination : Path.Combine(parent, $"{stem} ({suffix}){extension}");
                try
                {
                    Commit(staging, candidate, directory);
                    return candidate;
                }
                catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate))
                {
                    // Another conversion or user already owns this name. Retry without replacement.
                }
            }
        }
        finally
        {
            if (directory)
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
            else File.Delete(staging);
        }
    }

    private static void Commit(string staging, string candidate, bool directory)
    {
        if (OperatingSystem.IsLinux())
        {
            // .NET's Unix moves check existence before rename, which can replace a racing output.
            // RENAME_NOREPLACE makes the name claim atomic for both files and directories.
            if (RenameNoReplace(-100, staging, -100, candidate, 1) == 0) return;
            int error = Marshal.GetLastPInvokeError();
            throw new IOException("Could not commit the output without replacement.", new Win32Exception(error));
        }
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Atomic output commits are supported on Windows and Linux.");
        if (directory) Directory.Move(staging, candidate);
        else File.Move(staging, candidate, overwrite: false);
    }

    [DllImport("libc", EntryPoint = "renameat2", SetLastError = true)]
    private static extern int RenameNoReplace(int oldDirectory, string oldPath, int newDirectory, string newPath, uint flags);
}
