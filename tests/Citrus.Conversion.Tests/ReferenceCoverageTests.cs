using System.Text.Json;
using Citrus.Core;

namespace Citrus.Conversion.Tests;

public sealed class ReferenceCoverageTests
{
    [Fact]
    public async Task All_83_reference_entry_points_resolve_to_registered_operations_and_editor_hosts()
    {
        string? root=AppContext.BaseDirectory;
        while(root is not null&&!File.Exists(Path.Combine(root,"docs","reference-wheel-inventory.json")))root=Path.GetDirectoryName(root);
        Assert.NotNull(root);
        using var inventory=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root,"docs","reference-wheel-inventory.json")));
        var entries=inventory.RootElement.EnumerateArray().ToArray();
        Assert.Equal(83,entries.Length);
        var ids=entries.Select(e=>e.GetProperty("operationId").GetString()!).Distinct().ToArray();
        Assert.Equal(81,ids.Length);
        var registry=await EngineRegistry.Create(new(),Path.Combine(AppContext.BaseDirectory,"media"));
        try
        {
            var operations=registry.Engines.SelectMany(e=>e.Operations).ToArray();
            foreach(string id in ids)Assert.Contains(operations,o=>o.Id==id);
            Assert.NotNull(registry.ImageEditors);Assert.NotNull(registry.MediaEditors);Assert.NotNull(registry.PdfEditors);Assert.NotNull(registry.ImageMetadata);
            foreach(var operation in operations.Where(o=>ids.Contains(o.Id)&&o.NeedsOptions&&o.Mode==OperationMode.Tools))
                Assert.True(registry.ImageEditors.Routes.TryGet(operation.Id,out _)||registry.MediaEditors.Routes.TryGet(operation.Id,out _)||registry.PdfEditors.Routes.TryGet(operation.Id,out _)||registry.ImageMetadata.Routes.TryGet(operation.Id,out _)||operation.Id=="video.crop",operation.Id+" lacks a typed editor route.");
            Assert.DoesNotContain(operations,o=>o.OutputExtension==".pages");
            Assert.Contains(operations,o=>o.Id=="document.convert.docx"&&o.Label.Contains("Pages compatible",StringComparison.Ordinal));
        }
        finally{registry.ImageEditors?.Dispose();}
    }
}
