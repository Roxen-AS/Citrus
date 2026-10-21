using Citrus.Conversion.Editing.Pdf;

namespace Citrus.Conversion.Tests;

public sealed class OfflineStagingFilesTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"citrus-output-boundary-"+Guid.NewGuid().ToString("N"));
    public OfflineStagingFilesTests()=>Directory.CreateDirectory(root);
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
    [Fact]
    public void Cleanup_clears_readonly_private_profile_entries()
    {
        string work=Path.Combine(root,"private");string documents=Path.Combine(work,"worker-profile","Documents");Directory.CreateDirectory(documents);
        string file=Path.Combine(documents,"profile.txt");File.WriteAllText(file,"profile");File.SetAttributes(file,FileAttributes.ReadOnly);File.SetAttributes(documents,File.GetAttributes(documents)|FileAttributes.ReadOnly);
        OfflineStagingFiles.DeletePrivateTree(work);Assert.False(Directory.Exists(work));
    }
    [Fact]
    public void Unexpected_nested_worker_output_is_rejected_before_reading()
    {
        string payload=Path.Combine(root,"payload");Directory.CreateDirectory(Path.Combine(payload,"unlisted"));File.WriteAllText(Path.Combine(payload,"converted.txt"),"valid");
        Assert.Throws<InvalidDataException>(()=>OfflineStagingFiles.ValidatePayload(payload,"txt"));
    }
    [Fact]
    public void Worker_output_cannot_change_the_requested_format()
    {
        string payload=Path.Combine(root,"payload");Directory.CreateDirectory(payload);File.WriteAllText(Path.Combine(payload,"converted.html"),"unrequested");
        Assert.Throws<InvalidDataException>(()=>OfflineStagingFiles.ValidatePayload(payload,"txt"));
    }
}
