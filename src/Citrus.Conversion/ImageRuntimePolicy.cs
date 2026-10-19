using ImageMagick;
using ImageMagick.Configuration;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Citrus.Conversion;

/// <summary>Own the native policy before any conversion can populate ImageMagick's policy cache.</summary>
internal static class ImageRuntimePolicy
{
    // Magick.NET 15 exposes configuration initialization, not an in-memory policy setter.
    // Initialize at assembly startup: the first loader call can be too late if another
    // conversion component has already used the native library.
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Initialize()
#pragma warning restore CA2255
    {
        var configuration = ConfigurationFiles.Default;
        var policy = XDocument.Parse(configuration.Policy.Data);
        policy.Root!.Add(
            new XElement("policy", new XAttribute("domain", "delegate"), new XAttribute("rights", "none"), new XAttribute("pattern", "*")),
            new XElement("policy", new XAttribute("domain", "coder"), new XAttribute("rights", "none"), new XAttribute("pattern", "*")),
            new XElement("policy", new XAttribute("domain", "coder"), new XAttribute("rights", "read|write"), new XAttribute("pattern", "{PNG,JPEG,WEBP,TIFF,BMP,GIF,AVIF}")),
            // MSVG compiles validated SVG to MVG; XC creates an in-memory solid-color canvas.
            // These internal readers are never public input selectors or output formats.
            new XElement("policy", new XAttribute("domain", "coder"), new XAttribute("rights", "read"), new XAttribute("pattern", "{MSVG,MVG,XC}")));
        // The native policy tokenizer does not reliably consume the DTD/default-namespace
        // representation emitted by XDocument. Retain its rules as plain policy elements.
        policy.DocumentType?.Remove();
        policy.Descendants().Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Remove();
        configuration.Policy.Data = policy.ToString();
        var directory = Directory.CreateTempSubdirectory("citrus-imagemagick-");
        try
        {
            MagickNET.Initialize(configuration, directory.FullName);
        }
        catch
        {
            DeleteConfiguration(directory);
            throw;
        }
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteConfiguration(directory);
    }

    private static void DeleteConfiguration(DirectoryInfo directory)
    {
        try { directory.Delete(recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
