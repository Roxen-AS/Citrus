using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;

namespace Citrus.Windows;

/// <summary>One resident per Windows user SID and logon session; IPC has the same scope and user-only ACL.</summary>
internal sealed class SingleInstance : IAsyncDisposable, IDisposable
{
    private readonly Mutex mutex;
    private readonly CancellationTokenSource stopping = new();
    private Task? listener;
    public string Identity { get; }
    public string MutexName => @"Local\" + Identity;
    public string PipeName => Identity + "-activate";
    public bool IsOwner { get; }
    public SingleInstance(string? testSuffix = null)
    {
        if (testSuffix is not null && !Guid.TryParseExact(testSuffix, "N", out _)) throw new ArgumentException("Instance suffix must be a unique test GUID.");
        using var user = WindowsIdentity.GetCurrent();
        using var process = Process.GetCurrentProcess();
        string sid = user.User?.Value ?? throw new InvalidOperationException("Windows user identity unavailable.");
        Identity = $"Citrus-{sid}-{process.SessionId}" + (testSuffix is null ? "" : "-test-" + testSuffix);
        mutex = new Mutex(false, MutexName);
        try { IsOwner = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsOwner = true; }
    }
    public void Listen(Action activate)
    {
        if (!IsOwner || listener is not null) throw new InvalidOperationException("Only the resident can listen once.");
        var initial = CreateServer();
        listener = Task.Run(async () =>
        {
            NamedPipeServerStream? server = initial;
            try
            {
                while (!stopping.IsCancellationRequested)
                {
                    using (server)
                    {
                        try
                        {
                            await server.WaitForConnectionAsync(stopping.Token);
                            using var readDeadline = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                            readDeadline.CancelAfter(TimeSpan.FromSeconds(2));
                            byte[] message = new byte[1];
                            if (await server.ReadAsync(message, readDeadline.Token) == 1 && message[0] == (byte)'A')
                            {
                                activate();
                                await server.WriteAsync(new byte[] { (byte)'K' }, readDeadline.Token);
                            }
                        }
                        catch (IOException) { }
                        catch (OperationCanceledException) when (!stopping.IsCancellationRequested) { }
                    }
                    if (stopping.IsCancellationRequested) break;
                    server = CreateServer();
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
            finally { server?.Dispose(); }
        });
    }
    private NamedPipeServerStream CreateServer() => new(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    public async Task ActivateAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(deadline.Token);
        await client.WriteAsync(new byte[] { (byte)'A' }, deadline.Token);
        byte[] reply = new byte[1];
        if (await client.ReadAsync(reply, deadline.Token) != 1 || reply[0] != (byte)'K') throw new IOException("The resident did not acknowledge activation.");
    }
    public async ValueTask DisposeAsync()
    {
        stopping.Cancel();
        if (listener is not null) await listener;
        Dispose();
    }
    public void Dispose()
    {
        stopping.Cancel();
        listener?.GetAwaiter().GetResult();
        // Mutex ownership remains on the creating UI thread across this awaited UI-context continuation.
        if (IsOwner) mutex.ReleaseMutex();
        mutex.Dispose();
        stopping.Dispose();
    }
}
