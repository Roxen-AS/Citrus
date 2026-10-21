using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Citrus.Conversion.Tests;

public sealed class OfflineWorkerIsolationTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"citrus-isolation-"+Guid.NewGuid().ToString("N"));
    public OfflineWorkerIsolationTests()=>Directory.CreateDirectory(root);
    public void Dispose()=>Directory.Delete(root,true);
    [Fact]
    public async Task Actual_AppContainer_denies_unadmitted_files_and_loopback_network()
    {
        string pack=FeaturePackLocator.Find();string work=Path.Combine(root,"private");Directory.CreateDirectory(work);
        string secret=Path.Combine(root,"unadmitted.txt");await File.WriteAllTextAsync(secret,"test-only unadmitted content");
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;
        using(var baseline=new TcpClient()){await baseline.ConnectAsync(IPAddress.Loopback,port);using var accepted=await listener.AcceptTcpClientAsync();Assert.True(accepted.Connected);}
        string script=Path.Combine(work,"probe.py");
        await File.WriteAllTextAsync(script,"""
import json, os, pathlib, socket, sys
result = {"FileDenied": False, "NetworkDenied": False, "HostSecretInherited": "CITRUS_ISOLATION_TEST_SECRET" in os.environ}
try:
    pathlib.Path(sys.argv[1]).read_text()
except PermissionError:
    result["FileDenied"] = True
try:
    socket.create_connection(("127.0.0.1", int(sys.argv[2])), timeout=2).close()
except (PermissionError, TimeoutError):
    result["NetworkDenied"] = True
pathlib.Path("probe-result.json").write_text(json.dumps(result))
""");
        string? previous=Environment.GetEnvironmentVariable("CITRUS_ISOLATION_TEST_SECRET");Environment.SetEnvironmentVariable("CITRUS_ISOLATION_TEST_SECRET","test-only-marker");
        OfflineToolResult result;
        try { result=await OfflineToolRunner.RunAsync(Path.Combine(pack,"libreoffice","program","python.exe"),[script,secret,port.ToString(System.Globalization.CultureInfo.InvariantCulture)],pack,work,TimeSpan.FromSeconds(20),default); }
        finally { Environment.SetEnvironmentVariable("CITRUS_ISOLATION_TEST_SECRET",previous); }
        Assert.True(result.ExitCode==0,result.Log);
        using var report=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(work,"probe-result.json")));
        Assert.True(report.RootElement.GetProperty("FileDenied").GetBoolean());Assert.True(report.RootElement.GetProperty("NetworkDenied").GetBoolean());
        Assert.False(report.RootElement.GetProperty("HostSecretInherited").GetBoolean());
        Assert.False(listener.Pending());Assert.Equal("test-only unadmitted content",await File.ReadAllTextAsync(secret));
    }
}
