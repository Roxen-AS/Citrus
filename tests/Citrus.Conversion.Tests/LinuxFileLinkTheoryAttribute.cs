namespace Citrus.Conversion.Tests;

// A host source-reparse surrogate, not Windows Cloud Files runtime coverage.
public sealed class LinuxFileLinkTheoryAttribute : TheoryAttribute
{
    public LinuxFileLinkTheoryAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "Linux source-link surrogate; real hydrated Cloud Files acceptance is separately pending on Windows.";
    }
}
