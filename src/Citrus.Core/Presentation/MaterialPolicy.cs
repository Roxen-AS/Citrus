namespace Citrus.Core.Presentation;

public sealed record MaterialPreferences(bool ReduceTransparency = false, bool ReduceMotion = false, bool SoundEnabled = true);
public readonly record struct MaterialEnvironment(int WindowsBuild, bool CompositionEnabled, bool WinRtAvailable,
    bool AdvancedEffectsEnabled, bool HighContrast, bool RemoteSession, bool ClientAreaAnimation);
public readonly record struct MaterialDecision(bool UseNativeAcrylic, bool MotionEnabled, bool SoundEnabled,
    string Reason, int WindowsBuild, int? HResult = null);
public static class MaterialPolicy
{
    public static MaterialDecision Resolve(MaterialPreferences preferences, MaterialEnvironment environment, int? hResult = null)
    {
        string reason = environment.WindowsBuild < 22621 ? "WindowsBuildUnsupported"
            : environment.HighContrast ? "HighContrast"
            : preferences.ReduceTransparency ? "ReduceTransparency"
            : environment.RemoteSession ? "RemoteSession"
            : !environment.CompositionEnabled ? "CompositionUnavailable"
            : !environment.WinRtAvailable ? "WinRtUnavailable"
            : !environment.AdvancedEffectsEnabled ? "AdvancedEffectsDisabled"
            : hResult < 0 ? "NativeConfigurationFailed" : "Eligible";
        return new(reason == "Eligible", !preferences.ReduceMotion && environment.ClientAreaAnimation,
            preferences.SoundEnabled, reason, environment.WindowsBuild, hResult);
    }
}
