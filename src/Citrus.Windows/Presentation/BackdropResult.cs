using Citrus.Core.Presentation;

namespace Citrus.Windows.Presentation;

internal readonly record struct BackdropResult(string Mode, string Reason, int WindowsBuild, int? HResult = null)
{
    public bool IsNative => Mode == "ConfiguredNativeAcrylic";
    public static BackdropResult Solid(MaterialDecision decision, string? reason = null, int? hResult = null)
        => new("Solid", reason ?? decision.Reason, decision.WindowsBuild, hResult ?? decision.HResult);
}

// Narrow seam around documented DWM calls; a successful HRESULT is configuration, never optical proof.
internal interface IBackdropApi
{
    int SetAttribute(nint hwnd, int attribute, int value);
    int ExtendFrame(nint hwnd, int margin);
}

internal sealed class DwmBackdropApi : IBackdropApi
{
    public int SetAttribute(nint hwnd, int attribute, int value) => BackdropNativeMethods.DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
    public int ExtendFrame(nint hwnd, int margin)
    {
        var margins = new BackdropNativeMethods.Margins(margin);
        return BackdropNativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }
}

internal static class BackdropConfiguration
{
    internal const int SystemBackdropType = 38, None = 1, TransientWindow = 3;
    internal static BackdropResult Apply(IBackdropApi api, nint hwnd, MaterialDecision decision, bool dark)
    {
        if (!decision.UseNativeAcrylic)
        {
            int reset = Reset(api, hwnd);
            return BackdropResult.Solid(decision, hResult: reset < 0 ? reset : null);
        }
        int hr = api.ExtendFrame(hwnd, -1);
        if (hr >= 0) hr = api.SetAttribute(hwnd, 20, dark ? 1 : 0);
        if (hr >= 0) hr = api.SetAttribute(hwnd, SystemBackdropType, TransientWindow);
        if (hr < 0)
        {
            Reset(api, hwnd);
            return BackdropResult.Solid(decision, "NativeConfigurationFailed", hr);
        }
        return new("ConfiguredNativeAcrylic", "ConfiguredNotOpticallyVerified", decision.WindowsBuild, hr);
    }

    internal static int Reset(IBackdropApi api, nint hwnd)
    {
        int attribute = api.SetAttribute(hwnd, SystemBackdropType, None);
        int frame = api.ExtendFrame(hwnd, 0);
        return attribute < 0 ? attribute : frame;
    }
}
