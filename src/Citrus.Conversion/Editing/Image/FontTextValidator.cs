using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Citrus.Core.Editing;
using SkiaSharp;

namespace Citrus.Conversion.Editing.Image;

public static class FontTextValidator
{
    public static ImmutableArray<ValidationIssue> Validate(FontArtifact font,string text)
    {
        try { using var face=FontArtifactService.OpenVerified(font,font.OriginalPath,default); return Validate(face,text); }
        catch(Exception error) when(error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return [new("Font",error.Message)]; }
    }
    internal static ImmutableArray<ValidationIssue> Validate(SKTypeface face,string text)
    {
        if (string.IsNullOrEmpty(text)||text.Length>100000) return [new("Text","Text must contain 1..100000 UTF-16 code units.")];
        using var font=new SKFont(face,12);
        var span=text.AsSpan();
        while (!span.IsEmpty)
        {
            if(Rune.DecodeFromUtf16(span,out var rune,out int consumed)!=OperationStatus.Done) return [new("Text","Invalid Unicode scalar in text.")];
            span=span[consumed..]; int v=rune.Value;
            if (v is 10 or 13) continue;
            var category=Rune.GetUnicodeCategory(rune);
            bool basic=v is >=0x20 and <=0x7e or >=0xa0 and <=0x2ff or >=0x370 and <=0x52f or >=0x3040 and <=0x30ff or >=0x3400 and <=0x9fff or >=0xac00 and <=0xd7a3 or >=0xff01 and <=0xff60;
            if(!basic||category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format or UnicodeCategory.Control)
                return [new("Text",$"U+{v:X4} requires unsupported combining, complex-script, RTL or emoji shaping. Use basic left-to-right text in a static face.")];
            if(font.GetGlyph(v)==0) return [new("Text",$"Selected font has no glyph for U+{v:X4}. Choose another explicit face; font fallback is disabled.")];
        }
        return [];
    }
    internal static void ValidateDocument(EditorDocument document,ImmutableDictionary<Guid,string> paths,CancellationToken ct)
    {
        var recipe=(ImageRecipe)document.Recipe;
        foreach (var layer in recipe.Annotations)
        {
            ct.ThrowIfCancellationRequested();
            string? text=layer.Geometry switch { TextGeometry t=>t.Text,NumberGeometry n=>n.Number.ToString(CultureInfo.InvariantCulture),_=>null };
            if(text is null) continue;
            var spec=layer.Style.Font ?? throw new ArgumentException("Text requires an explicit font.");
            var artifact=document.Fonts.Single(f=>f.Id==spec.ArtifactId);
            using var face=FontArtifactService.OpenVerified(artifact,paths[artifact.Id],ct);
            var issues=Validate(face,text);
            if(!issues.IsEmpty) throw new NotSupportedException(string.Join(" ",issues.Select(i=>i.Message)));
        }
    }
}
