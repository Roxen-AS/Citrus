using System.Text.Json;
using Citrus.Core;

namespace Citrus.Windows.Tests;

public sealed class GlassRefusalIdentityTests
{
    private static readonly ScreenRect Crop = new(100, 100, 368, 414);
    private static readonly ScreenRect Work = new(0, 0, 1200, 800);
    private static GlassCaptureSnapshot Refused(params GlassCaptureWindow[] extras) => new(Crop, Work, 1, 2, 0,
        [new(2, Crop, true, true), .. extras, new(1, Work, true, true), new(500, Work, true, true)], true,
        new(true, true, "WinSta0", "Default", "Default", true, 1, 1, 0, 42, 1),
        new(368 * 414, 368 * 414, true));
    private static GlassWindowIdentity Identity(long handle) => new(handle, handle, "diagnostic-class", 123,
        "diagnostic-process", 456, 1, true, Work, 0, 0, "Default",
        new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Refused_snapshot_preserves_window_and_process_creation_identities_without_qualifying_it()
    {
        var original = Refused(new GlassCaptureWindow(99, Crop, true, true));
        var before = GlassCaptureOwnership.Check(original);
        var enriched = GlassNativeDesktopProbe.AddRefusalIdentities(original, handle => Identity(handle.ToInt64()));
        Assert.Equal(before, GlassCaptureOwnership.Check(enriched));
        Assert.Equal("UnrelatedVisualOccluder", before.Reason);
        Assert.Equal(original.Windows.Select(window => window.Handle), enriched.Windows.Select(window => window.Handle));
        Assert.Null(original.Windows[1].Identity);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(enriched));
        var identity = json.RootElement.GetProperty("Windows")[1].GetProperty("Identity");
        Assert.Equal("diagnostic-class", identity.GetProperty("ClassName").GetString());
        Assert.Equal(123u, identity.GetProperty("ProcessId").GetUInt32());
        Assert.Equal(456u, identity.GetProperty("ThreadId").GetUInt32());
        Assert.Equal(new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc), identity.GetProperty("ProcessStartedUtc").GetDateTime());
        Assert.Equal(32, json.RootElement.GetProperty("RefusalIdentityLimit").GetInt32());
        Assert.False(json.RootElement.GetProperty("RefusalIdentitiesTruncated").GetBoolean());
    }

    [Fact]
    public void Identity_reads_are_bounded_and_skip_hidden_nonintersecting_and_below_background_windows()
    {
        var original = Refused([new(98, Crop, false, true), new(97, new(900, 700, 2, 2), true, true),
            .. Enumerable.Range(100, 40).Select(handle => new GlassCaptureWindow(handle, Crop, true, true))]);
        var read = new List<long>();
        var enriched = GlassNativeDesktopProbe.AddRefusalIdentities(original, handle =>
        {
            read.Add(handle.ToInt64());
            return Identity(handle.ToInt64());
        });
        Assert.Equal(32, read.Count);
        Assert.DoesNotContain(98, read);
        Assert.DoesNotContain(97, read);
        Assert.DoesNotContain(1, read);
        Assert.DoesNotContain(500, read);
        Assert.True(enriched.RefusalIdentitiesTruncated);
        Assert.Equal(original.Windows.Count, enriched.Windows.Count);
        Assert.Equal(GlassCaptureOwnership.Check(original), GlassCaptureOwnership.Check(enriched));
    }

    [Fact]
    public void Unavailable_identity_stays_nullable_and_cannot_replace_the_original_refusal()
    {
        var original = Refused(new GlassCaptureWindow(99, Crop, true, true));
        var enriched = GlassNativeDesktopProbe.AddRefusalIdentities(original, _ => throw new InvalidOperationException("identity unavailable"));
        Assert.Null(enriched.Windows[1].Identity);
        Assert.Equal(nameof(InvalidOperationException), enriched.Windows[1].IdentityReadFailure);
        Assert.Equal(GlassCaptureOwnership.Check(original), GlassCaptureOwnership.Check(enriched));
    }
}
