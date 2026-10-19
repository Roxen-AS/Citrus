using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics;
using SkiaSharp;

namespace Citrus.Windows.Editors.Image;

public sealed record InstalledFontChoice(string Family,string Style,string Path)
{
    public string Label=>Family+" · "+Style;
}

/// <summary>One bounded, cancellable catalog per editor. Selecting a row still captures exact file bytes separately.</summary>
public sealed class InstalledFontCatalog : IDisposable
{
    private readonly object sync=new();
    private readonly CancellationTokenSource lifetime;
    private readonly string[] directories;
    private Task<ImmutableArray<InstalledFontChoice>>? work;
    private bool disposed;
    public InstalledFontCatalog(CancellationToken ct,IEnumerable<string>? directories=null)
    {
        lifetime=CancellationTokenSource.CreateLinkedTokenSource(ct);
        this.directories=(directories??new[]{Environment.GetFolderPath(Environment.SpecialFolder.Fonts),System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Microsoft","Windows","Fonts")}).ToArray();
    }
    public Task<ImmutableArray<InstalledFontChoice>> GetAsync(CancellationToken ct)
    {
        lock(sync){if(disposed)return Task.FromCanceled<ImmutableArray<InstalledFontChoice>>(new CancellationToken(true));return (work??=Task.Run(Read,lifetime.Token)).WaitAsync(ct);}
    }
    private ImmutableArray<InstalledFontChoice> Read()
    {
        var choices=new List<InstalledFontChoice>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var clock=Stopwatch.StartNew();int count=0;
        foreach(string directory in directories.Where(Directory.Exists))
        {
            string[] files;
            try{files=Directory.EnumerateFiles(directory).Take(512).Where(p=>System.IO.Path.GetExtension(p).Equals(".ttf",StringComparison.OrdinalIgnoreCase)||System.IO.Path.GetExtension(p).Equals(".otf",StringComparison.OrdinalIgnoreCase)).ToArray();}
            catch(Exception e)when(e is IOException or UnauthorizedAccessException){continue;}
            foreach(string path in files)
            {
                lifetime.Token.ThrowIfCancellationRequested();if(++count>512||clock.Elapsed>TimeSpan.FromSeconds(8))return Finish();
                if(!seen.Add(path))continue;
                try
                {
                    if(!StaticMonochromeFile(path))continue;
                    using var face=SKTypeface.FromFile(path);if(face is null||string.IsNullOrWhiteSpace(face.FamilyName))continue;
                    choices.Add(new(face.FamilyName,StyleName(face.FontStyle.Weight,(int)face.FontStyle.Slant,face.FontStyle.Width),path));
                }
                catch(Exception e)when(e is not (OutOfMemoryException or OperationCanceledException)){ }
            }
        }
        return Finish();
        ImmutableArray<InstalledFontChoice> Finish()=>[..choices.OrderBy(c=>c.Family,StringComparer.CurrentCultureIgnoreCase).ThenBy(c=>c.Style,StringComparer.CurrentCultureIgnoreCase).DistinctBy(c=>(c.Family,c.Style))];
    }
    public static string StyleName(int weight,int slant,int width=5)
    {
        string bold=weight switch{<=150=>"Thin",<=250=>"Extra light",<=350=>"Light",<=450=>"Regular",<=550=>"Medium",<=650=>"Semibold",<=750=>"Bold",<=850=>"Extra bold",_=>"Black"};
        string italic=slant==0?"":slant==1?" Italic":" Oblique";string stretch=width<5?" Condensed":width>5?" Expanded":"";
        return bold+italic+stretch;
    }
    private static bool StaticMonochromeFile(string path)
    {
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(input.Length is <12 or >64*1024*1024)return false;
        Span<byte> header=stackalloc byte[12];input.ReadExactly(header);uint signature=BinaryPrimitives.ReadUInt32BigEndian(header);
        if(signature is not (0x00010000 or 0x4f54544f or 0x74727565))return false; // Collections remain an explicit Advanced selection.
        int count=BinaryPrimitives.ReadUInt16BigEndian(header[4..]);if(count>4096||12L+count*16L>input.Length)return false;
        Span<byte> entry=stackalloc byte[16];
        for(int i=0;i<count;i++)
        {
            input.ReadExactly(entry);string tag=System.Text.Encoding.ASCII.GetString(entry[..4]);
            if(tag is "fvar" or "COLR" or "CPAL" or "CBDT" or "CBLC" or "sbix" or "SVG ")return false;
            if((ulong)BinaryPrimitives.ReadUInt32BigEndian(entry[8..12])+BinaryPrimitives.ReadUInt32BigEndian(entry[12..16])>(ulong)input.Length)return false;
        }
        return true;
    }
    public void Dispose()
    {
        Task? pending;lock(sync){if(disposed)return;disposed=true;pending=work;}
        lifetime.Cancel();
        if(pending is null||pending.IsCompleted)lifetime.Dispose();
        else _=pending.ContinueWith(_=>lifetime.Dispose(),CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
    }
}
