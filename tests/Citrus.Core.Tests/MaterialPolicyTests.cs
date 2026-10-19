using Citrus.Core.Presentation;

namespace Citrus.Core.Tests;

public class MaterialPolicyTests
{
    private static readonly MaterialEnvironment Eligible = new(22621, true, true, true, false, false, true);

    [Fact]
    public void Win10_pre22621_remote_noeffects_highcontrast_and_user_reduce_use_solid()
    {
        Assert.True(MaterialPolicy.Resolve(new(), Eligible).UseNativeAcrylic);
        foreach (var environment in new[] {
            Eligible with { WindowsBuild = 19045 }, Eligible with { WindowsBuild = 22000 },
            Eligible with { RemoteSession = true }, Eligible with { AdvancedEffectsEnabled = false },
            Eligible with { HighContrast = true }, Eligible with { CompositionEnabled = false } })
            Assert.False(MaterialPolicy.Resolve(new(), environment).UseNativeAcrylic);
        Assert.False(MaterialPolicy.Resolve(new(ReduceTransparency: true), Eligible).UseNativeAcrylic);
    }

    [Fact]
    public void Motion_is_independent_of_transparency_and_sound()
    {
        for (int mask = 0; mask < 8; mask++)
        {
            var preferences = new MaterialPreferences((mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0);
            var result = MaterialPolicy.Resolve(preferences, Eligible);
            Assert.Equal(!preferences.ReduceTransparency, result.UseNativeAcrylic);
            Assert.Equal(!preferences.ReduceMotion, result.MotionEnabled);
            Assert.Equal(preferences.SoundEnabled, result.SoundEnabled);
        }
        Assert.False(MaterialPolicy.Resolve(new(), Eligible with { ClientAreaAnimation = false }).MotionEnabled);
        Assert.True(MaterialPolicy.Resolve(new(), Eligible with { ClientAreaAnimation = false }).UseNativeAcrylic);
    }

    [Fact]
    public void Unavailable_winrt_or_failed_dwm_call_falls_back()
    {
        Assert.False(MaterialPolicy.Resolve(new(), Eligible with { WinRtAvailable = false }).UseNativeAcrylic);
        Assert.False(MaterialPolicy.Resolve(new(), Eligible, unchecked((int)0x80070057)).UseNativeAcrylic);
        Assert.True(MaterialPolicy.Resolve(new(), Eligible, 0).UseNativeAcrylic);
        Assert.Equal(unchecked((int)0x80070057), MaterialPolicy.Resolve(new(), Eligible, unchecked((int)0x80070057)).HResult);
    }
}
