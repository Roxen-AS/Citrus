using Citrus.Core.Presentation;

namespace Citrus.Core.Tests;

public class MaterialPolicyTests
{
    private static readonly MaterialEnvironment Eligible = new(22621, true, true, false, false, true);

    [Fact]
    public void Permanent_material_contract_keeps_sound_and_per_window_hresult_out_of_policy()
    {
        Assert.Contains(typeof(MaterialPreferences).GetConstructors(), constructor => constructor.GetParameters().Length == 2);
        Assert.Contains(typeof(MaterialEnvironment).GetConstructors(), constructor => constructor.GetParameters().Length == 6);
        Assert.Equal(new[] { "Animate", "Reason", "RequestNativeAcrylic" }, typeof(MaterialDecision).GetProperties().Select(property => property.Name).Order().ToArray());
        Assert.Single(typeof(MaterialPolicy).GetMethod("Resolve")!.GetParameters(), parameter => parameter.ParameterType == typeof(MaterialEnvironment));
        Assert.Equal(2, typeof(MaterialPolicy).GetMethod("Resolve")!.GetParameters().Length);
    }

    [Fact]
    public void High_contrast_synchronously_disables_effective_animation()
    {
        Assert.False(MaterialPolicy.Resolve(new(), Eligible with { HighContrast = true }).Animate);
    }

    [Fact]
    public void Win10_pre22621_remote_noeffects_highcontrast_and_user_reduce_use_solid()
    {
        Assert.True(MaterialPolicy.Resolve(new(), Eligible).RequestNativeAcrylic);
        foreach (var environment in new[] {
            Eligible with { WindowsBuild = 19045 }, Eligible with { WindowsBuild = 22000 },
            Eligible with { RemoteSession = true }, Eligible with { AdvancedEffectsEnabled = false },
            Eligible with { HighContrast = true }, Eligible with { CompositionEnabled = false } })
            Assert.False(MaterialPolicy.Resolve(new(), environment).RequestNativeAcrylic);
        Assert.False(MaterialPolicy.Resolve(new(ReduceTransparency: true), Eligible).RequestNativeAcrylic);
    }

    [Fact]
    public void Motion_is_independent_of_transparency_and_sound()
    {
        for (int mask = 0; mask < 4; mask++)
        {
            var preferences = new MaterialPreferences((mask & 1) != 0, (mask & 2) != 0);
            var result = MaterialPolicy.Resolve(preferences, Eligible);
            Assert.Equal(!preferences.ReduceTransparency, result.RequestNativeAcrylic);
            Assert.Equal(!preferences.ReduceMotion, result.Animate);
            Assert.Null(typeof(MaterialPreferences).GetProperty("SoundEnabled"));
        }
        Assert.False(MaterialPolicy.Resolve(new(), Eligible with { ClientAreaAnimation = false }).Animate);
        Assert.True(MaterialPolicy.Resolve(new(), Eligible with { ClientAreaAnimation = false }).RequestNativeAcrylic);
    }

    [Fact]
    public void Unavailable_advanced_effects_conservatively_uses_solid_without_per_window_hresult()
    {
        Assert.False(MaterialPolicy.Resolve(new(), Eligible with { AdvancedEffectsEnabled = false }).RequestNativeAcrylic);
        Assert.Null(typeof(MaterialDecision).GetProperty("HResult"));
    }
}
