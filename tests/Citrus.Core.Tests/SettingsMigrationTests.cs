using System.Text.Json;
using Citrus.Windows;
namespace Citrus.Core.Tests;
public class SettingsMigrationTests
{
    [Fact]
    public void Legacy_preferences_keep_remaps_and_receive_independent_sound_motion_defaults()
    {
        var settings = JsonSerializer.Deserialize<CitrusSettings>("""{"Theme":2,"GesturesEnabled":false,"ShiftMode":2,"AltMode":0,"ControlMode":1}""")!;
        Assert.True(settings.SoundEnabled);
        Assert.False(settings.ReduceMotion);
        Assert.False(settings.ReduceTransparency);
        Assert.Equal(Citrus.Core.OperationMode.Actions, settings.ShiftMode);
        Assert.False(settings.GesturesEnabled);
        settings.Validate();
    }
    [Theory]
    [InlineData(0)]
    [InlineData(400)]
    [InlineData(1500)]
    [InlineData(999999)]
    public void Obsolete_delay_is_ignored_and_not_saved_while_other_preferences_survive(int delay)
    {
        var source = JsonSerializer.Deserialize<CitrusSettings>($"{{\"HeldModifierDelayMs\":{delay},\"ShiftMode\":2,\"AltMode\":1,\"ControlMode\":0,\"SoundEnabled\":false,\"Theme\":2}}")!;
        source.Validate();
        string saved = JsonSerializer.Serialize(source);
        Assert.DoesNotContain("HeldModifierDelayMs", saved);
        var restored = JsonSerializer.Deserialize<CitrusSettings>(saved)!;
        restored.Validate();
        Assert.Equal(Citrus.Core.OperationMode.Actions, restored.ShiftMode);
        Assert.Equal(Citrus.Core.OperationMode.Convert, restored.ControlMode);
        Assert.Equal(ThemeChoice.Dark, restored.Theme);
        Assert.False(restored.SoundEnabled);
    }
    [Fact]
    public void Explicit_mute_survives_save_independently_of_motion()
    {
        var settings = JsonSerializer.Deserialize<CitrusSettings>("""{"SoundEnabled":false,"ReduceMotion":true}""")!;
        var roundTrip = JsonSerializer.Deserialize<CitrusSettings>(JsonSerializer.Serialize(settings))!;
        Assert.False(roundTrip.SoundEnabled);
        Assert.True(roundTrip.ReduceMotion);
        Assert.False(roundTrip.ReduceTransparency);
    }
    [Fact]
    public void Reduced_transparency_roundtrips_without_changing_sound_or_motion()
    {
        var settings = new CitrusSettings { ReduceTransparency = true };
        var restored = JsonSerializer.Deserialize<CitrusSettings>(JsonSerializer.Serialize(settings))!;
        Assert.True(restored.ReduceTransparency);
        Assert.True(restored.SoundEnabled);
        Assert.False(restored.ReduceMotion);
    }
}
