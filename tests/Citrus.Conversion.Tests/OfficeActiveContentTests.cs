using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Citrus.Core;
using Citrus.Conversion.Editing.Pdf;

namespace Citrus.Conversion.Tests;

/// <summary>Only a self-authored harmless macro fixture is ever enabled, and that control still runs in AppContainer.</summary>
public sealed class OfficeActiveContentTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"citrus-active-content-"+Guid.NewGuid().ToString("N"));
    public OfficeActiveContentTests()=>Directory.CreateDirectory(root);
    public void Dispose()=>OfflineStagingFiles.DeletePrivateTree(root);
    [Fact]
    public async Task Document_auto_open_macro_is_executable_in_control_but_never_in_production()
    {
        string source=Fixture(),pack=FeaturePackLocator.Find(),control=Path.Combine(root,"control");Directory.CreateDirectory(control);
        string worker=Path.Combine(control,"enabled-fixture-worker.py");
        // A test-only copy enables our harmless macro to establish that the ODT
        // event actually fires. Production has no switch that can enable it.
        string code=await File.ReadAllTextAsync(Path.Combine(pack,"office-worker.py"));
        code=code.Replace("DisableMacrosExecution\" oor:op=\"fuse\"><value>true","DisableMacrosExecution\" oor:op=\"fuse\"><value>false",StringComparison.Ordinal)
                 .Replace("DisableActiveContent\" oor:op=\"fuse\"><value>true","DisableActiveContent\" oor:op=\"fuse\"><value>false",StringComparison.Ordinal)
                 .Replace("MacroSecurityLevel\" oor:op=\"fuse\"><value>3","MacroSecurityLevel\" oor:op=\"fuse\"><value>0",StringComparison.Ordinal)
                 .Replace("MacroSecurityLevel=3,EnableMacrosExecution=false","MacroSecurityLevel=0,EnableMacrosExecution=true",StringComparison.Ordinal);
        // Writer posts OnLoad to its Office main loop. The positive control
        // must observe that event before it exports, rather than racing the
        // queued macro with the next synchronous save. Production never waits.
        const string beforeSave="    save = method(document, 1, ctypes.c_int,";
        Assert.Contains(beforeSave,code,StringComparison.Ordinal);
        const string observeEvent="""
    import uno, time
    control_context = uno.getComponentContext()
    control_desktop = control_context.ServiceManager.createInstanceWithContext("com.sun.star.frame.Desktop", control_context)
    control_deadline = time.monotonic() + 5
    while time.monotonic() < control_deadline:
        control_document = control_desktop.getCurrentComponent()
        if control_document is not None and "MACRO EXECUTED" in control_document.Text.String:
            break
        time.sleep(.02)

""";
        code=code.Replace(beforeSave,observeEvent+beforeSave,StringComparison.Ordinal);
        await File.WriteAllTextAsync(worker,code,new UTF8Encoding(false));
        string admitted=Path.Combine(control,"fixture.odt");File.Copy(source,admitted);string payload=Path.Combine(control,"payload");Directory.CreateDirectory(payload);
        string request=Path.Combine(control,"request.json");await File.WriteAllTextAsync(request,JsonSerializer.Serialize(new{Input=admitted,Format="pdf",OutputDirectory=payload}));
        var baseline=await OfflineToolRunner.RunAsync(Path.Combine(pack,"libreoffice","program","python.exe"),[worker,request],pack,control,TimeSpan.FromSeconds(45),default);
        Assert.True(baseline.ExitCode==0,baseline.Log);using(var pdf=UglyToad.PdfPig.PdfDocument.Open(Path.Combine(payload,"converted.pdf")))Assert.Contains("MACRO EXECUTED",pdf.GetPage(1).Text);
        var engine=new DocumentConversionEngine();var operation=engine.Operations.Single(o=>o.Id=="document.convert.pdf");
        var result=await engine.ExecuteAsync(new(operation,[source],new(),root),null,default);using var output=UglyToad.PdfPig.PdfDocument.Open(Assert.Single(result.Outputs));
        Assert.Contains("Original harmless content",output.GetPage(1).Text);Assert.DoesNotContain("MACRO EXECUTED",output.GetPage(1).Text);
        using var original=ZipFile.OpenRead(source);using var reader=new StreamReader(original.GetEntry("content.xml")!.Open());Assert.Contains("Original harmless content",await reader.ReadToEndAsync());
    }
    private string Fixture()
    {
        string path=Path.Combine(root,"macro.odt");using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
        Add("mimetype","application/vnd.oasis.opendocument.text");
        Add("META-INF/manifest.xml","""
<?xml version="1.0" encoding="UTF-8"?>
<manifest:manifest xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0" manifest:version="1.3">
<manifest:file-entry manifest:full-path="/" manifest:media-type="application/vnd.oasis.opendocument.text"/>
<manifest:file-entry manifest:full-path="content.xml" manifest:media-type="text/xml"/>
<manifest:file-entry manifest:full-path="Basic/script-lc.xml" manifest:media-type="text/xml"/>
<manifest:file-entry manifest:full-path="Basic/Standard/script-lb.xml" manifest:media-type="text/xml"/>
<manifest:file-entry manifest:full-path="Basic/Standard/Module1.xml" manifest:media-type="text/xml"/>
</manifest:manifest>
""");
        Add("content.xml","""
<?xml version="1.0" encoding="UTF-8"?>
<office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" xmlns:script="urn:oasis:names:tc:opendocument:xmlns:script:1.0" xmlns:xlink="http://www.w3.org/1999/xlink" xmlns:dom="http://www.w3.org/2001/xml-events" xmlns:ooo="http://openoffice.org/2004/office" office:version="1.3">
<office:scripts><office:event-listeners><script:event-listener script:language="ooo:script" script:event-name="dom:load" xlink:href="vnd.sun.star.script:Standard.Module1.OnLoad?language=Basic&amp;location=document" xlink:type="simple"/></office:event-listeners></office:scripts>
<office:body><office:text><text:p>Original harmless content</text:p></office:text></office:body></office:document-content>
""");
        Add("Basic/script-lc.xml","""
<?xml version="1.0" encoding="UTF-8"?>
<library:libraries xmlns:library="http://openoffice.org/2000/library" xmlns:xlink="http://www.w3.org/1999/xlink"><library:library library:name="Standard" library:link="false"/></library:libraries>
""");
        Add("Basic/Standard/script-lb.xml","""
<?xml version="1.0" encoding="UTF-8"?>
<library:library xmlns:library="http://openoffice.org/2000/library" library:name="Standard" library:readonly="false" library:passwordprotected="false"><library:element library:name="Module1"/></library:library>
""");
        Add("Basic/Standard/Module1.xml","""
<?xml version="1.0" encoding="UTF-8"?>
<script:module xmlns:script="http://openoffice.org/2000/script" script:name="Module1" script:language="StarBasic"><![CDATA[Sub OnLoad
ThisComponent.Text.String = "MACRO EXECUTED"
End Sub]]></script:module>
""");return path;
        void Add(string name,string value){using var sink=new StreamWriter(zip.CreateEntry(name,CompressionLevel.NoCompression).Open(),new UTF8Encoding(false));sink.Write(value);}
    }
}
