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
