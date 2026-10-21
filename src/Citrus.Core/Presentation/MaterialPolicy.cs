namespace Citrus.Core.Presentation;

public sealed record MaterialPreferences(bool ReduceTransparency = false, bool ReduceMotion = false);
public readonly record struct MaterialEnvironment(int WindowsBuild, bool CompositionEnabled, bool AdvancedEffectsEnabled,
    bool HighContrast, bool RemoteSession, bool ClientAreaAnimation);
public readonly record struct MaterialDecision(bool RequestNativeAcrylic, bool Animate, string Reason);
public static class MaterialPolicy
{
    public static MaterialDecision Resolve(MaterialPreferences preferences, MaterialEnvironment environment)
    {
        string reason = environment.WindowsBuild < 22621 ? "WindowsBuildUnsupported"
            : environment.HighContrast ? "HighContrast"
            : preferences.ReduceTransparency ? "ReduceTransparency"
            : environment.RemoteSession ? "RemoteSession"
            : !environment.CompositionEnabled ? "CompositionUnavailable"
            : !environment.AdvancedEffectsEnabled ? "AdvancedEffectsDisabled" : "Eligible";
        return new(reason == "Eligible", !preferences.ReduceMotion && environment.ClientAreaAnimation && !environment.HighContrast, reason);
    }
}
