using ImageMagick;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Citrus.Conversion;

/// <summary>Explicit decoders shared by raster operations and image-to-PDF. Never select a document delegate.</summary>
internal static class ValidatedImageLoader
{
    internal static readonly IReadOnlyDictionary<string, MagickFormat> Formats = new Dictionary<string, MagickFormat>(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = MagickFormat.Png, [".jpg"] = MagickFormat.Jpeg, [".jpeg"] = MagickFormat.Jpeg,
        [".webp"] = MagickFormat.WebP, [".tif"] = MagickFormat.Tiff, [".tiff"] = MagickFormat.Tiff,
        [".bmp"] = MagickFormat.Bmp, [".gif"] = MagickFormat.Gif, [".avif"] = MagickFormat.Avif, [".svg"] = MagickFormat.Msvg, [".heic"]=MagickFormat.Heic,[".heif"]=MagickFormat.Heic
    };

    internal static MagickImage Load(string source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string extension = Path.GetExtension(source);
        if(extension.Equals(".heic",StringComparison.OrdinalIgnoreCase)||extension.Equals(".heif",StringComparison.OrdinalIgnoreCase))return Editing.Image.HeicCodec.Load(source,token);
        if (!Formats.TryGetValue(extension, out var format)) throw new NotSupportedException("Choose a supported single-frame image.");
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase)) ValidateSvg(input);
        using (var frames = new MagickImageCollection())
        {
            frames.Ping(input, new MagickReadSettings { Format = format });
            if (frames.Count != 1) throw new NotSupportedException("Choose a single-frame image. Animated images and multipage TIFFs are not supported yet; no copy was created.");
        }
        token.ThrowIfCancellationRequested();
        input.Position = 0;
        var image = new MagickImage(input, new MagickReadSettings { Format = format });
        try
        {
            token.ThrowIfCancellationRequested();
            image.AutoOrient();
            return image;
        }
        catch { image.Dispose(); throw; }
    }

    private static void ValidateSvg(Stream input)
    {
        const string message = "Choose a self-contained SVG with shapes or text. External resources, CSS stylesheets, scripts and animation are not supported.";
        try
        {
            using var xml = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, CloseInput = false, MaxCharactersInDocument = 16 * 1024 * 1024 });
            var document = XDocument.Load(xml);
            if (document.Root?.Name != XName.Get("svg", "http://www.w3.org/2000/svg")) throw new NotSupportedException(message);
            if (document.DescendantNodes().OfType<XProcessingInstruction>().Any()) throw new NotSupportedException(message);
            foreach (var element in document.Descendants())
            {
                // MSVG compares SVG keywords without case sensitivity, unlike XML names. Match its interpretation.
                if (element.Name.LocalName.ToLowerInvariant() is "script" or "foreignobject" or "style" or "animate" or "animatetransform" or "animatemotion" or "set")
                    throw new NotSupportedException(message);
                foreach (var attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
                {
                    string value = attribute.Value.Trim();
                    if (attribute.Name.LocalName.Equals("href", StringComparison.OrdinalIgnoreCase) && !value.StartsWith('#')) throw new NotSupportedException(message);
                    if (attribute.Name.Namespace == XNamespace.Xml && attribute.Name.LocalName.Equals("base", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException(message);
                    // Keep local paint-server references (gradients/clips), disallow external CSS url/import tricks.
                    string withoutFragments = Regex.Replace(value, "url\\(\\s*['\"]?#[A-Za-z_][A-Za-z0-9_.:-]*['\"]?\\s*\\)", "", RegexOptions.IgnoreCase);
                    if (withoutFragments.Contains("url", StringComparison.OrdinalIgnoreCase) || value.Contains('\\') || value.Contains('@'))
                        throw new NotSupportedException(message);
                }
            }
        }
        catch (XmlException failure) { throw new NotSupportedException(message, failure); }
        finally { input.Position = 0; }
    }

}
