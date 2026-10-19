using System.Text.Json;
namespace Citrus.Windows.Tests;
public sealed class GlassSettingsTests
{
    [Fact]
    public void Old_settings_preserve_sound_true_and_add_reduce_transparency_false()
    {
        var settings = JsonSerializer.Deserialize<CitrusSettings>("{\"ReduceMotion\":true}")!;
        Assert.True(settings.SoundEnabled);
        Assert.True(settings.ReduceMotion);
        Assert.False(settings.ReduceTransparency);
        Assert.True(JsonSerializer.Deserialize<CitrusSettings>(JsonSerializer.Serialize(settings with { ReduceTransparency = true }))!.ReduceTransparency);
    }
}
