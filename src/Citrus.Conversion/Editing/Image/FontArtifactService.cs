using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using Citrus.Core.Editing;
using SkiaSharp;

namespace Citrus.Conversion.Editing.Image;

/// <summary>Exact file bytes and collection index own each face; no font-manager lookup or fallback.</summary>
public sealed class FontArtifactService : IFontArtifactService
{
    private const int MaximumFontBytes = 64 * 1024 * 1024;

    public Task<FontArtifact> CaptureAsync(string fontFile, int faceIndex, CancellationToken ct) => Task.Run(() =>
    {
        string path = Path.GetFullPath(fontFile); byte[] bytes = ReadBytes(path,ct);
        using var face = OpenFace(bytes,faceIndex);
        ct.ThrowIfCancellationRequested();
        return new FontArtifact(Guid.NewGuid(),path,bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)),faceIndex,face.FamilyName,$"{face.FontStyle.Weight}/{face.FontStyle.Width}/{face.FontStyle.Slant}");
    },ct);

    public async Task<ImmutableDictionary<Guid,string>> StageVerifiedAsync(ImmutableArray<FontArtifact> fonts, string privateDirectory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (fonts.IsDefault || fonts.Any(f => f.Id == Guid.Empty || f.Length is <= 0 or > MaximumFontBytes || f.Sha256.Length != 64 || !f.Sha256.All(Uri.IsHexDigit) || f.FaceIndex < 0) || fonts.Select(f=>f.Id).Distinct().Count()!=fonts.Length) throw new ArgumentException("Invalid font artifacts.");
        if (fonts.IsEmpty) return ImmutableDictionary<Guid,string>.Empty;
        string root=Path.GetFullPath(privateDirectory); SafeEditorFiles.RejectLinks(root);
        if (fonts.Any(f => Path.GetFullPath(f.OriginalPath).StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Font staging cannot contain an original font.");
        string owned=Path.Combine(root,"fonts"); bool createdRoot=!Directory.Exists(root), ownsFonts=false;
        if (Directory.Exists(owned)||File.Exists(owned)) throw new IOException("Font staging already exists; use a fresh private directory.");
        try
        {
            Directory.CreateDirectory(owned); ownsFonts=true;
            var result=ImmutableDictionary.CreateBuilder<Guid,string>();
            foreach (var font in fonts)
            {
                ct.ThrowIfCancellationRequested(); byte[] bytes=ReadVerified(font,font.OriginalPath,ct);
                using var face=OpenFace(bytes,font.FaceIndex);
                string path=Path.Combine(owned,font.Id.ToString("N")+".font");
                await using (var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous))
                    await output.WriteAsync(bytes,ct).ConfigureAwait(false);
                result.Add(font.Id,path);
            }
            ct.ThrowIfCancellationRequested(); return result.ToImmutable();
        }
        catch
        {
            if (ownsFonts&&Directory.Exists(owned)) Directory.Delete(owned,true);
            if (createdRoot&&Directory.Exists(root)&&!Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
            throw;
        }
    }

    internal static SKTypeface OpenVerified(FontArtifact font,string path,CancellationToken ct) => OpenFace(ReadVerified(font,path,ct),font.FaceIndex);
    private static byte[] ReadVerified(FontArtifact font,string path,CancellationToken ct)
    {
        byte[] bytes=ReadBytes(path,ct);
        if (bytes.LongLength!=font.Length||!Convert.ToHexString(SHA256.HashData(bytes)).Equals(font.Sha256,StringComparison.OrdinalIgnoreCase)) throw new IOException("Font content changed. Resolve the font again.");
        return bytes;
    }
    private static byte[] ReadBytes(string path,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if (stream.Length is <= 0 or > MaximumFontBytes) throw new NotSupportedException("Font files must be 1 byte to 64 MiB.");
        byte[] bytes=new byte[(int)stream.Length]; stream.ReadExactly(bytes);
        if (stream.ReadByte()!=-1) throw new IOException("Font changed while reading. Resolve it again.");
        ct.ThrowIfCancellationRequested(); return bytes;
    }
    internal static SKTypeface OpenFace(byte[] bytes,int faceIndex)
    {
        // Skia may accept out-of-range collection indices on some backends. Validate SFNT
        // ourselves before native decoding, so the requested face can never silently change.
        if (bytes.Length<12||faceIndex<0) throw new InvalidDataException("Invalid static TrueType/OpenType font face.");
        int offset=0;
        if (bytes.AsSpan(0,4).SequenceEqual("ttcf"u8))
        {
            uint count=BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8,4));
            if (faceIndex>=count||12L+count*4L>bytes.Length) throw new ArgumentException("Font collection FaceIndex is out of range.");
            uint tableOffset=BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12+faceIndex*4,4));
            if (tableOffset>bytes.Length-12) throw new InvalidDataException("Invalid font collection offset.");
            offset=(int)tableOffset;
        }
        else if (faceIndex!=0) throw new ArgumentException("A single-face font requires FaceIndex zero.");
        uint signature=BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset,4));
        if (signature is not (0x00010000 or 0x4F54544F or 0x74727565)) throw new NotSupportedException("Select a static TrueType/OpenType font file.");
        int tables=BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset+4,2));
        if (offset+12L+tables*16L>bytes.Length) throw new InvalidDataException("Invalid font table directory.");
        for(int i=0;i<tables;i++)
        {
            var entry=bytes.AsSpan(offset+12+i*16,16); string tag=System.Text.Encoding.ASCII.GetString(entry[..4]);
            if (tag=="fvar") throw new NotSupportedException("Variable fonts are unsupported; choose a static face.");
            if (tag is "COLR" or "CPAL" or "CBDT" or "CBLC" or "sbix" or "SVG ") throw new NotSupportedException("Color/emoji fonts are unsupported; choose a static monochrome face.");
            uint start=BinaryPrimitives.ReadUInt32BigEndian(entry[8..12]), length=BinaryPrimitives.ReadUInt32BigEndian(entry[12..16]);
            if ((ulong)start+length>(ulong)bytes.Length) throw new InvalidDataException("Font table exceeds file bounds.");
        }
        using var data=SKData.CreateCopy(bytes);
        return SKTypeface.FromData(data,faceIndex) ?? throw new InvalidDataException("The selected font face could not be decoded.");
    }
}
