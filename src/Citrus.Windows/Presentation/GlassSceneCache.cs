namespace Citrus.Windows.Presentation;

/// <summary>Bounded shared immutable wheel fields; all construction happens on workers.</summary>
internal sealed class GlassSceneCache(long maximumBytes = 64 * 1024 * 1024,
    Func<int, double, bool, bool, GlassScene>? factory = null)
{
    private readonly object gate = new();
    private readonly long limit = maximumBytes > 0 ? maximumBytes : throw new ArgumentOutOfRangeException(nameof(maximumBytes));
    private readonly Dictionary<Key, Entry> entries = [];
    private long access;
    internal static GlassSceneCache Shared { get; } = new();
    internal long RetainedBytes { get { lock (gate) return Bytes(); } }

    internal GlassScene GetWheel(int count, double scale, bool navigationVisible, bool fullPresentation)
    {
        // Foreground actions never alter the continuous optical backing.
        var key = new Key(0, scale, fullPresentation, fullPresentation, GlassMaterial.Wheel);
        Entry entry;
        lock (gate)
        {
            if (!entries.TryGetValue(key, out entry!))
            {
                entry = new(new Lazy<GlassScene>(() => factory is not null
                    ? factory(0, scale, fullPresentation, fullPresentation)
                    : fullPresentation ? GlassScene.WheelPresentation(0, scale, true)
                    : GlassScene.Wheel(0, scale)));
                entries.Add(key, entry);
            }
            entry.LastAccess = ++access;
        }
        try
        {
            GlassScene scene = entry.Scene.Value;
            lock (gate)
            {
                while (Bytes() > limit || entries.Count > 2)
                {
                    var oldest = entries.Where(pair => pair.Value.Scene.IsValueCreated)
                        .OrderBy(pair => pair.Value.LastAccess).FirstOrDefault();
                    if (oldest.Value is null) break;
                    entries.Remove(oldest.Key);
                }
            }
            return scene;
        }
        catch
        {
            lock (gate)
                if (entries.TryGetValue(key, out var existing) && ReferenceEquals(existing, entry)) entries.Remove(key);
            throw;
        }
    }

    internal Task PrewarmAsync(double scale, CancellationToken token = default) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        D3D11StudioRenderer.PrecompileShaders();
        foreach (bool full in new[] { true, false })
        {
            token.ThrowIfCancellationRequested();
            GetWheel(0, scale, full, full);
        }
    }, token);

    private long Bytes() => entries.Values.Where(entry => entry.Scene.IsValueCreated)
        .Sum(entry => (long)entry.Scene.Value.Field.Length * sizeof(float));
    private readonly record struct Key(int Count, double Scale, bool Navigation, bool Full, GlassMaterial Material);
    private sealed class Entry(Lazy<GlassScene> scene)
    {
        internal Lazy<GlassScene> Scene { get; } = scene;
        internal long LastAccess;
    }
}
