namespace Citrus.Core.Editing;

/// <summary>One process-wide budget for editor and ordinary file preview decoding.</summary>
public static class PreviewWorkScheduler
{
    private static readonly SemaphoreSlim Workers = new(2, 2);
    public static async ValueTask<IDisposable> AcquireAsync(CancellationToken ct)
    {
        await Workers.WaitAsync(ct).ConfigureAwait(false);
        return new Lease();
    }
    private sealed class Lease : IDisposable
    {
        private int released;
        public void Dispose() { if (Interlocked.Exchange(ref released, 1) == 0) Workers.Release(); }
    }
}
