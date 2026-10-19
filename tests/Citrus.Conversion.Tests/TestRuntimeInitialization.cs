using System.Runtime.CompilerServices;

namespace Citrus.Conversion.Tests;

internal static class TestRuntimeInitialization
{
    // Fixtures can use Magick before any Conversion method. Run the production module
    // initializer first so native policy caching matches application startup. The CLR
    // runs that module constructor once, including when Conversion is used later.
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Initialize()
#pragma warning restore CA2255
        => RuntimeHelpers.RunModuleConstructor(typeof(ImageEngine).Module.ModuleHandle);
}
