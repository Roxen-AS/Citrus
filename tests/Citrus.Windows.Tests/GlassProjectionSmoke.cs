using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.UI.ViewManagement;

namespace Citrus.Windows.Tests;

internal static class GlassProjectionSmoke
{
    internal static int Run()
    {
        var settings = new UISettings();
        bool effects = settings.AdvancedEffectsEnabled;
        double scale = settings.TextScaleFactor;
        var sdk = typeof(UISettings).Assembly;
        var runtime = typeof(WinRT.ComWrappersSupport).Assembly;
        bool local = new[] { sdk, runtime }.All(assembly => assembly.Location.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            RuntimeInformation.OSArchitecture, RuntimeInformation.ProcessArchitecture,
            AdvancedEffectsEnabled = effects, TextScaleFactor = scale,
            Projection = sdk.FullName, ProjectionPath = sdk.Location,
            Runtime = runtime.FullName, RuntimePath = runtime.Location,
            AppLocalProjection = local, OsPreferenceWrites = false
        }));
        return local && RuntimeInformation.ProcessArchitecture == Architecture.X64 ? 0 : 1;
    }
}
